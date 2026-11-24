#!/usr/bin/env python3
"""カメラ 2 台を同時に動かして、「それぞれのカメラが台のどこを見ているか」を確かめる道具。

画面の上にカメラの映像を 2 つ並べ、下に台を真上から見た図を出す。
図の色：青 = カメラ 1 だけ、橙 = カメラ 2 だけ、紫 = 両方、灰色 = どちらも見えない。
認識（YOLO）も 2 台それぞれで動かし、見つけた物を図の上に点で出す（2 台の位置が合うかを見る）。

使い方（Windows）:
  .venv\\Scripts\\python camera\\two_cameras.py                 # カメラ 0 と 1
  .venv\\Scripts\\python camera\\two_cameras.py --cams 1 2      # 番号を変える
  .venv\\Scripts\\python camera\\two_cameras.py --cams 0        # 1 台だけで試す
  .venv\\Scripts\\python camera\\two_cameras.py --cams a.mp4 b.mp4   # 録画で試す

最初に、カメラごとに台の 4 隅をクリックして合わせる（1 / 2 キー）。
台の角が写っていないときは、t キーで「コンロ」「電子レンジ」など写っている置き場に替えて、その 4 隅で合わせる。
合わせた結果は camera/config/table_cam<番号>.json に残り、次からはそのまま使う。

キー:
  1 / 2 : カメラ 1 / 2 の位置合わせ（台の 4 隅をクリック）
  d     : 認識（YOLO）の ON / OFF
  p     : 今の画面を camera/captures/ に保存する
  q     : 終わる

注意: 見える範囲は「台の面（高さ 0）」で計算している。電子レンジのような高い物の陰は計算に入っていない。
"""
import argparse
import json
import platform
import sys
import time
from pathlib import Path

import cv2
import numpy as np

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import util  # noqa: E402
from util import draw_lines, draw_text  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent

# ---- 台と置き場の寸法（mm、台の左手前が原点、x は右・y は奥） ----
# 「左・右・手前・奥」は、台の前に立つ人から見た向き。
# 実物に合わせて camera/config/table_layout.json で変えられる。ファイルがなければ、設計図（tools/kitchen_plan.py の P）の数字を使う。
LAYOUT_PATH = ROOT / "camera" / "config" / "table_layout.json"
DESIGN_LAYOUT = {
    "台": [1050, 550],  # 横幅, 奥行き
    "置き場": {  # 名前: [x, y, 横幅, 奥行き]
        "まな板": [90, 90, 300, 220],
        "皿": [410, 100, 160, 160],
        "コンロ": [600, 60, 400, 300],
        "電子レンジ": [30, 320, 280, 200],
        "調味料": [400, 330, 180, 110],
    },
}


def load_layout():
    """台の大きさと置き場の位置を読む"""
    if LAYOUT_PATH.exists():
        return json.loads(LAYOUT_PATH.read_text(encoding="utf-8"))
    return DESIGN_LAYOUT


LAYOUT = load_layout()
TABLE_W, TABLE_D = LAYOUT["台"]
AREAS = {name: tuple(rect) for name, rect in LAYOUT["置き場"].items()}


def pan_circle():
    """フライパン（中心 x, y, 半径）。コンロの右の口にのせる"""
    if "コンロ" not in AREAS:
        return None
    x, y, w, d = AREAS["コンロ"]
    return (x + w * 0.75, y + d / 2, min(90, w / 4, d / 2))


PAN = pan_circle()
CORNER_NAMES = ["左手前", "右手前", "右奥", "左奥"]  # 位置合わせでクリックする順番


def rect_corners(x, y, w, d):
    """四角の 4 隅を、クリックする順番（左手前 → 右手前 → 右奥 → 左奥）で返す"""
    return [(x, y), (x + w, y), (x + w, y + d), (x, y + d)]


