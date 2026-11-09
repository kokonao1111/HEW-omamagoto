"""
写真の素材（Poly Haven・ambientCG）の「ざらつき（Roughness）」の画像から、
Unity（URP の Lit）の mask の画像を作る。

  mask の R = 金属らしさ（ここでは 0。金属の素材なら --metallic で 1 にする）
  mask の A = なめらかさ = 1 − ざらつき

動かし方（unity フォルダーで）:
  uv run --with pillow --with numpy python tools/rough_to_mask.py 入力のざらつき.jpg 出力の_mask.png
"""

import argparse

import numpy as np
from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser(description="ざらつきの画像から、Unity の mask の画像を作る")
    parser.add_argument("roughness", help="ざらつきの画像（白いほどざらざら）")
    parser.add_argument("mask", help="作る mask の画像（.png）")
    parser.add_argument("--metallic", type=float, default=0.0, help="金属らしさ（0〜1）")
    args = parser.parse_args()

    rough = np.asarray(Image.open(args.roughness).convert("L"), dtype=np.float32) / 255
    height, width = rough.shape
    mask = np.zeros((height, width, 4), dtype=np.float32)
    mask[..., 0] = args.metallic
    mask[..., 3] = 1 - rough

    Image.fromarray((mask * 255 + 0.5).astype(np.uint8), "RGBA").save(args.mask)
    print("作った:", args.mask)


if __name__ == "__main__":
    main()
