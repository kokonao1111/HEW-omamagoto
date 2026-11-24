#!/usr/bin/env python3
"""カメラで見つけた野菜（ピーマン・ブロッコリー）を、Unity のコンロの上に出す（試し用。2026-11-24）。

しくみ:
  学習モデル（models/test_green.onnx：pepper・broccoli）で、映像の中の野菜を見つける（GPU があれば GPU）。
  映像の中の位置を、コンロの天板の上の位置（u：左 0 → 右 1、v：手前 0 → 奥 1）に直して、今見えている物の一覧を送る:
    {"event": "items", "zone": "stove", "items": [{"item": "pepper", "x": 0.3, "y": 0.6}, ...]}
  送り先は ws://localhost:9000/camera（door_watch.py の「サーバーの代わり」が、Unity に配る）。
  なので、先に door_watch.py を動かしておく（扉を見ないなら door_watch.py --no-carrot でも何でもよい）。

映像とコンロの向き:
  本物のコンロはまだないので、はじめは映像全体をコンロの天板とみなす。
  カメラは台を横（上）から見ていて、映像の上が台の左はし（電子レンジのがわ）、映像の左が台の手前、映像の右が台の奥、とする。
  ちがっていたら、o（90 度回す）・m（裏返す）で合わせる。本物のコンロができたら、c で天板の 4 隅をクリックして合わせる。

使い方（Windows）:
  .venv\\Scripts\\python camera\\veg_watch.py --camera 2

キー（窓で押す）:
  o : 向きを 90 度回す   m : 裏返す
  p : 今見えている野菜の所を、Unity のフライパンの真ん中にする（映像のはしは動かさずに、そこだけ合わせる）
  c : コンロの天板の 4 隅をクリック（左手前 → 右手前 → 右奥 → 左奥。左右・手前は台の前に立つ人から見た向き）
  r : 4 隅をやめて、映像全体に戻す
  q : 終わる
"""
import argparse
import json
import platform
import queue
import sys
import threading
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from alignment import CameraAlignment, transform_points  # noqa: E402
from door_watch import draw_text_lines  # noqa: E402
from onnx_detect import OnnxDetector  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = ROOT / "camera" / "config" / "veg_stove.json"
REFERENCE_PATH = ROOT / "camera" / "config" / "veg_stove_reference.png"  # 4 隅を決めたときの映像（カメラのずれを合わせる用）
FOLLOW_SECONDS = 2.0  # これだけおきに、カメラのずれを計算し直す
SNAPSHOT_PATH = ROOT / "camera" / "captures" / "veg_view.jpg"  # 今の画面（1 秒ごとに上書き。窓が見えないときに確かめる用）
CORNER_NAMES = ["左手前", "右手前", "右奥", "左奥"]
# Unity のコンロ（KitchenLayout と同じ数字。2026-11-24 に実物の台に合わせた：1 口、台の手前から 20 mm の所から奥行き 300 mm）
STOVE_Y, STOVE_DEPTH = 20, 300
# Unity のフライパンの真ん中（コンロのあたりの位置。1 口なので左右の真ん中、奥行きの真ん中。StoveAreaToWorld と同じ決め方）
PAN_CENTER = (0.5, (STOVE_Y + STOVE_DEPTH / 2) / (STOVE_Y + STOVE_DEPTH))
COLORS = {"pepper": (40, 160, 40), "broccoli": (90, 230, 120)}
DISPLAY_SCALE = 0.6


