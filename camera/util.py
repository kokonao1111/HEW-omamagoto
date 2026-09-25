"""画面描画の共通処理（日本語テキストなど）"""
from pathlib import Path

import cv2
import numpy as np

try:
    from PIL import Image, ImageDraw, ImageFont

    _FONT_CANDIDATES = [
        "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc",
        "/System/Library/Fonts/Hiragino Sans GB.ttc",
        "C:/Windows/Fonts/meiryob.ttc",
        "C:/Windows/Fonts/YuGothB.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc",
    ]
    _FONT = next((ImageFont.truetype(p, 24) for p in _FONT_CANDIDATES if Path(p).exists()), None)
except ImportError:
    _FONT = None


def draw_text(img, text, xy, color=(255, 255, 255), size=24):
    """日本語フォントがあれば PIL で、無ければ OpenCV（ASCII のみ）で描く。img を返す"""
    if _FONT is None:
        cv2.putText(img, text.encode("ascii", "ignore").decode(), xy, cv2.FONT_HERSHEY_SIMPLEX, size / 30, color, 2)
        return img
    pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
    d = ImageDraw.Draw(pil)
    font = _FONT.font_variant(size=size)
    d.text((xy[0] + 2, xy[1] + 2), text, font=font, fill=(0, 0, 0))
    d.text(xy, text, font=font, fill=color[::-1])
    return cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)


def draw_lines(img, lines, x=12, y=12, color=(255, 255, 255), size=22, gap=6):
    """複数行をまとめて描く"""
    for line in lines:
        img = draw_text(img, line, (x, y), color, size)
        y += size + gap
    return img
