#!/usr/bin/env python3
"""Web カメラから学習用の写真を連写して dataset/raw/<クラス名>/ に保存する。

本番と同じカメラ・配置・照明で撮るための道具。キーを1回押すと、カウントダウンのあと
指定秒数だけ自動で連写する（押しっぱなし不要）。連写中は対象を動かして角度を変える。

使い方:
  .venv/bin/python tools/capture.py にんじん 包丁 その他        # クラス名を並べる（1〜9 キーで切替）
  .venv/bin/python tools/capture.py にんじん --camera 1 --seconds 5 --fps 5

画面の操作:
  Space   : 3 秒カウントダウン → 連写開始（既定 5 秒 × 5 枚/秒 = 25 枚）
  1〜9    : 保存先クラスを切り替える
  B       : 何も置いていない台を 10 枚撮って _background に保存（背景差分の枠付け用・毎回最初にやる）
  Q / Esc : 終了

保存先: dataset/raw/<クラス名>/<日時>_<連番>.jpg（カメラの解像度のまま）
"""
import argparse
import platform
import sys
import time
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "dataset" / "raw"
BACKGROUND = "_background"

# 日本語を画面に描くためのフォント（無ければ英語で描く）
try:
    from PIL import Image, ImageDraw, ImageFont

    _FONT_CANDIDATES = [
        "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc",
        "/System/Library/Fonts/Hiragino Sans GB.ttc",
        "C:/Windows/Fonts/meiryob.ttc",
        "C:/Windows/Fonts/YuGothB.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc",
    ]
    _FONT = next((ImageFont.truetype(p, 28) for p in _FONT_CANDIDATES if Path(p).exists()), None)
except ImportError:
    _FONT = None


def draw_text(img, text, xy, color=(255, 255, 255), big=False):
    if _FONT is None:
        cv2.putText(img, text.encode("ascii", "ignore").decode(), xy, cv2.FONT_HERSHEY_SIMPLEX, 1.6 if big else 0.8, color, 2)
        return img
    pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
    d = ImageDraw.Draw(pil)
    font = _FONT.font_variant(size=72 if big else 28)
    d.text((xy[0] + 2, xy[1] + 2), text, font=font, fill=(0, 0, 0))
    d.text(xy, text, font=font, fill=color[::-1])
    return cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("classes", nargs="+", help="クラス名（フォルダ名になる）")
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--seconds", type=float, default=5.0, help="1回の連写時間（秒）")
    ap.add_argument("--fps", type=float, default=5.0, help="連写の枚数/秒")
    ap.add_argument("--countdown", type=float, default=3.0, help="連写前のカウントダウン（秒）")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    args = ap.parse_args()

    backend = cv2.CAP_DSHOW if platform.system() == "Windows" else cv2.CAP_ANY
    cap = cv2.VideoCapture(args.camera, backend)
    if not cap.isOpened():
        raise SystemExit(f"カメラ {args.camera} を開けません")
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    cls_idx = 0
    counts = {c: len(list((RAW / c).glob("*.jpg"))) if (RAW / c).exists() else 0 for c in args.classes + [BACKGROUND]}
    burst = None  # {"cls", "start", "end", "next", "n"}
    countdown_until = None
    interval = 1.0 / args.fps
    session = datetime.now().strftime("%Y%m%d_%H%M%S")

    def save(cls, frame):
        d = RAW / cls
        d.mkdir(parents=True, exist_ok=True)
        counts[cls] += 1
        cv2.imwrite(str(d / f"{session}_{counts[cls]:04d}.jpg"), frame, [cv2.IMWRITE_JPEG_QUALITY, 92])

    print("Space: 連写  1-9: クラス切替  B: 背景を撮る  Q: 終了")
    while True:
        ok, frame = cap.read()
        if not ok:
            continue
        now = time.time()
        cls = args.classes[cls_idx]

        # カウントダウン → 連写開始
        if countdown_until is not None and now >= countdown_until:
            burst = {"cls": cls, "start": now, "end": now + args.seconds, "next": now, "n": 0}
            countdown_until = None

        # 連写中
        if burst is not None:
            if now >= burst["next"]:
                save(burst["cls"], frame)
                burst["n"] += 1
                burst["next"] += interval
            if now >= burst["end"]:
                print(f"{burst['cls']}: {burst['n']} 枚保存（合計 {counts[burst['cls']]} 枚）")
                burst = None

        # 画面表示
        view = frame.copy()
        h, w = view.shape[:2]
        for i, c in enumerate(args.classes):
            mark = "▶ " if i == cls_idx else "   "
            view = draw_text(view, f"{mark}{i + 1}: {c}  {counts[c]}枚", (16, 16 + i * 34), (80, 255, 120) if i == cls_idx else (220, 220, 220))
        view = draw_text(view, f"B: 背景 {counts[BACKGROUND]}枚   Space: 連写 {args.seconds:.0f}秒   Q: 終了", (16, h - 40), (200, 200, 200))
        if countdown_until is not None:
            view = draw_text(view, f"{int(countdown_until - now) + 1}", (w // 2 - 30, h // 2 - 40), (60, 210, 255), big=True)
            view = draw_text(view, "対象を構えて…", (w // 2 - 90, h // 2 + 50), (60, 210, 255))
        elif burst is not None:
            remain = burst["end"] - now
            view = draw_text(view, f"● 撮影中 {remain:.1f}s  {burst['n']}枚  ─ 角度を変えて！", (w // 2 - 200, h // 2 - 20), (60, 60, 255))
            cv2.rectangle(view, (0, 0), (w - 1, h - 1), (0, 0, 255), 8)
        cv2.imshow("capture", view)

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord(" ") and burst is None and countdown_until is None:
            countdown_until = now + args.countdown
        if ord("1") <= key <= ord("9"):
            i = key - ord("1")
            if i < len(args.classes):
                cls_idx = i
        if key in (ord("b"), ord("B")) and burst is None:
            # 背景は動かないので間隔を空けずに 10 枚
            for _ in range(10):
                ok2, f2 = cap.read()
                if ok2:
                    save(BACKGROUND, f2)
            print(f"背景: 10 枚保存（合計 {counts[BACKGROUND]} 枚）")

    cap.release()
    cv2.destroyAllWindows()
    print("保存先:", RAW)


if __name__ == "__main__":
    main()
