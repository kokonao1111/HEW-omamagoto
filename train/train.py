#!/usr/bin/env python3
"""dataset/yolo/ を使って YOLO を追加学習し、ブラウザ用 (models/best.onnx + models/model.json) に書き出す。

使い方:
  .venv/bin/python train/train.py                 # 既定: yolov8n, 60 epoch, 640px
  .venv/bin/python train/train.py --epochs 100
  .venv/bin/python train/train.py --export-only   # 学習済み runs/veg/weights/best.pt を書き出すだけ
"""
import argparse
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "dataset" / "yolo" / "data.yaml"
RUNS = ROOT / "runs"
MODELS = ROOT / "models"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="yolov8n.pt", help="ベースモデル (yolov8n.pt / yolo11n.pt など)")
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--batch", type=int, default=16)
    ap.add_argument("--device", default=None, help="mps / cpu / 0 …（省略時は自動）")
    ap.add_argument("--export-only", action="store_true")
    args = ap.parse_args()

    import torch
    from ultralytics import YOLO

    device = args.device or ("mps" if torch.backends.mps.is_available() else "cpu")
    best = RUNS / "veg" / "weights" / "best.pt"

    if not args.export_only:
        if not DATA.exists():
            raise SystemExit(f"{DATA} がありません。先に train/auto_label.py を実行してください")
        print(f"device={device} model={args.model} epochs={args.epochs}")
        model = YOLO(args.model)
        model.train(
            data=str(DATA),
            epochs=args.epochs,
            imgsz=args.imgsz,
            batch=args.batch,
            device=device,
            project=str(RUNS),
            name="veg",
            exist_ok=True,
            patience=25,
            # おもちゃ・室内向けの水増し: 明るさ/色味の変化を多めに
            hsv_h=0.02, hsv_s=0.6, hsv_v=0.5, degrees=15, flipud=0.2, fliplr=0.5, scale=0.5,
        )

    if not best.exists():
        raise SystemExit(f"{best} がありません")

    model = YOLO(str(best))
    metrics = model.val(data=str(DATA), imgsz=args.imgsz, device=device, plots=False, verbose=False)
    print(f"\n検証 mAP50={metrics.box.map50:.3f}  mAP50-95={metrics.box.map:.3f}")

    onnx_path = Path(model.export(format="onnx", imgsz=args.imgsz, opset=12, simplify=True, dynamic=False))
    MODELS.mkdir(exist_ok=True)
    shutil.copy(onnx_path, MODELS / "best.onnx")
    names = [model.names[i] for i in range(len(model.names))]
    # ブラウザ側 (yolo.js) が読む設定: 入力サイズとクラス名
    (MODELS / "model.json").write_text(
        json.dumps({"imgsz": args.imgsz, "classes": names, "base": args.model}, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"書き出し: {MODELS / 'best.onnx'} ({(MODELS / 'best.onnx').stat().st_size // 1024} KB)  classes={names}")
    print("ブラウザの「詳細設定 → 検出方法」で「自前モデル(YOLO)」を選ぶと使えます")


if __name__ == "__main__":
    main()