# 位置合わせに使える四角（台の角が画面に入らないときは、写っている置き場の 4 隅で合わせる）
# 電子レンジ・調味料は高さがあるので、台に接している「下の角」をクリックする
REFERENCES = {"台": rect_corners(0, 0, TABLE_W, TABLE_D)}
REFERENCES.update({name: rect_corners(*rect) for name, rect in AREAS.items()})
CORNERS = list(zip(CORNER_NAMES, REFERENCES["台"]))  # 台の 4 隅（線を描くのに使う）

CAM_COLORS = [(230, 140, 40), (40, 150, 255)]  # BGR。カメラ 1 = 青、カメラ 2 = 橙
BOTH_COLOR = (200, 80, 170)  # 両方 = 紫
NONE_COLOR = (70, 70, 70)  # どちらも見えない = 灰色
VIEW_H = 360  # 並べるときのカメラ映像の高さ
MAP_SCALE = 0.9  # 真上の図の 1 mm あたりの画素
MAP_MARGIN = 30
GRID_MM = 5  # 見える範囲を調べる細かさ


# ======================== カメラ 1 台分 ========================
class Camera:
    """カメラ（または動画）1 台と、その位置合わせ（台の mm → 画像の画素）"""

    def __init__(self, source, number, width, height, name=None):
        self.source = source
        self.number = number  # 並べる順番（1 か 2）。位置合わせのキーもこの番号
        self.name = name or f"カメラ{number}"  # 画面に出す名前（例：カメラ3（センター））
        self.cap = open_capture(source, width, height)
        self.frame = None
        self.size = (width, height)
        self.calib_path = ROOT / "camera" / "config" / f"table_cam{source_name(source)}.json"
        self.table_to_image = None  # 3x3 の射影変換
        self.reference = "台"  # 位置合わせに使った四角の名前
        self.load_calibration()

    def read(self):
        ok, frame = self.cap.read()
        if not ok and not str(self.source).isdigit():  # 動画は最初に戻す
            self.cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
            ok, frame = self.cap.read()
        if ok:
            self.frame = frame
            self.size = (frame.shape[1], frame.shape[0])
            self.fail_count = 0
        else:
            self.reconnect_if_lost()
        return ok

    def reconnect_if_lost(self):
        """読めない回数が続いたら、カメラが抜かれたとみなして開き直す（抜き差しに強くする）"""
        self.fail_count = getattr(self, "fail_count", 0) + 1
        if self.fail_count < 30 or not str(self.source).isdigit():
            return
        print(f"{self.name} が読めないので、つなぎ直します")
        self.cap.release()
        try:
            self.cap = open_capture(self.source, *self.size)
        except SystemExit:
            pass  # まだつながっていない。次の 30 回でまた試す
        self.fail_count = 0

    # ---- 位置合わせ ----
    def load_calibration(self):
        if self.calib_path.exists():
            data = json.loads(self.calib_path.read_text(encoding="utf-8"))
            reference = data.get("reference", "台")
            if reference in REFERENCES:  # 置き場の名前が table_layout.json から消えていたら、合わせ直してもらう
                self.set_clicks(data["clicks"], reference)

    def set_clicks(self, clicks, reference="台"):
        """クリックした 4 点（画像の画素）と、その四角の本当の位置（mm）から、台 → 画像 の変換を作る"""
        self.reference = reference
        real_mm = np.float32(REFERENCES[reference])
        H = cv2.getPerspectiveTransform(real_mm, np.float32(clicks))
        # 台の真ん中で w が正になるように向きをそろえる（あとで「カメラの前か後ろか」を w の符号で見るため）
        if (H @ [TABLE_W / 2, TABLE_D / 2, 1])[2] < 0:
            H = -H
        self.table_to_image = H
        self.mask = None  # 見える範囲は、位置合わせが変わったときだけ計算し直す

    def save_calibration(self, clicks):
        self.calib_path.parent.mkdir(parents=True, exist_ok=True)
        data = {"frame": list(self.size), "reference": self.reference, "clicks": [list(map(int, p)) for p in clicks]}
        self.calib_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    @property
    def calibrated(self):
        return self.table_to_image is not None

    # ---- 変換 ----
    def image_to_table(self, pt):
        """画像の画素 → 台の mm"""
        p = np.linalg.inv(self.table_to_image) @ [pt[0], pt[1], 1]
        return p[0] / p[2], p[1] / p[2]

    def table_to_pixels(self, pts_mm):
        """台の mm の点の並び → 画像の画素の並び"""
        pts = np.float32(pts_mm).reshape(-1, 1, 2)
        return cv2.perspectiveTransform(pts, self.table_to_image).reshape(-1, 2)

    def visible_mask(self):
        """台を GRID_MM ごとのます目にして、このカメラの画面に入っているます目を True にする"""
        if getattr(self, "mask", None) is None:
            self.mask = self.compute_visible_mask()
        return self.mask

    def compute_visible_mask(self):
        xs = np.arange(GRID_MM / 2, TABLE_W, GRID_MM)
        ys = np.arange(GRID_MM / 2, TABLE_D, GRID_MM)
        gx, gy = np.meshgrid(xs, ys)  # 行 = y（手前から奥）、列 = x
        p = np.stack([gx, gy, np.ones_like(gx)], axis=-1) @ self.table_to_image.T
        w = p[..., 2]
        u = p[..., 0] / np.where(w == 0, 1e-9, w)
        v = p[..., 1] / np.where(w == 0, 1e-9, w)
        width, height = self.size
        return (w > 0) & (u >= 0) & (u < width) & (v >= 0) & (v < height)


