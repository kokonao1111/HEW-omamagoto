#!/usr/bin/env python3
"""dataset/raw/<クラス名>/ の写真から YOLO 学習用データセット (dataset/yolo/) を自動生成する。

前提: 1枚の写真に対象が1つ、背景は白い机など彩度の低いもの。
      「色のついた部分」を彩度で切り出して枠にし（--method sat）、YOLO 形式のラベルを書き出す。
      背景が単色でない写真は --method grabcut を試す。
      dataset/raw/_background/ の写真はラベル無し（何も無い机・手だけ など）として学習に混ぜる。

確認用に dataset/preview/<クラス名>/ に枠を描いた画像を出すので、目で見て変な枠が無いか確認すること。
赤い枠 = GrabCut が失敗して「画像全体」を枠にしたもの。

使い方:
  .venv/bin/python train/auto_label.py            # 通常
  .venv/bin/python train/auto_label.py --full     # 全部「画像全体」を枠にする（対象が画面いっぱいに写っている場合）
"""
import argparse
import json
import random
import shutil
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageOps

try:
    from pillow_heif import register_heif_opener  # iPhone の HEIC 対応

    register_heif_opener()
except ImportError:
    pass

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "dataset" / "raw"
OUT = ROOT / "dataset" / "yolo"
PREVIEW = ROOT / "dataset" / "preview"
EXTS = {".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp"}
BACKGROUND_DIR = "_background"


def load_image(path: Path, max_side: int) -> np.ndarray:
    """EXIF の回転を反映して読み込み、長辺を max_side に縮小した BGR 配列を返す"""
    im = ImageOps.exif_transpose(Image.open(path)).convert("RGB")
    s = max_side / max(im.size)
    if s < 1:
        im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    return cv2.cvtColor(np.array(im), cv2.COLOR_RGB2BGR)


def background_reference(bg_dir: Path, max_side: int):
    """_background/ の写真の中央値画像を返す（固定カメラで撮った「何も無い台」）。無ければ None"""
    files = sorted(p for p in bg_dir.iterdir() if p.suffix.lower() in EXTS) if bg_dir.exists() else []
    files = [p for p in files if getattr(p.stat(), "st_blocks", 1) > 0][:30]
    if not files:
        return None
    imgs = [load_image(p, max_side) for p in files]
    h, w = imgs[0].shape[:2]
    imgs = [im for im in imgs if im.shape[:2] == (h, w)]
    return np.median(np.stack(imgs), axis=0).astype(np.uint8)


def diff_box(img: np.ndarray, ref: np.ndarray, thr: int = 40):
    """固定カメラの背景との差分から物体の枠を返す。木目の台でも使える。失敗したら None"""
    if img.shape != ref.shape:
        ref = cv2.resize(ref, (img.shape[1], img.shape[0]))
    d = cv2.absdiff(img, ref).max(axis=2)
    mask = (d > thr).astype(np.uint8) * 255
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    h, w = mask.shape
    areas = [cv2.contourArea(c) for c in contours]
    biggest = max(areas)
    if biggest < 0.003 * w * h:
        return None
    pts = np.vstack([c for c, a in zip(contours, areas) if a >= 0.15 * biggest])
    x, y, bw, bh = cv2.boundingRect(pts)
    if (bw * bh) / (w * h) > 0.92:
        return None
    return (x, y, x + bw, y + bh)


def saturation_box(img: np.ndarray, work_side: int = 640, min_sat: int = 70, min_val: int = 40):
    """白い机の上の「色のついた物」の枠を返す。机・影・黒い縁は彩度が低いので除外される。失敗したら None"""
    h, w = img.shape[:2]
    s = work_side / max(h, w)
    small = cv2.resize(img, (max(1, round(w * s)), max(1, round(h * s)))) if s < 1 else img.copy()
    hsv = cv2.cvtColor(small, cv2.COLOR_BGR2HSV)
    mask = ((hsv[:, :, 1] >= min_sat) & (hsv[:, :, 2] >= min_val)).astype(np.uint8) * 255
    k = np.ones((7, 7), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, k)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    sh, sw = mask.shape
    # 一番大きい塊と、その 15% 以上の大きさの塊をまとめて 1 つの枠にする（包丁の柄と刃が離れて見えても拾う）
    areas = [cv2.contourArea(c) for c in contours]
    biggest = max(areas)
    if biggest < 0.005 * sw * sh:
        return None
    pts = np.vstack([c for c, a in zip(contours, areas) if a >= 0.15 * biggest])
    x, y, bw, bh = cv2.boundingRect(pts)
    if (bw * bh) / (sw * sh) > 0.92:
        return None
    inv = 1 / s if s < 1 else 1
    return (int(x * inv), int(y * inv), int((x + bw) * inv), int((y + bh) * inv))


