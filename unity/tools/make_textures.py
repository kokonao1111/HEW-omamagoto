"""
キッチンの画面で使う、小さな画像を作る（チームの自作。外の素材は使わない）。

作るもの（unity/Assets/Art/Textures/generated/ に保存）:
  flame_gas.png          ガスの炎 1 本（黒い所は透明として、足し算で重ねる）
  steam_puff.png         湯気のもや（白。A が濃さ）
  grain.png              塩・こしょうの粒（丸。A で形を切り抜く）
  microwave_mesh.png     電子レンジの窓の、穴のあいた金属の板（A = 0 が穴）。1 枚で 30 mm 角
  microwave_display.png  電子レンジの時計の表示（7 セグメントの数字 "12:00"）。光る色の画像
  cast_iron_nor_gl.png   鋳物（五徳）の細かいざらざらの凹凸。1 枚で 100 mm 角
  cast_iron_mask.png     鋳物のなめらかさ（A）。R は金属らしさ

動かし方（unity フォルダーで）:
  uv run --with pillow --with numpy python tools/make_textures.py
"""

from pathlib import Path

import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent.parent / "Assets" / "Art" / "Textures" / "generated"
rng = np.random.default_rng(20261109)


def save(name: str, array: np.ndarray) -> None:
    array = np.clip(array, 0, 1)
    mode = "RGBA" if array.shape[2] == 4 else "RGB"
    Image.fromarray((array * 255 + 0.5).astype(np.uint8), mode).save(OUT / name)
    print("作った:", OUT / name)


def grid(width: int, height: int):
    """画素の真ん中の座標（0〜1）"""
    x = (np.arange(width) + 0.5) / width
    y = (np.arange(height) + 0.5) / height
    return np.meshgrid(x, y)


def smooth_noise(size: int, cells: int) -> np.ndarray:
    """つなぎ目のない、なめらかなノイズ（0〜1）。cells が大きいほど細かい"""
    base = rng.random((cells, cells))
    x = np.arange(size) * cells / size
    x0 = np.floor(x).astype(int)
    t = x - x0
    t = t * t * (3 - 2 * t)
    x1 = (x0 + 1) % cells
    rows = base[:, x0] * (1 - t) + base[:, x1] * t                    # 横に補間
    return rows[x0, :] * (1 - t)[:, None] + rows[x1, :] * t[:, None]  # 縦に補間


def fractal_noise(size: int, start_cells: int, octaves: int) -> np.ndarray:
    total = np.zeros((size, size))
    weight_sum = 0.0
    for o in range(octaves):
        weight = 0.5 ** o
        total += smooth_noise(size, start_cells * 2 ** o) * weight
        weight_sum += weight
    return total / weight_sum


def flame_gas() -> None:
    """
    下（根もと）が濃い青、先へ行くほど細く薄くなる、ガスの炎。
    画像の下が根もと、上が先。黒は「足しても変わらない」ので透明と同じ
    """
    w, h = 64, 128
    x, y = grid(w, h)
    along = 1 - y                    # 0 = 根もと、1 = 先
    across = np.abs(x - 0.5) * 2     # 0 = 真ん中、1 = はし

    # 外の炎：しずくの形（根もとが少しすぼまり、真ん中でふくらみ、先へとがる）
    width_outer = 0.95 * np.sin(np.clip(along, 0, 1) ** 0.6 * np.pi) ** 0.8 + 0.05
    outer = np.clip(1 - across / width_outer, 0, 1) ** 1.5 * np.clip(1 - along, 0, 1) ** 0.7
    # 内の炎：根もとに近い、明るい小さな芯
    width_inner = 0.55 * np.sin(np.clip(along / 0.45, 0, 1) ** 0.7 * np.pi) ** 0.9 + 0.02
    inner = np.clip(1 - across / width_inner, 0, 1) ** 1.2 * (along < 0.45)

    blue = np.array([0.10, 0.28, 1.00])
    cyan = np.array([0.45, 0.80, 1.00])
    rgb = outer[..., None] * blue * 0.7 + inner[..., None] * cyan
    tip = np.clip((along - 0.6) / 0.4, 0, 1)                         # 先は少し紫がかる
    rgb[..., 0] += outer * tip * 0.12
    save("flame_gas.png", rgb)


def steam_puff() -> None:
    """ふわっとした、ふちのぼやけたもや"""
    size = 128
    x, y = grid(size, size)
    distance = np.hypot(x - 0.5, y - 0.5) * 2
    noise = fractal_noise(size, 4, 4)
    alpha = np.clip(1 - distance, 0, 1) ** 1.6 * (0.55 + 0.45 * noise)
    rgba = np.dstack([np.ones((size, size))] * 3 + [alpha])
    save("steam_puff.png", rgba)