def source_name(source):
    """設定ファイルの名前に使う文字（カメラ番号、または動画の名前）"""
    return str(source) if str(source).isdigit() else Path(source).stem


def open_capture(source, width, height):
    if not str(source).isdigit():
        cap = cv2.VideoCapture(str(source))
    else:
        backend = cv2.CAP_DSHOW if platform.system() == "Windows" else cv2.CAP_ANY
        cap = cv2.VideoCapture(int(source), backend)
        # 圧縮（MJPG）で受け取る。圧縮なし（YUY2）だと 1280x720 は 1 秒に 5 枚しか来ない。
        # Windows（DirectShow）では、大きさを先に決めてから MJPG にしないと効かない（順番が逆だと YUY2 のまま）
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    if not cap.isOpened():
        raise SystemExit(f"カメラ（入力）を開けません: {source}")
    return cap


# ======================== 位置合わせの画面 ========================
def run_calibration(cam):
    """台（または置き場）の 4 隅を決まった順にクリックしてもらう。
    t で使う四角を替える、r でやり直し、Enter で決定、q でやめる"""
    window = f"calibration cam{cam.number}"  # OpenCV の窓の名前は英数字にしておく（日本語は文字化けする）
    clicks = []
    names = list(REFERENCES)
    reference = cam.reference

    def on_mouse(event, x, y, flags, _):
        if event == cv2.EVENT_LBUTTONDOWN and len(clicks) < 4:
            clicks.append((x, y))

    cv2.namedWindow(window)
    cv2.setMouseCallback(window, on_mouse)
    while True:
        cam.read()
        view = cam.frame.copy()
        for i, p in enumerate(clicks):
            cv2.circle(view, p, 7, CAM_COLORS[cam.number - 1], -1)
            if i:
                cv2.line(view, clicks[i - 1], p, CAM_COLORS[cam.number - 1], 2)
        if len(clicks) == 4:
            draw_preview(view, clicks, reference)
            guide = f"赤い丸 = {reference}の左手前。線が本物に重なれば Enter、ずれていたら o で回す / m で裏返す"
        else:
            guide = f"{reference}の「{CORNER_NAMES[len(clicks)]}」の角をクリック（左右は台の前に立つ人から見た向き）"
        view = draw_texts(view, [
            (f"合わせる四角: 【{reference}】", (12, 10), (0, 255, 255), 34),
            (f"t で替える: {' → '.join(names)}", (12, 54), (0, 255, 255), 20),
            *text_lines([
                f"{cam.name}（{cam.number} キー）: {guide}",
                "カメラが奥から写しているときは、映像の上が手前・左右も逆になる。迷ったら角を順にクリックして o で回す",
                "電子レンジ・調味料は、台に接している下の角をクリック",
                "r: やり直し   o: 回す   m: 裏返す   Enter: 決定   q: やめる",
            ], 12, 86, size=20),
        ])
        cv2.imshow(window, view)
        key = cv2.waitKey(30) & 0xFF
        if key == ord("r"):
            clicks.clear()
        elif key == ord("t"):
            reference = names[(names.index(reference) + 1) % len(names)]
            clicks.clear()
        elif key == ord("o") and len(clicks) == 4:
            clicks.append(clicks.pop(0))  # どのクリックを「左手前」とみなすかを 1 つずらす（90 度回す）
        elif key == ord("m") and len(clicks) == 4:
            clicks[1], clicks[3] = clicks[3], clicks[1]  # 回る向きを逆にする（裏返す）
        elif key in (13, 10) and len(clicks) == 4:
            cam.set_clicks(clicks, reference)
            cam.save_calibration(clicks)
            break
        elif key in (ord("q"), 27):
            break
    cv2.destroyWindow(window)