# ======================== 映像の位置 → コンロの上の位置 ========================
class StoveMapping:
    """映像の画素 → コンロの天板の上の位置（u, v。0〜1）。
    4 隅をクリックしてあればその四角を、なければ映像全体を天板とし、向き（orientation）で回す・裏返す。
    4 隅を決めたときの映像を覚えておき、カメラが動いたら 4 隅を自動でずらす（follow_camera）"""

    def __init__(self, frame_size):
        self.width, self.height = frame_size
        self.orientation = 0  # 0〜7（4 通りの回転 × 裏返すかどうか）
        self.reference_corners = None  # 4 隅を決めたときの [左手前, 右手前, 右奥, 左奥]（画素）
        self.corners = None  # 今のカメラの向きでの 4 隅（reference_corners をカメラのずれの分だけずらしたもの）
        self.anchor = None  # この位置（合わせる前の u, v）を、フライパンの真ん中に合わせる
        self.alignment = CameraAlignment()
        self.has_reference = False
        self.aligned_ok = False
        self.load()

    def follow_camera(self, frame):
        """今の映像と、4 隅を決めたときの映像を見比べて、4 隅をずらす。
        4 隅を決めたときの映像がまだなければ（前の版で決めた 4 隅など）、今の映像を覚える"""
        if self.reference_corners is None:
            return
        if not self.has_reference:
            self._remember_reference(frame)
            return
        transform = self.alignment.find(frame)
        self.aligned_ok = transform is not None
        if transform is not None:
            self.corners = transform_points(self.reference_corners, transform)

    def _remember_reference(self, frame):
        REFERENCE_PATH.parent.mkdir(parents=True, exist_ok=True)
        cv2.imwrite(str(REFERENCE_PATH), frame)
        self.alignment.set_reference(frame)
        self.has_reference = True

    def to_stove(self, point):
        """映像の画素 → Unity に送る位置 (u, v)。
        u：コンロの左 0 → 右 1。v：台のいちばん手前 0 → コンロの奥 1（Unity の StoveAreaToWorld と同じ決め方）。
        4 隅をクリックしてあれば、その四角をコンロの天板とし、四角より手前は台の上になる。
        フライパンの真ん中に合わせた所があれば、そこがフライパンの真ん中に来るようにゆがめる"""
        u, v = self.to_stove_raw(point)
        if self.corners is not None:
            v = (STOVE_Y + v * STOVE_DEPTH) / (STOVE_Y + STOVE_DEPTH)  # 天板の上（0〜1）→ 台の手前からの位置
        if self.anchor is None:
            return u, v
        return bend(u, self.anchor[0], PAN_CENTER[0]), bend(v, self.anchor[1], PAN_CENTER[1])

    def set_anchor(self, raw_point):
        """今の位置（合わせる前の u, v）を、フライパンの真ん中にする"""
        self.anchor = [float(raw_point[0]), float(raw_point[1])]
        self.save()

    def to_stove_raw(self, point):
        """映像の画素 → (u, v)（フライパンに合わせる前）。4 隅があれば天板の上の位置（外なら 0〜1 からはみ出す）、なければ映像全体での位置"""
        if self.corners is not None:
            transform = cv2.getPerspectiveTransform(np.float32(self.corners), np.float32([[0, 0], [1, 0], [1, 1], [0, 1]]))
            u, v = cv2.perspectiveTransform(np.float32([[point]]), transform)[0][0]
            return float(u), float(v)
        a, b = point[0] / self.width, point[1] / self.height  # 映像の左 0 → 右 1、上 0 → 下 1
        return self._orient(a, b)

    def _orient(self, a, b):
        """映像全体のときの向き。はじめ（0）は「映像の上 = 台の左（電子レンジのがわ）、映像の左 = 台の手前、映像の右 = 台の奥」
        （カメラ 2 は台を横（上）から見ている。2026-11-24 にユーザーに確かめた）"""
        u, v = b, a
        for _ in range(self.orientation % 4):
            u, v = v, 1 - u  # 90 度回す
        if self.orientation >= 4:
            u = 1 - u  # 左右を裏返す
        return u, v

    def corner_pixels(self):
        """天板の 4 隅（左手前・右手前・右奥・左奥）が、映像のどこに来るか（線を描く用）"""
        if self.corners is not None:
            return [tuple(map(int, c)) for c in self.corners]
        # 映像の四隅が、天板のどの角にあたるかを調べて並べる
        image_corners = [(0, 0), (self.width - 1, 0), (self.width - 1, self.height - 1), (0, self.height - 1)]
        by_stove = {}
        for x, y in image_corners:
            u, v = self.to_stove_raw((x, y))
            by_stove[(round(u), round(v))] = (x, y)
        return [by_stove[(0, 0)], by_stove[(1, 0)], by_stove[(1, 1)], by_stove[(0, 1)]]

    def rotate(self):
        self.orientation = (self.orientation // 4) * 4 + (self.orientation + 1) % 4
        self.save()

    def mirror(self):
        self.orientation = (self.orientation + 4) % 8
        self.save()

    def set_corners(self, corners, frame=None):
        """4 隅を決める（None なら映像全体に戻す）。frame を渡すと、その映像を「4 隅を決めたときの映像」として覚える"""
        self.reference_corners = corners
        self.corners = corners
        if corners is not None and frame is not None:
            self._remember_reference(frame)
        self.save()

    def save(self):
        CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        data = {"orientation": self.orientation, "corners": self.reference_corners, "anchor": self.anchor}
        CONFIG_PATH.write_text(json.dumps(data, indent=2), encoding="utf-8")

    def load(self):
        if CONFIG_PATH.exists():
            data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            self.orientation = data.get("orientation", 0)
            self.reference_corners = self.corners = data.get("corners")
            self.anchor = data.get("anchor")
        if REFERENCE_PATH.exists() and self.reference_corners is not None:
            self.alignment.set_reference(cv2.imread(str(REFERENCE_PATH)))
            self.has_reference = True


def bend(value, source, target):
    """0〜1 の値を、両はし（0 と 1）は動かさずに、source が target に来るようにゆがめる（折れ線）"""
    source = min(max(source, 0.01), 0.99)
    if value <= source:
        return value / source * target
    return target + (value - source) / (1 - source) * (1 - target)


def best_per_kind(detections, per_kind):
    """野菜の種類ごとに、いちばん自信のある物から per_kind 個だけ残す。
    同じ所が 2 つの種類に見えたとき（ピーマンの上にブロッコリーの枠が重なる など）は、自信の高いほうだけ残す"""
    kept = []
    for d in sorted(detections, key=lambda d: d.conf, reverse=True):
        same_kind = sum(1 for k in kept if k.cls == d.cls)
        if same_kind >= per_kind or any(overlap(d, k) > 0.5 for k in kept):
            continue
        kept.append(d)
    return kept


def overlap(a, b):
    """2 つの枠が、小さいほうの何割くらい重なっているか（0〜1）"""
    width = max(0.0, min(a.x2, b.x2) - max(a.x1, b.x1))
    height = max(0.0, min(a.y2, b.y2) - max(a.y1, b.y1))
    smaller = min((a.x2 - a.x1) * (a.y2 - a.y1), (b.x2 - b.x1) * (b.y2 - b.y1))
    return width * height / smaller if smaller > 0 else 0.0


# ======================== 送る ========================
class CameraSender:
    """ws://localhost:9000/camera につないで、イベントを送る。切れたらつなぎ直す。別のスレッドで動く"""

    def __init__(self, url):
        self.url = url
        self.queue = queue.Queue(maxsize=1)  # 古いものはためない（いちばん新しい一覧だけ送ればよい）
        self.connected = False
        threading.Thread(target=self._run, daemon=True).start()

    def send(self, body):
        try:
            self.queue.get_nowait()
        except queue.Empty:
            pass
        self.queue.put(body)

    def _run(self):
        from websockets.sync.client import connect

        seq = 0
        while True:
            try:
                with connect(self.url) as connection:
                    self.connected = True
                    while True:
                        body = self.queue.get()
                        seq += 1
                        connection.send(json.dumps({"device": "veg-test", "ts": int(time.time() * 1000), "seq": seq, **body}))
            except Exception:  # noqa: BLE001  つながらない・切れた → 少し待ってつなぎ直す
                self.connected = False
                time.sleep(1)


# ======================== カメラの映像 ========================
class FrameGrabber:
    """カメラの映像を、別のスレッドでずっと読み続ける。いつでもいちばん新しい 1 枚がもらえる。
    映像を待つ時間（1 枚 33 ms）と、学習モデルの時間を重ねられるので、1 秒に見つけられる回数が増える"""

    def __init__(self, cap):
        self.cap = cap
        self.frame = None
        self.number = 0  # 何枚目か（新しい映像が来たかを見分ける）
        self.lock = threading.Lock()
        self.new_frame = threading.Event()
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        while True:
            ok, frame = self.cap.read()
            if not ok:
                time.sleep(0.005)
                continue
            with self.lock:
                self.frame = frame
                self.number += 1
            self.new_frame.set()

    def next(self, last_number):
        """last_number より新しい映像が来るまで待って、(映像, 何枚目か) を返す"""
        while True:
            with self.lock:
                if self.number != last_number and self.frame is not None:
                    return self.frame, self.number
                self.new_frame.clear()
            self.new_frame.wait(0.1)


def follow_in_background(mapping, frame, busy):
    """カメラのずれ合わせ（少し重い）を、別のスレッドでする。前の計算が終わっていなければ、何もしない"""
    if busy.is_set():
        return
    busy.set()

    def run():
        try:
            mapping.follow_camera(frame)
        finally:
            busy.clear()

    threading.Thread(target=run, daemon=True).start()


# ======================== 画面 ========================
def open_camera(index, width, height):
    backend = cv2.CAP_DSHOW if platform.system() == "Windows" else cv2.CAP_ANY
    cap = cv2.VideoCapture(index, backend)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)  # 大きさを先に決めてから MJPG にする（逆だと Windows で遅い）
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    if not cap.isOpened():
        raise SystemExit(f"カメラ {index} を開けません")
    return cap