def grain() -> None:
    """丸い粒。少しだけゆがませる"""
    size = 32
    x, y = grid(size, size)
    distance = np.hypot((x - 0.5) * 1.1, y - 0.5) * 2
    alpha = (distance < 0.85).astype(float)
    shade = 1 - 0.25 * distance ** 2                                  # ふちを少し暗く（丸く見える）
    rgba = np.dstack([shade] * 3 + [alpha])
    save("grain.png", rgba)


def microwave_mesh() -> None:
    """
    窓の、丸い穴がたくさんあいた金属の板。穴は直径 1.8 mm、3 mm おきに互い違い（現実の電子レンジと同じくらい）。
    30 mm 角で 1 枚（横に 10 個、縦に 5 組 × 2 段）
    """
    size = 300                                       # 1 画素 = 0.1 mm
    x, y = np.meshgrid(np.arange(size) + 0.5, np.arange(size) + 0.5)
    pitch = 30.0                                     # 3 mm（画素）
    row_height = pitch * np.sqrt(3) / 2
    rows = size / row_height
    row_height = size / round(rows)                  # つなぎ目なく並ぶように、少しだけ縦を合わせる
    row = np.floor(y / row_height)
    shift = (row % 2) * pitch / 2
    cx = (np.floor((x - shift) / pitch) + 0.5) * pitch + shift
    cy = (row + 0.5) * row_height
    hole = np.hypot(x - cx, y - cy) < 9.0            # 半径 0.9 mm
    alpha = (~hole).astype(float)
    metal = 0.08 + 0.02 * fractal_noise(size, 10, 2)
    rgba = np.dstack([metal, metal, metal * 1.05, alpha])
    save("microwave_mesh.png", rgba)


SEGMENTS = {  # 7 セグメントの数字。a=上 b=右上 c=右下 d=下 e=左下 f=左上 g=真ん中
    "0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc",
    "5": "afgcd", "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcdfg",
}


def microwave_display() -> None:
    """黒い表示に、青白く光る "12:00"。光る所だけ明るい"""
    w, h = 256, 64
    image = np.zeros((h, w))
    digit_w, digit_h, stroke = 26, 44, 6
    top = (h - digit_h) // 2

    def segment(x0: int, part: str) -> None:
        half = digit_h // 2
        boxes = {
            "a": (x0 + stroke, top, x0 + digit_w - stroke, top + stroke),
            "d": (x0 + stroke, top + digit_h - stroke, x0 + digit_w - stroke, top + digit_h),
            "g": (x0 + stroke, top + half - stroke // 2, x0 + digit_w - stroke, top + half + stroke // 2),
            "f": (x0, top + stroke, x0 + stroke, top + half - 1),
            "b": (x0 + digit_w - stroke, top + stroke, x0 + digit_w, top + half - 1),
            "e": (x0, top + half + 1, x0 + stroke, top + digit_h - stroke),
            "c": (x0 + digit_w - stroke, top + half + 1, x0 + digit_w, top + digit_h - stroke),
        }
        left, upper, right, lower = boxes[part]
        image[upper:lower, left:right] = 1

    text = "12:00"
    x0 = 52
    for ch in text:
        if ch == ":":
            for dy in (top + 12, top + digit_h - 18):
                image[dy:dy + stroke, x0 + 4:x0 + 4 + stroke] = 1
            x0 += 18
            continue
        for part in SEGMENTS[ch]:
            segment(x0, part)
        x0 += digit_w + 10

    # 光がにじむように、少しぼかして足す
    blurred = image.copy()
    for _ in range(3):
        blurred = (blurred + np.roll(blurred, 1, 0) + np.roll(blurred, -1, 0) + np.roll(blurred, 1, 1) + np.roll(blurred, -1, 1)) / 5
    glow = np.clip(image + blurred * 0.35, 0, 1)
    color = np.array([0.55, 0.9, 1.0])
    save("microwave_display.png", glow[..., None] * color)


def cast_iron() -> None:
    """鋳物の、砂を固めたような細かいざらざら。凹凸（法線）と、なめらかさ"""
    size = 512                                      # 100 mm 角
    height = fractal_noise(size, 32, 4) * 0.7 + rng.random((size, size)) * 0.3
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 2.5
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 2.5
    normal = np.dstack([-dx, dy, np.ones_like(dx)])  # OpenGL 形式（緑が上）
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    save("cast_iron_nor_gl.png", normal * 0.5 + 0.5)

    smooth = 0.32 + 0.18 * (1 - height)              # へこんだ所ほど少しつやがある（油がしみている）
    mask = np.dstack([np.full_like(smooth, 0.25), np.zeros_like(smooth), np.zeros_like(smooth), smooth])
    save("cast_iron_mask.png", mask)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    flame_gas()
    steam_puff()
    grain()
    microwave_mesh()
    microwave_display()
    cast_iron()