def draw_preview(view, clicks, reference):
    """今のクリックで合わせたら、台と置き場がどこに来るかを映像に重ねる（決める前の確かめ用）"""
    H = cv2.getPerspectiveTransform(np.float32(REFERENCES[reference]), np.float32(clicks))

    def polygon(pts_mm, color, thickness):
        pts = cv2.perspectiveTransform(np.float32(pts_mm).reshape(-1, 1, 2), H).reshape(-1, 2)
        cv2.polylines(view, [np.int32(pts)], True, color, thickness, cv2.LINE_AA)
        return pts

    polygon(REFERENCES["台"], (255, 255, 255), 2)
    for name, rect in AREAS.items():
        polygon(rect_corners(*rect), (0, 255, 255) if name == reference else (255, 255, 255), 2)
    # 台の「左手前」の角に印（どちらが手前かわかるように）
    corner = cv2.perspectiveTransform(np.float32([[REFERENCES[reference][0]]]), H)[0][0]
    cv2.circle(view, (int(corner[0]), int(corner[1])), 14, (0, 0, 255), 3)


# ======================== 描く ========================
# 日本語の文字は、線や色を全部描いたあとに、まとめて 1 回で描く（1 つずつ描くと遅い）。
# 文字は (文字, (x, y), 色, 大きさ) の並びで集めておく。

def draw_texts(img, texts):
    """集めた文字を、PIL への変換 1 回でまとめて描く"""
    if util._FONT is None:
        for text, xy, color, size in texts:
            img = draw_text(img, text, xy, color, size)
        return img
    pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
    canvas = ImageDraw.Draw(pil)
    for text, (x, y), color, size in texts:
        font = util._FONT.font_variant(size=size)
        canvas.text((x + 2, y + 2), text, font=font, fill=(0, 0, 0))
        canvas.text((x, y), text, font=font, fill=color[::-1])
    return cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)


def text_lines(lines, x, y, color=(255, 255, 255), size=18, gap=5):
    """複数行の文字を、上から順に並べた形にする"""
    return [(line, (x, y + i * (size + gap)), color, size) for i, line in enumerate(lines)]


