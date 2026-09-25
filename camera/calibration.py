"""キャリブレーション: 画像上の「まな板」「フライパン」「皿」の 4 隅を登録し、
まな板の座標（0〜1）への変換とゾーン判定を行う。設定は JSON に保存する。"""
import json
from pathlib import Path

import cv2
import numpy as np

from util import draw_lines

ZONES = [("board", "まな板", True), ("pan", "フライパン", False), ("plate", "皿", False)]  # (id, 表示名, 必須)
ZONE_COLORS = {"board": (80, 255, 120), "pan": (60, 180, 255), "plate": (255, 200, 80)}
CORNER_NAMES = ["左上", "右上", "右下", "左下"]


class Calibration:
    def __init__(self, path: Path, frame_size):
        self.path = Path(path)
        self.w, self.h = frame_size
        self.corners = {}  # zone id -> [(x,y)*4] 画像ピクセル
        self.H = None  # まな板 → 単位正方形 の射影変換
        self.load()

    # ---------- 保存・読み込み ----------
    def load(self):
        if self.path.exists():
            data = json.loads(self.path.read_text(encoding="utf-8"))
            self.corners = {k: [tuple(p) for p in v] for k, v in data.get("zones", {}).items()}
        if "board" not in self.corners:
            # 未設定なら画面全体をまな板とみなす（警告つきで動かす）
            self.corners["board"] = [(0, 0), (self.w - 1, 0), (self.w - 1, self.h - 1), (0, self.h - 1)]
            self.is_default = True
        else:
            self.is_default = False
        self._update_homography()

    def save(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps({"frame": [self.w, self.h], "zones": self.corners}, ensure_ascii=False, indent=2), encoding="utf-8")
        self.is_default = False

    def _update_homography(self):
        src = np.float32(self.corners["board"])
        dst = np.float32([[0, 0], [1, 0], [1, 1], [0, 1]])
        self.H = cv2.getPerspectiveTransform(src, dst)

    # ---------- 変換・判定 ----------
    def to_board(self, pt):
        """画像ピクセル → まな板座標 (u, v)。0〜1 がまな板の内側"""
        p = cv2.perspectiveTransform(np.float32([[pt]]), self.H)[0][0]
        return float(p[0]), float(p[1])

    def zone_of(self, pt):
        """点がどのゾーンに入っているか（id）。どこにも入らなければ None。小さいゾーン優先"""
        best = None
        for zid, pts in self.corners.items():
            if cv2.pointPolygonTest(np.int32(pts), (float(pt[0]), float(pt[1])), False) >= 0:
                area = cv2.contourArea(np.int32(pts))
                if best is None or area < best[1]:
                    best = (zid, area)
        return best[0] if best else None

    def draw(self, img):
        for zid, pts in self.corners.items():
            color = ZONE_COLORS.get(zid, (200, 200, 200))
            cv2.polylines(img, [np.int32(pts)], True, color, 2)
            name = next((n for i, n, _ in ZONES if i == zid), zid)
            cv2.putText(img, zid, (pts[0][0] + 6, pts[0][1] + 22), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)
        return img

    # ---------- 対話式の設定 ----------
    def run_ui(self, cap, window="calibration"):
        """ゾーンごとに 4 隅をクリックさせる。Enter で次へ、r でやり直し、s で任意ゾーンを飛ばす、q で中断"""
        clicks = []

        def on_mouse(ev, x, y, flags, _):
            if ev == cv2.EVENT_LBUTTONDOWN and len(clicks) < 4:
                clicks.append((x, y))

        cv2.namedWindow(window)
        cv2.setMouseCallback(window, on_mouse)
        new = {}
        for zid, name, required in ZONES:
            clicks.clear()
            while True:
                ok, frame = cap.read()
                if not ok:
                    continue
                view = frame.copy()
                for prev, pts in new.items():
                    cv2.polylines(view, [np.int32(pts)], True, ZONE_COLORS[prev], 2)
                for i, p in enumerate(clicks):
                    cv2.circle(view, p, 6, ZONE_COLORS[zid], -1)
                    if i:
                        cv2.line(view, clicks[i - 1], p, ZONE_COLORS[zid], 2)
                nxt = CORNER_NAMES[len(clicks)] if len(clicks) < 4 else "Enter で確定"
                view = draw_lines(view, [
                    f"[{name}] の 4 隅を順にクリック: {nxt}",
                    "r: やり直し   Enter: 確定" + ("" if required else "   s: このゾーンは使わない") + "   q: 中断",
                ], color=ZONE_COLORS[zid])
                cv2.imshow(window, view)
                key = cv2.waitKey(30) & 0xFF
                if key == ord("r"):
                    clicks.clear()
                elif key == ord("s") and not required:
                    break
                elif key in (13, 10) and len(clicks) == 4:
                    new[zid] = list(clicks)
                    break
                elif key in (ord("q"), 27):
                    cv2.destroyWindow(window)
                    return False
        cv2.destroyWindow(window)
        self.corners = new
        self._update_homography()
        self.save()
        return True
