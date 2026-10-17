# -*- coding: utf-8 -*-
"""Spike: 从照片里粗抠出人物，检验抠图可行性（后续用于做狗爬/磕头姿态）。"""
import os
import cv2
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "source.jpg")
OUT = os.path.join(ROOT, "build")
os.makedirs(OUT, exist_ok=True)

SCALE = 2
im = Image.open(SRC).convert("RGB")
W, H = im.size
im = im.resize((W * SCALE, H * SCALE), Image.LANCZOS)
im.save(os.path.join(OUT, "src_2x.png"))
bgr = cv2.cvtColor(np.array(im), cv2.COLOR_RGB2BGR)
h, w = bgr.shape[:2]
print("up", w, h)

mask = np.zeros((h, w), np.uint8)
rect = (int(w * 0.03), int(h * 0.015), int(w * 0.94), int(h * 0.975))
bgd = np.zeros((1, 65), np.float64)
fgd = np.zeros((1, 65), np.float64)
cv2.grabCut(bgr, mask, rect, bgd, fgd, 6, cv2.GC_INIT_WITH_RECT)
fg = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
# 闭运算补洞
k = np.ones((5, 5), np.uint8)
fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, k, iterations=2)

alpha = cv2.GaussianBlur(fg, (0, 0), 1.2)
rgba = cv2.cvtColor(bgr, cv2.COLOR_BGR2BGRA)
rgba[:, :, 3] = alpha
cv2.imwrite(os.path.join(OUT, "cutout.png"), rgba)
print("fg px", int((fg > 0).sum()), "of", fg.size)

# 棋盘格预览
tile = 16
chk = np.zeros((h, w, 3), np.uint8)
for y in range(0, h, tile):
    for x in range(0, w, tile):
        chk[y:y + tile, x:x + tile] = 200 if ((x // tile + y // tile) % 2) else 140
a = (alpha.astype(np.float32) / 255.0)[..., None]
prev = (bgr.astype(np.float32) * a + chk.astype(np.float32) * (1 - a)).astype(np.uint8)
cv2.imwrite(os.path.join(OUT, "cutout_checker.png"), prev)
print("done")