def draw_camera_view(cam, dets):
    """カメラの映像（縮めたもの）に、台と置き場の線・認識した物の枠を重ねる。(画像, 文字) を返す"""
    scale = VIEW_H / cam.frame.shape[0]
    view = cv2.resize(cam.frame, (int(cam.frame.shape[1] * scale), VIEW_H))
    color = CAM_COLORS[cam.number - 1]
    texts = []

    def polygon(pts_mm, line_color, thickness):
        pts = cam.table_to_pixels(pts_mm) * scale
        cv2.polylines(view, [np.int32(pts)], True, line_color, thickness, cv2.LINE_AA)

    if cam.calibrated:
        polygon([mm for _, mm in CORNERS], color, 2)  # 台全体
        for rect in AREAS.values():
            polygon(rect_corners(*rect), (255, 255, 255), 1)
        polygon(REFERENCES[cam.reference], (0, 255, 255), 2)  # 位置合わせに使った四角（黄色）
    for d in dets:
        x1, y1, x2, y2 = (int(v * scale) for v in (d.x1, d.y1, d.x2, d.y2))
        cv2.rectangle(view, (x1, y1), (x2, y2), (90, 220, 60), 2)
        texts.append((f"{d.cls} {d.conf:.2f}", (x1, max(0, y1 - 20)), (90, 220, 60), 16))
    if cam.calibrated:
        label = f"{cam.number}: {cam.name}（入力 {cam.source}）  合わせた四角: {cam.reference}"
    else:
        label = f"{cam.number}: {cam.name}（入力 {cam.source}）  未設定：{cam.number} キーで位置合わせ"
    texts.append((label, (8, 8), color, 18))
    return view, texts


def to_map(x_mm, y_mm):
    """台の mm → 真上の図の画素（手前を下にする）"""
    return int(MAP_MARGIN + x_mm * MAP_SCALE), int(MAP_MARGIN + (TABLE_D - y_mm) * MAP_SCALE)


def coverage_image(masks):
    """ます目ごとに、見えているカメラで色をつけた画像を作る（手前が下）"""
    ny, nx = masks[0].shape if masks else (int(TABLE_D / GRID_MM), int(TABLE_W / GRID_MM))
    img = np.full((ny, nx, 3), NONE_COLOR, np.uint8)
    if len(masks) >= 1:
        img[masks[0]] = CAM_COLORS[0]
    if len(masks) >= 2:
        img[masks[1] & ~masks[0]] = CAM_COLORS[1]
        img[masks[1] & masks[0]] = BOTH_COLOR
    img = cv2.resize(img, (int(TABLE_W * MAP_SCALE), int(TABLE_D * MAP_SCALE)), interpolation=cv2.INTER_NEAREST)
    return img[::-1]  # 行 0 が手前なので、上下を返して奥を上にする


def area_ratio(mask, x, y, w, d):
    """mask のうち、置き場（mm の四角）の中で True の割合"""
    c0, c1 = int(x / GRID_MM), int((x + w) / GRID_MM)
    r0, r1 = int(y / GRID_MM), int((y + d) / GRID_MM)
    part = mask[r0:r1, c0:c1]
    return float(part.mean()) if part.size else 0.0


def draw_map(cams, dets_per_cam, width):
    """台を真上から見た図：見える範囲の色分け・置き場の線・認識した物の点・割合の表。(画像, 文字) を返す"""
    masks = [c.visible_mask() for c in cams if c.calibrated]
    map_w = int(TABLE_W * MAP_SCALE) + MAP_MARGIN * 2
    map_h = int(TABLE_D * MAP_SCALE) + MAP_MARGIN * 2
    canvas = np.full((map_h, map_w + 460, 3), 30, np.uint8)
    canvas[MAP_MARGIN:MAP_MARGIN + int(TABLE_D * MAP_SCALE), MAP_MARGIN:MAP_MARGIN + int(TABLE_W * MAP_SCALE)] = coverage_image(masks)
    texts = []

    for name, (x, y, w, d) in AREAS.items():
        cv2.rectangle(canvas, to_map(x, y + d), to_map(x + w, y), (255, 255, 255), 1)
        texts.append((name, (to_map(x, y + d)[0] + 4, to_map(x, y + d)[1] + 2), (255, 255, 255), 14))
    if PAN:
        cv2.circle(canvas, to_map(PAN[0], PAN[1]), int(PAN[2] * MAP_SCALE), (255, 255, 255), 1)
    texts.append(("手前（人が立つ側）", (MAP_MARGIN, map_h - MAP_MARGIN + 6), (200, 200, 200), 14))
    texts.append(("奥", (MAP_MARGIN, 8), (200, 200, 200), 14))

    # 認識した物を、カメラの色の点で出す（2 台の点が近ければ位置が合っている）
    for cam, dets in zip(cams, dets_per_cam):
        if not cam.calibrated:
            continue
        for d in dets:
            x, y = cam.image_to_table(d.center)
            if -100 < x < TABLE_W + 100 and -100 < y < TABLE_D + 100:
                cv2.circle(canvas, to_map(x, y), 7, CAM_COLORS[cam.number - 1], -1)
                cv2.circle(canvas, to_map(x, y), 7, (255, 255, 255), 1)

    texts += text_lines(summary_lines(cams, masks), x=map_w + 10, y=MAP_MARGIN, size=16)
    return canvas, texts