def grabcut_box(img: np.ndarray, work_side: int = 480, margin: float = 0.04):
    """中央付近にある物体の枠 (x0,y0,x1,y1) を返す。失敗したら None"""
    h, w = img.shape[:2]
    s = work_side / max(h, w)
    small = cv2.resize(img, (max(1, round(w * s)), max(1, round(h * s)))) if s < 1 else img.copy()
    sh, sw = small.shape[:2]
    rect = (int(sw * margin), int(sh * margin), int(sw * (1 - 2 * margin)), int(sh * (1 - 2 * margin)))
    mask = np.zeros((sh, sw), np.uint8)
    bgd = np.zeros((1, 65), np.float64)
    fgd = np.zeros((1, 65), np.float64)
    try:
        cv2.grabCut(small, mask, rect, bgd, fgd, 5, cv2.GC_INIT_WITH_RECT)
    except cv2.error:
        return None
    fg = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    contours, _ = cv2.findContours(fg, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    c = max(contours, key=cv2.contourArea)
    x, y, bw, bh = cv2.boundingRect(c)
    frac = (bw * bh) / (sw * sh)
    if frac < 0.02 or frac > 0.92:  # 小さすぎ/大きすぎ = 失敗とみなす
        return None
    inv = 1 / s if s < 1 else 1
    return (int(x * inv), int(y * inv), int((x + bw) * inv), int((y + bh) * inv))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--full", action="store_true", help="枠付けをせず画像全体を枠にする")
    ap.add_argument("--method", choices=["auto", "diff", "sat", "grabcut"], default="auto",
                    help="auto: _background があれば diff、無ければ sat / diff: 固定カメラの背景差分（tools/capture.py で撮ったとき）"
                         " / sat: 白い机の上の色つき物体を彩度で切り出す / grabcut: 背景が単色でないとき")
    ap.add_argument("--max-side", type=int, default=1280, help="保存する画像の長辺ピクセル")
    ap.add_argument("--val", type=float, default=0.15, help="検証用に回す割合")
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    if not RAW.exists():
        raise SystemExit(f"{RAW} がありません。dataset/raw/<クラス名>/ に写真を入れてください")

    class_dirs = sorted(d for d in RAW.iterdir() if d.is_dir() and d.name != BACKGROUND_DIR and not d.name.startswith("."))
    names = [d.name for d in class_dirs]
    if not names:
        raise SystemExit("dataset/raw/ にクラスのフォルダがありません（例: dataset/raw/にんじん/）")

    # 出力を作り直す
    for sub in ("images/train", "images/val", "labels/train", "labels/val"):
        p = OUT / sub
        if p.exists():
            shutil.rmtree(p)
        p.mkdir(parents=True)
    if PREVIEW.exists():
        shutil.rmtree(PREVIEW)

    random.seed(args.seed)
    stats = {}
    fallback = 0

    bg_ref = None
    if args.method in ("auto", "diff"):
        bg_ref = background_reference(RAW / BACKGROUND_DIR, args.max_side)
        if args.method == "diff" and bg_ref is None:
            raise SystemExit("--method diff には dataset/raw/_background/ の写真が必要です（tools/capture.py の B キー）")
    method = "diff" if bg_ref is not None else ("sat" if args.method == "auto" else args.method)
    print(f"枠付けの方法: {method}")

    skipped = []

    def list_photos(d: Path):
        """iCloud で未ダウンロードのファイル（実体が 0 ブロック）は読むと固まるので最初から除外する"""
        files = sorted(p for p in d.iterdir() if p.suffix.lower() in EXTS)
        ready = [p for p in files if getattr(p.stat(), "st_blocks", 1) > 0]
        for p in files:
            if p not in ready:
                skipped.append(p.name)
        if len(ready) < len(files):
            print(f"  {d.name}: {len(files) - len(ready)} 枚が iCloud から未ダウンロードのため除外")
        return ready

    def process(src: Path, cls_idx, cls_name):
        nonlocal fallback
        try:
            img = load_image(src, args.max_side)
        except (OSError, TimeoutError) as e:  # iCloud 未ダウンロードなど
            skipped.append(src.name)
            print(f"  読めない: {src.name} ({e})")
            return
        h, w = img.shape[:2]
        split = "val" if random.random() < args.val else "train"
        stem = f"{cls_name}_{src.stem}"
        cv2.imwrite(str(OUT / "images" / split / f"{stem}.jpg"), img, [cv2.IMWRITE_JPEG_QUALITY, 92])
        label_path = OUT / "labels" / split / f"{stem}.txt"
        if cls_idx is None:  # 背景画像: ラベル無し（空ファイル）
            label_path.write_text("")
            return
        if args.full:
            box = None
        elif method == "diff" and img.shape == bg_ref.shape:
            box = diff_box(img, bg_ref) or saturation_box(img)
        elif method == "diff":
            # 背景と解像度が違う写真（別のカメラ・スマホ）は差分が使えないので彩度で
            box = saturation_box(img) or grabcut_box(img)
        elif method == "sat":
            box = saturation_box(img) or grabcut_box(img)
        else:
            box = grabcut_box(img)
        color = (60, 220, 90)
        if box is None:
            m = 0.02
            box = (int(w * m), int(h * m), int(w * (1 - m)), int(h * (1 - m)))
            color = (60, 60, 255)
            fallback += 1
        x0, y0, x1, y1 = box
        cx, cy, bw, bh = (x0 + x1) / 2 / w, (y0 + y1) / 2 / h, (x1 - x0) / w, (y1 - y0) / h
        label_path.write_text(f"{cls_idx} {cx:.6f} {cy:.6f} {bw:.6f} {bh:.6f}\n")
        # 確認用プレビュー
        pv = img.copy()
        cv2.rectangle(pv, (x0, y0), (x1, y1), color, 3)
        pdir = PREVIEW / cls_name
        pdir.mkdir(parents=True, exist_ok=True)
        cv2.imwrite(str(pdir / f"{stem}.jpg"), cv2.resize(pv, (w // 2, h // 2)), [cv2.IMWRITE_JPEG_QUALITY, 80])

    for idx, d in enumerate(class_dirs):
        files = list_photos(d)
        for f in files:
            process(f, idx, d.name)
        stats[d.name] = len(files)
        print(f"{d.name}: {len(files)} 枚")

    bg = RAW / BACKGROUND_DIR
    if bg.exists():
        files = list_photos(bg)
        for f in files:
            process(f, None, BACKGROUND_DIR)
        stats[BACKGROUND_DIR] = len(files)
        print(f"背景(ラベル無し): {len(files)} 枚")

    (OUT / "data.yaml").write_text(
        f"path: {OUT}\ntrain: images/train\nval: images/val\nnames:\n" + "".join(f"  {i}: {n}\n" for i, n in enumerate(names)),
        encoding="utf-8",
    )
    (OUT / "classes.json").write_text(json.dumps(names, ensure_ascii=False), encoding="utf-8")
    print(f"\nクラス: {names}")
    print(f"GrabCut 失敗→画像全体を枠にした枚数: {fallback}（dataset/preview/ の赤い枠）")
    if skipped:
        print(f"読めずに飛ばした写真: {len(skipped)} 枚（iCloud から Mac にダウンロードされていない可能性。Finder で右クリック→「今すぐダウンロード」）")
    print(f"出力: {OUT}  プレビュー: {PREVIEW}")


if __name__ == "__main__":
    main()
