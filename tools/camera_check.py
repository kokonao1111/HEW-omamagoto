#!/usr/bin/env python3
"""この PC × この Web カメラで「露出・ホワイトバランスを固定できるか」を判定する。

使い方:
  .venv/bin/python tools/camera_check.py          # カメラ 0 番
  .venv/bin/python tools/camera_check.py 1        # カメラ 1 番

やっていること:
  1. 自動露出を切って露出を「暗め」「明るめ」に設定し、画面の平均明るさが変わるかを見る
     → 変わる = 手動露出が効く（固定できる）/ 変わらない = 自動のみ
  2. 同じ要領でホワイトバランス（色温度）を変えて色味が変わるかを見る
展示用 PC で実行すること（OS とドライバで結果が変わる。macOS は OpenCV から設定できないことが多い）。
"""
import platform
import sys
import time

import cv2
import numpy as np

idx = int(sys.argv[1]) if len(sys.argv) > 1 else 0
system = platform.system()
backend = cv2.CAP_DSHOW if system == "Windows" else cv2.CAP_ANY
cap = cv2.VideoCapture(idx, backend)
if not cap.isOpened():
    raise SystemExit(f"カメラ {idx} を開けません")

cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
w, h, fps = cap.get(cv2.CAP_PROP_FRAME_WIDTH), cap.get(cv2.CAP_PROP_FRAME_HEIGHT), cap.get(cv2.CAP_PROP_FPS)
print(f"OS: {system}  backend: {cap.getBackendName()}  解像度: {int(w)}x{int(h)} @ {fps:.0f}fps")


def grab(n=8, settle=0.6):
    """設定が反映されるのを待ってから n 枚読み、平均画像を返す"""
    time.sleep(settle)
    for _ in range(5):
        cap.read()
    frames = [f for ok, f in (cap.read() for _ in range(n)) if ok]
    return np.mean(frames, axis=0)


def brightness(img):
    return float(cv2.cvtColor(img.astype(np.uint8), cv2.COLOR_BGR2GRAY).mean())


def tint(img):
    b, g, r = img.reshape(-1, 3).mean(axis=0)
    return float(r - b)  # 赤み − 青み。色温度を変えると動く


# ---- 露出 ----
auto_val = cap.get(cv2.CAP_PROP_AUTO_EXPOSURE)
exp_val = cap.get(cv2.CAP_PROP_EXPOSURE)
print(f"\n[露出] 現在: AUTO_EXPOSURE={auto_val}  EXPOSURE={exp_val}")
base = brightness(grab())
if base < 2:
    print("  ! 映像が真っ黒です。カメラの権限（設定 → プライバシー → カメラ）やレンズカバーを確認してください")

# 手動モードの値は OS で違う: Windows(DSHOW)=0.25, Linux(V4L2)=1, それ以外は両方試す
manual_flags = [0.25] if system == "Windows" else [1, 0.25]
for flag in manual_flags:
    cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, flag)
# Windows は log2 秒 (-13〜-1)、Linux は 100µs 単位 (1〜5000) が多いので両系統を試す
candidates = [(-10, -4), (20, 800)]
result = None
for dark, bright in candidates:
    cap.set(cv2.CAP_PROP_EXPOSURE, dark)
    b_dark = brightness(grab())
    cap.set(cv2.CAP_PROP_EXPOSURE, bright)
    b_bright = brightness(grab())
    print(f"  露出 {dark:>5} → 明るさ {b_dark:5.1f} / 露出 {bright:>5} → 明るさ {b_bright:5.1f}")
    if abs(b_bright - b_dark) > 15:
        result = (dark, bright)
        break
print("  → 露出固定: OK（手動露出が効きます）" if result else "  → 露出固定: NG（自動露出しか使えません）")

# 自動に戻す
for flag in ([0.75] if system == "Windows" else [3, 0.75]):
    cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, flag)

# ---- ホワイトバランス ----
print(f"\n[WB] 現在: AUTO_WB={cap.get(cv2.CAP_PROP_AUTO_WB)}  WB_TEMPERATURE={cap.get(cv2.CAP_PROP_WB_TEMPERATURE)}")
cap.set(cv2.CAP_PROP_AUTO_WB, 0)
cap.set(cv2.CAP_PROP_WB_TEMPERATURE, 2800)
t_warm = tint(grab())
cap.set(cv2.CAP_PROP_WB_TEMPERATURE, 6500)
t_cool = tint(grab())
print(f"  色温度 2800K → 赤み {t_warm:+5.1f} / 6500K → 赤み {t_cool:+5.1f}")
print("  → WB固定: OK" if abs(t_warm - t_cool) > 8 else "  → WB固定: NG（自動のみ）")
cap.set(cv2.CAP_PROP_AUTO_WB, 1)

print(f"\n基準の明るさ（自動露出時）: {base:.1f}")
print("NG の場合: Windows なら Logi Tune / Logi Options+ 等のメーカーアプリで固定できることがある。macOS は基本的にアプリ側で固定する。")
cap.release()