def summary_lines(cams, masks):
    """見えている割合を文字にする"""
    if not masks:
        return ["位置合わせをすると、見える範囲がここに出ます", "1 / 2 キーで台の 4 隅をクリック"]
    names = [c.name for c in cams if c.calibrated]
    lines = ["見えている割合（台の面）"]
    either = np.logical_or.reduce(masks)
    both = np.logical_and.reduce(masks) if len(masks) >= 2 else None
    lines.append("台全体: " + "  ".join(f"{n} {m.mean():.0%}" for n, m in zip(names, masks)))
    lines.append("　　　  " + (f"両方 {both.mean():.0%}  " if both is not None else "") + f"どれか {either.mean():.0%}")
    for name, rect in AREAS.items():
        parts = [f"{n} {area_ratio(m, *rect):.0%}" for n, m in zip(names, masks)]
        if both is not None:
            parts.append(f"両方 {area_ratio(both, *rect):.0%}")
        lines.append(f"{name}: " + "  ".join(parts))
    legend = "  ".join(f"{color} = {c.name}だけ" for color, c in zip(["青", "橙"], cams))
    lines += ["", legend, "紫 = 両方  灰色 = 見えない", "点 = 認識した物（カメラの色）",
              "カメラの映像の黄色の線 = 合わせた四角"]
    return lines


def side_by_side(views):
    """カメラの映像を横に並べる。(画像, 文字) の並び → まとめた (画像, 文字)"""
    images, texts, x = [], [], 0
    for image, view_texts in views:
        images.append(image)
        texts += [(t, (px + x, py), c, s) for t, (px, py), c, s in view_texts]
        x += image.shape[1]
    return np.hstack(images), texts


def stack_top_bottom(top, bottom):
    """上の段（カメラ）と下の段（真上の図）を縦につなぐ。下の段は、上の段の幅に合わせて縮める"""
    (top_img, top_texts), (bottom_img, bottom_texts) = top, bottom
    width = top_img.shape[1]
    scale = min(1.0, width / bottom_img.shape[1])
    if scale < 1.0:
        bottom_img = cv2.resize(bottom_img, (width, int(bottom_img.shape[0] * scale)), interpolation=cv2.INTER_AREA)
    bottom_img = np.pad(bottom_img, ((0, 0), (0, width - bottom_img.shape[1]), (0, 0)))
    # 文字は縮めた画像の上に、あとから描く（位置と大きさも同じだけ縮める。小さくなりすぎないようにする）
    shifted = [(t, (int(x * scale), int(y * scale) + top_img.shape[0]), c, max(12, int(s * scale)))
               for t, (x, y), c, s in bottom_texts]
    return np.vstack([top_img, bottom_img]), top_texts + shifted


def fit_to_screen(img):
    """窓が画面からはみ出さないように縮める"""
    try:
        import ctypes

        screen_w = ctypes.windll.user32.GetSystemMetrics(0)
        screen_h = ctypes.windll.user32.GetSystemMetrics(1)
    except (AttributeError, OSError):  # Windows 以外
        screen_w, screen_h = 1600, 900
    scale = min(1.0, (screen_w - 40) / img.shape[1], (screen_h - 120) / img.shape[0])
    if scale >= 1.0:
        return img
    return cv2.resize(img, (int(img.shape[1] * scale), int(img.shape[0] * scale)), interpolation=cv2.INTER_AREA)


