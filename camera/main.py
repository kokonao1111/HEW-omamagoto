#!/usr/bin/env python3
"""カメラ側のメインプログラム。
Web カメラ → YOLO で野菜・包丁を検出 → 手検出 → まな板座標に変換 → 切断/投入を判定 → JSON で送信。

使い方:
  .venv/bin/python camera/main.py                          # カメラ 0、サーバー無し（コンソールに出す）
  .venv/bin/python camera/main.py --camera 1 --server ws://omamagoto.local:9000
  .venv/bin/python camera/main.py --source test.mp4        # 動画ファイルで試す

キー:
  c : キャリブレーション（まな板・フライパン・皿の 4 隅をクリック）
  b : まな板の範囲だけを切り出して推論する（精度が上がる）ON/OFF
  q : 終了
"""
import argparse
import platform
import sys
import time
from collections import deque
from pathlib import Path

import cv2

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calibration import Calibration  # noqa: E402
from detect import Detector, HandDetector  # noqa: E402
from logic import GameLogic  # noqa: E402
from sender import Sender  # noqa: E402
from util import draw_lines, draw_text  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
COLORS = {"包丁": (60, 106, 255), "knife": (60, 106, 255)}
DEFAULT_COLOR = (90, 220, 60)


def open_source(src, width, height):
    if isinstance(src, str) and not src.isdigit():
        cap = cv2.VideoCapture(src)
    else:
        backend = cv2.CAP_DSHOW if platform.system() == "Windows" else cv2.CAP_ANY
        cap = cv2.VideoCapture(int(src), backend)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    if not cap.isOpened():
        raise SystemExit(f"入力を開けません: {src}")
    return cap


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--source", default=None, help="動画/画像ファイル（指定するとカメラの代わりに使う）")
    ap.add_argument("--model", default=str(ROOT / "models" / "best.onnx"))
    ap.add_argument("--conf", type=float, default=0.4)
    ap.add_argument("--server", default=None, help="ws://host:port（省略時はコンソール出力）")
    ap.add_argument("--calib", default=None, help="キャリブレーション JSON（既定: camera/config/calib_cam<N>.json）")
    ap.add_argument("--rate", type=float, default=20, help="位置送信の上限 回/秒")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--no-hand", action="store_true", help="手検出を使わない")
    ap.add_argument("--snapshot", default=None, help="このフォルダに 2 秒ごとに画面のスクリーンショット cam<N>.jpg を保存する（確認用）")
    args = ap.parse_args()
    snap_t = 0.0

    cap = open_source(args.source or str(args.camera), args.width, args.height)
    ok, frame = cap.read()
    if not ok:
        raise SystemExit("最初のフレームが読めません")
    h, w = frame.shape[:2]
    calib_path = Path(args.calib) if args.calib else ROOT / "camera" / "config" / f"calib_cam{args.camera}.json"
    calib = Calibration(calib_path, (w, h))
    detector = Detector(args.model, args.conf)
    hands = HandDetector() if not args.no_hand else None
    logic = GameLogic()
    sender = Sender(args.server)
    print(f"モデル: {args.model}  クラス: {list(detector.names.values())}  入力: {w}x{h}")
    if calib.is_default:
        print("キャリブレーション未設定: 画面全体をまな板として動かします。c キーで設定してください")

    crop_board = False
    last_pos_sent = 0.0
    recent = deque(maxlen=6)  # 画面に出す最近のイベント
    fps_t, fps = time.time(), 0.0
    is_video = args.source is not None

    while True:
        ok, frame = cap.read()
        if not ok:
            if is_video:  # 動画は最初に戻す
                cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
                continue
            break
        now = time.time()

        crop = None
        if crop_board and not calib.is_default:
            xs = [p[0] for p in calib.corners["board"]]
            ys = [p[1] for p in calib.corners["board"]]
            m = 0.15  # 少し余白を付ける
            cw, ch = max(xs) - min(xs), max(ys) - min(ys)
            crop = (max(0, min(xs) - cw * m), max(0, min(ys) - ch * m), min(w, max(xs) + cw * m), min(h, max(ys) + ch * m))
        dets = detector(frame, crop)
        hand_list = hands(frame) if hands else []
        events = logic.update(dets, hand_list, calib, now)

        for ev in events:
            if ev["event"] == "position":
                if now - last_pos_sent < 1 / args.rate and ev.get("visible", True):
                    continue
            else:
                recent.append(f"{time.strftime('%H:%M:%S')} {ev['event']} {ev.get('item', '')} {ev.get('count', ev.get('present', ''))}")
            sender.send(ev)
        if any(e["event"] == "position" for e in events) and now - last_pos_sent >= 1 / args.rate:
            last_pos_sent = now

        # ---- デバッグ表示 ----
        view = frame.copy()
        calib.draw(view)
        if crop:
            cv2.rectangle(view, (int(crop[0]), int(crop[1])), (int(crop[2]), int(crop[3])), (255, 255, 255), 1)
        for d in dets:
            color = COLORS.get(d.cls, DEFAULT_COLOR)
            cv2.rectangle(view, (int(d.x1), int(d.y1)), (int(d.x2), int(d.y2)), color, 2)
            u, v = calib.to_board(d.center)
            zone = calib.zone_of(d.center) or "-"
            view = draw_text(view, f"{d.cls} {d.conf:.2f} ({u:.2f},{v:.2f}) {zone}", (int(d.x1), max(0, int(d.y1) - 26)), color, 20)
        for (cx, cy), (x1, y1, x2, y2) in hand_list:
            cv2.rectangle(view, (int(x1), int(y1)), (int(x2), int(y2)), (255, 255, 0), 1)
            cv2.circle(view, (int(cx), int(cy)), 8, (255, 255, 0), -1)
        fps = 0.9 * fps + 0.1 * (1 / max(1e-3, now - fps_t)); fps_t = now
        hand_txt = "手: あり" if hand_list else ("手: なし" + (f" ({now - logic.hand_absent_since:.1f}s)" if logic.hand_absent_since else ""))
        confirmed = ", ".join(f"{z}/{i}:{n}" for (z, i), n in sorted(logic.confirmed.items())) or "-"
        lines = [
            f"FPS {fps:4.1f}   検出 {len(dets)}   {hand_txt}   {'サーバー接続' if sender.connected else ('サーバー未接続' if args.server else 'サーバー無し')}",
            f"確定ピース数: {confirmed}",
            *recent,
            "c: キャリブレーション   b: まな板切り出し " + ("ON" if crop_board else "OFF") + "   q: 終了",
        ]
        view = draw_lines(view, lines, color=(255, 255, 255), size=20)
        if calib.is_default:
            view = draw_text(view, "キャリブレーション未設定（c キー）", (12, h - 40), (60, 210, 255), 22)
        cv2.imshow(f"camera {args.camera}", view)
        if args.snapshot and now - snap_t > 2:
            snap_t = now
            Path(args.snapshot).mkdir(parents=True, exist_ok=True)
            cv2.imwrite(str(Path(args.snapshot) / f"cam{args.camera}.jpg"), view, [cv2.IMWRITE_JPEG_QUALITY, 80])

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord("c"):
            calib.run_ui(cap)
            logic = GameLogic()
        if key == ord("b"):
            crop_board = not crop_board

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
