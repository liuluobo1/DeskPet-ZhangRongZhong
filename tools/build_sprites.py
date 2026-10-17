# -*- coding: utf-8 -*-
"""从照片生成桌宠帧：crawl_0..3（爬行）+ kowtow_0..2（磕头），输出到 assets/sprites/。

前置：先跑 tools/cutout.py 生成 build/cutout.png
运行：python tools/build_sprites.py
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import rig as R  # noqa: E402

OUT = os.path.join(ROOT, "assets", "sprites")
H_PX = 260          # 成品高度（桌宠实际显示高度）
PAD = 8
CRAWL_T = (0.0, 0.25, 0.5, 0.75)
KOWTOW_T = (0.0, 0.5, 1.0)


def union_bbox(images):
    box = None
    for im in images:
        b = im.getbbox()
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]),
                                     max(box[2], b[2]), max(box[3], b[3]))
    return box


def main():
    os.makedirs(OUT, exist_ok=True)
    crawl = [R.scene("crawl", t) for t in CRAWL_T]
    kowtow = [R.scene("kowtow", t) for t in KOWTOW_T]
    box = union_bbox(crawl + kowtow)
    box = (max(0, box[0] - PAD), max(0, box[1] - PAD), min(R.W, box[2] + PAD), min(R.H, box[3] + PAD))
    cw, ch = box[2] - box[0], box[3] - box[1]
    scale = H_PX / float(ch)
    w_px = int(round(cw * scale))
    print("bbox", box, "-> canvas", (w_px, H_PX))

    for i, im in enumerate(crawl):
        im.crop(box).resize((w_px, H_PX), Image.LANCZOS).save(os.path.join(OUT, "crawl_%d.png" % i))
    for i, im in enumerate(kowtow):
        im.crop(box).resize((w_px, H_PX), Image.LANCZOS).save(os.path.join(OUT, "kowtow_%d.png" % i))
    print("saved 7 frames ->", OUT)


if __name__ == "__main__":
    main()