# ======================== 本体 ========================
def make_detector(args):
    if args.no_detect:
        return None
    from onnx_detect import OnnxDetector

    detector = OnnxDetector(args.model, args.conf)  # GPU（DirectML）があれば GPU で動く
    print("認識のクラス:", list(detector.names.values()), "動かす所:", detector.device)
    return detector


def main():
    ap = argparse.ArgumentParser(description="カメラ 2 台の見える範囲と認識を確かめる")
    ap.add_argument("--cams", nargs="+", default=["0", "1"], help="カメラ番号か動画ファイル（1〜2 個）")
    ap.add_argument("--model", default=str(ROOT / "models" / "best.onnx"))
    ap.add_argument("--conf", type=float, default=0.4)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--no-detect", action="store_true", help="認識（YOLO）を使わない")
    ap.add_argument("--names", nargs="+", default=[], help="画面に出すカメラの名前（--cams と同じ順番）。例：--names カメラ1 カメラ3（センター）")
    args = ap.parse_args()

    names = args.names + [None] * 2
    cams = [Camera(src, i + 1, args.width, args.height, names[i]) for i, src in enumerate(args.cams[:2])]
    for cam in cams:
        if not cam.read():
            raise SystemExit(f"{cam.name}（{cam.source}）から映像が来ません")
        print(f"{cam.name}: 入力 {cam.source}  {cam.size[0]}x{cam.size[1]}")
    detector = make_detector(args)
    detect_on = detector is not None
    fps, last = 0.0, time.time()

    timer = StageTimer()
    while True:
        timer.start()
        for cam in cams:
            cam.read()
        timer.lap("読む")
        dets_per_cam = [detector(cam.frame) if detect_on else [] for cam in cams]
        timer.lap("認識")

        top = side_by_side([draw_camera_view(c, d) for c, d in zip(cams, dets_per_cam)])
        bottom = draw_map(cams, dets_per_cam, top[0].shape[1])
        screen, texts = stack_top_bottom(top, bottom)

        now = time.time()
        fps = 0.9 * fps + 0.1 / max(1e-3, now - last)
        last = now
        status = (f"FPS {fps:4.1f}（{timer.summary()}）  認識 {'ON' if detect_on else 'OFF'}"
                  "   1/2: 位置合わせ  d: 認識  p: 保存  q: 終わる")
        texts.append((status, (12, VIEW_H + 6), (255, 255, 255), 16))
        screen = fit_to_screen(draw_texts(screen, texts))
        timer.lap("描く")
        cv2.imshow("two cameras", screen)

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key in (ord("1"), ord("2")) and int(chr(key)) <= len(cams):
            run_calibration(cams[int(chr(key)) - 1])
        if key == ord("d") and detector is not None:
            detect_on = not detect_on
        if key == ord("p"):
            save_screen(screen, cams)

    for cam in cams:
        cam.cap.release()
    cv2.destroyAllWindows()


class StageTimer:
    """1 回のループの中で、どこに何 ms かかっているかを測る（遅いときの原因さがし用）"""

    def __init__(self):
        self.ms = {}
        self.t = time.time()

    def start(self):
        self.t = time.time()

    def lap(self, name):
        now = time.time()
        self.ms[name] = 0.8 * self.ms.get(name, 0) + 0.2 * (now - self.t) * 1000  # なめらかにする
        self.t = now

    def summary(self):
        return " ".join(f"{name} {ms:.0f}ms" for name, ms in self.ms.items())


def save_screen(screen, cams):
    """今の画面と、カメラの生の映像を保存する（あとで見比べる用）"""
    folder = ROOT / "camera" / "captures"
    folder.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%d_%H%M%S")
    cv2.imwrite(str(folder / f"{stamp}_screen.jpg"), screen)
    for cam in cams:
        cv2.imwrite(str(folder / f"{stamp}_cam{cam.number}.jpg"), cam.frame)
    print("保存しました:", folder / f"{stamp}_screen.jpg")


if __name__ == "__main__":
    main()
