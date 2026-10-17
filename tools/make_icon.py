# -*- coding: utf-8 -*-
"""用头像生成程序图标（exe 图标 / 任务栏 / 托盘共用）：assets/sprites/icon.ico

运行：python tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "portrait.jpg")
OUT = os.path.join(ROOT, "assets", "sprites", "icon.ico")
PREVIEW = os.path.join(ROOT, "docs", "icon.png")
RADIUS = 0.18


def main():
    src = Image.open(SRC).convert("RGB")
    w, h = src.size
    side = min(w, h)
    sq = src.crop(((w - side) // 2, (h - side) // 2, (w - side) // 2 + side, (h - side) // 2 + side))
    big = 256
    sq = sq.resize((big, big), Image.LANCZOS)
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, big - 1, big - 1), int(big * RADIUS), fill=255)
    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    icon.paste(sq, (0, 0), mask)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    icon.save(OUT, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    icon.resize((32, 32), Image.LANCZOS).resize((128, 128), Image.NEAREST).save(PREVIEW)
    print("icon ->", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