def follow_status(mapping):
    """カメラのずれを合わせられているかを 1 行で出す"""
    if mapping.reference_corners is None:
        return "コンロ：映像全体（4 隅は決めていない）"
    if mapping.aligned_ok:
        return f"コンロの 4 隅：カメラのずれに合わせた（目印 {mapping.alignment.matches} 点）"
    return f"コンロの 4 隅：合わせられない（目印 {mapping.alignment.matches} 点。前の位置のまま）"


def draw(frame, detections, items, mapping, sender, clicks, clicking, fps):
    view = frame.copy()
    corners = mapping.corner_pixels()
    cv2.polylines(view, [np.int32(corners)], True, (0, 200, 255), 2)
    cv2.circle(view, corners[0], 14, (0, 0, 255), 3)  # 左手前の角
    for d, item in zip(detections, items):
        color = COLORS.get(d.cls, (255, 255, 255))
        cv2.rectangle(view, (int(d.x1), int(d.y1)), (int(d.x2), int(d.y2)), color, 3)
        cv2.putText(view, f"{d.cls} {d.conf:.2f} ({item['x']:.2f},{item['y']:.2f})", (int(d.x1), max(20, int(d.y1) - 8)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)
    for point in clicks:
        cv2.circle(view, tuple(point), 8, (255, 0, 255), -1)
    view = cv2.resize(view, None, fx=DISPLAY_SCALE, fy=DISPLAY_SCALE, interpolation=cv2.INTER_AREA)
    if clicking:
        guide = f"クリック：コンロの天板の「{CORNER_NAMES[len(clicks)]}」（左右・手前は台の前に立つ人から見た向き）"
    else:
        guide = "o: 回す  m: 裏返す  p: 今の野菜の所をフライパンの真ん中に  c: 4 隅をクリック  r: 映像全体  q: 終わる"
    lines = [
        f"見えている野菜: {len(items)} 個   1 秒に {fps:.0f} 回   Unity へ: {'つながっている' if sender.connected else 'つながっていない（先に door_watch.py を）'}",
        follow_status(mapping),
        guide,
    ]
    return draw_text_lines(view, lines, size=16, gap=4)  # 文字はまとめて 1 回で描く（1 行ずつだと遅い）


def main():
    ap = argparse.ArgumentParser(description="カメラで見つけたピーマン・ブロッコリーを、Unity のコンロの上に出す（試し用）")
    ap.add_argument("--camera", type=int, default=2)
    ap.add_argument("--model", default=str(ROOT / "models" / "test_green.onnx"))
    ap.add_argument("--conf", type=float, default=0.4)
    ap.add_argument("--per-kind", type=int, default=1, help="野菜の種類ごとに出す数（いちばん自信のある物から）。はじめはピーマン 1 つ・ブロッコリー 1 つ")
    ap.add_argument("--server", default="ws://localhost:9000/camera")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    args = ap.parse_args()

    cap = open_camera(args.camera, args.width, args.height)
    detector = OnnxDetector(args.model, args.conf)
    print("学習モデル:", list(detector.names.values()), detector.device)
    mapping = StoveMapping((args.width, args.height))
    sender = CameraSender(args.server)

    window = f"vegetable camera {args.camera}"
    cv2.namedWindow(window)
    clicks = []
    clicking = {"on": False}  # c を押して、4 隅をクリックしている途中か

    def on_mouse(event, x, y, flags, _):
        if event == cv2.EVENT_LBUTTONDOWN and clicking["on"]:
            clicks.append([int(x / DISPLAY_SCALE), int(y / DISPLAY_SCALE)])  # 窓は縮めて出しているので、元の大きさに直す
            if len(clicks) == 4:
                mapping.set_corners([list(c) for c in clicks], latest["frame"])  # 今の映像も覚える（カメラのずれを合わせる用）
                clicks.clear()
                clicking["on"] = False

    cv2.setMouseCallback(window, on_mouse)
    last_snapshot = last_follow = 0.0
    latest = {"frame": None}
    grabber = FrameGrabber(cap)
    frame_number = 0
    following = threading.Event()  # カメラのずれ合わせを計算している途中か
    fps, last_time = 0.0, time.time()

    while True:
        frame, frame_number = grabber.next(frame_number)  # 新しい映像が来るまで待つ（読み込みは別のスレッド）
        latest["frame"] = frame
        # 起動したとき（カメラが落ち着いてから）と、そのあと 2 秒ごとに、カメラのずれを合わせて 4 隅をずらす（別のスレッドで）
        if frame_number >= 15 and time.time() - last_follow > FOLLOW_SECONDS:
            follow_in_background(mapping, frame, following)
            last_follow = time.time()
        detections = best_per_kind(detector(frame), args.per_kind)
        items = []
        for d in detections:
            u, v = mapping.to_stove(d.center)
            items.append({"item": d.cls, "x": round(min(max(u, 0.0), 1.0), 3), "y": round(min(max(v, 0.0), 1.0), 3)})
        sender.send({"event": "items", "zone": "stove", "items": items})  # 見つけるたびに送る

        now = time.time()
        fps = 0.9 * fps + 0.1 / max(now - last_time, 1e-3)
        last_time = now
        view = draw(frame, detections, items, mapping, sender, clicks, clicking["on"], fps)
        cv2.imshow(window, view)
        if time.time() - last_snapshot > 1.0:
            SNAPSHOT_PATH.parent.mkdir(parents=True, exist_ok=True)
            cv2.imwrite(str(SNAPSHOT_PATH), view)
            last_snapshot = time.time()

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord("o"):
            mapping.rotate()
        if key == ord("m"):
            mapping.mirror()
        if key == ord("c"):
            clicks.clear()
            clicking["on"] = True
        if key == ord("r"):
            mapping.set_corners(None)
        if key == ord("p") and detections:
            # 今見えている野菜の真ん中を、Unity のフライパンの真ん中にする
            raw = [mapping.to_stove_raw(d.center) for d in detections]
            mapping.set_anchor(np.mean(raw, axis=0))
            print("フライパンの真ん中に合わせた:", np.round(mapping.anchor, 3))

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
