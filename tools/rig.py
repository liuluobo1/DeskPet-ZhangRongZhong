# -*- coding: utf-8 -*-
"""姿态装配 v11：胶囊躯干 + 干净胸口布料；四肢挂到正确位置；步态幅度加大。"""
import math
import os
import sys
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "build")
SRC = Image.open(os.path.join(OUT, "cutout.png")).convert("RGBA")   # 由 tools/cutout.py 生成


def fill_holes(img):
    a = np.array(img)
    alpha = a[:, :, 3]
    m = (alpha > 40).astype(np.uint8)
    h, w = m.shape
    ff = m.copy()
    cv2.floodFill(ff, np.zeros((h + 2, w + 2), np.uint8), (0, 0), 1)
    holes = (~ff.astype(bool)) & (m == 0)
    if holes.any():
        bgr = cv2.inpaint(cv2.cvtColor(a[:, :, :3], cv2.COLOR_RGB2BGR), holes.astype(np.uint8) * 255, 3, cv2.INPAINT_TELEA)
        a[:, :, :3] = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
        alpha[holes] = 255
    return Image.fromarray(a)


def dark(im, f):
    a = np.array(im).astype(np.float32)
    a[..., :3] *= f
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def fade_top(im, px):
    w, h = im.size
    g = Image.new("L", (1, h), 255)
    for y in range(min(px, h)):
        g.putpixel((0, y), int(255 * (y / max(1, px)) ** 0.7))
    g = g.resize((w, h)).filter(ImageFilter.GaussianBlur(3))
    a = np.array(im)
    a[:, :, 3] = (a[:, :, 3].astype(np.float32) * np.array(g).astype(np.float32) / 255).astype(np.uint8)
    return Image.fromarray(a)


def head_piece():
    im = fill_holes(SRC.crop((196, 0, 434, 258)))
    a = np.array(im)
    rgb = a[:, :, :3].astype(np.int16)
    blue = (rgb[:, :, 2] > rgb[:, :, 0] + 30) & (rgb[:, :, 2] > 90)
    a[:, :, 3][blue] = 0
    im = Image.fromarray(a)
    m = Image.new("L", im.size, 0)
    ImageDraw.Draw(m).ellipse((7, -4, 231, 236), fill=255)
    m = m.filter(ImageFilter.GaussianBlur(4))
    base = im.getchannel("A").point(lambda v: 255 if v > 60 else 0)
    im.putalpha(Image.composite(im.getchannel("A"), Image.new("L", im.size, 0), Image.composite(m, Image.new("L", im.size, 0), base)))
    return im


SHIRT_TEX = fill_holes(SRC.crop((150, 372, 438, 716))).rotate(-90, expand=True, resample=Image.BICUBIC)
PANTS_TEX = fill_holes(SRC.crop((110, 860, 460, 1246)))
HEAD = head_piece()
ARM = fill_holes(SRC.crop((16, 380, 200, 990)))
LEG = fill_holes(SRC.crop((186, 856, 340, 1246)))

W, H = 820, 500
GROUND = 432
BH = 146
BW = 320
HIP_X = 180
BODY_TOP = GROUND - 108 - BH
BODY_BOT = BODY_TOP + BH
SHO_X = HIP_X + BW

LEG_TOP = BODY_BOT - 58
leg = fade_top(LEG.resize((int(LEG.size[0] * (GROUND - LEG_TOP) / LEG.size[1]), GROUND - LEG_TOP), Image.LANCZOS),
               int((GROUND - LEG_TOP) * 0.26))
ARM_TOP = BODY_BOT - 68
arm = fade_top(ARM.resize((int(ARM.size[0] * (GROUND - ARM_TOP) / ARM.size[1]), GROUND - ARM_TOP), Image.LANCZOS),
               int((GROUND - ARM_TOP) * 0.28))
AW, AH = arm.size
LW, LH = leg.size

HEAD_H = int(BH * 1.18)
head = HEAD.resize((int(HEAD.size[0] * HEAD_H / HEAD.size[1]), HEAD_H), Image.LANCZOS)
HW, HH = head.size
HX = SHO_X - int(HW * 0.56)
HY = BODY_TOP - int(BH * 0.06)


def body_layer():
    """胶囊躯干：前段衬衫布料（露旗子），后段裤料，前低后高一点点。"""
    front_w = int(BW * 0.66)
    strip = Image.new("RGBA", (BW, BH), (0, 0, 0, 0))
    strip.alpha_composite(SHIRT_TEX.resize((front_w, BH), Image.LANCZOS), (0, 0))
    strip.alpha_composite(PANTS_TEX.resize((BW - front_w, BH), Image.LANCZOS), (front_w, 0))
    m = Image.new("L", (BW * 2, BH * 2), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, BW * 2 - 1, BH * 2 - 1), BH, fill=255)
    m = m.resize((BW, BH), Image.LANCZOS)
    strip.putalpha(m)
    return strip


BODY = body_layer()
print("body", (BW, BH), "arm", (AW, AH), "leg", (LW, LH), "head", (HW, HH))


def put(canvas, im, angle, top_left):
    r = im.rotate(angle, expand=True, resample=Image.BICUBIC) if angle else im
    canvas.alpha_composite(r, (int(top_left[0] - (r.size[0] - im.size[0]) / 2),
                               int(top_left[1] - (r.size[1] - im.size[1]) / 2)))
    return r


def scene(pose="crawl", t=0.0, dbg=False):
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse((HIP_X - 60, GROUND - 10, SHO_X + 90, GROUND + 26), fill=(15, 15, 20, 110))
    canvas = Image.alpha_composite(canvas, sh.filter(ImageFilter.GaussianBlur(12)))

    if pose == "crawl":
        sw = math.cos(2 * math.pi * t)
        arm_n = (SHO_X - int(AW * 1.10), ARM_TOP + 2 + 8 * sw)
        arm_f = (SHO_X - int(AW * 1.55), ARM_TOP - 2 - 8 * sw)
        leg_n = (HIP_X + int(BW * 0.16), LEG_TOP + 2 - 8 * sw)
        leg_f = (HIP_X - int(BW * 0.02), LEG_TOP - 2 + 8 * sw)
        a_arm_n, a_arm_f = 6 + 12 * sw, 6 - 12 * sw
        a_leg_n, a_leg_f = 6 - 10 * sw, 6 + 10 * sw
        hx, hy, hang = HX, HY, 4
        body_top = BODY_TOP
    else:
        arm_n = (SHO_X - int(AW * 0.6), BODY_BOT - 34)
        arm_f = (SHO_X - int(AW * 0.95), BODY_BOT - 36)
        leg_n = (HIP_X + int(BW * 0.10), BODY_BOT - 40)
        leg_f = (HIP_X - int(BW * 0.06), BODY_BOT - 38)
        a_arm_n, a_arm_f = -84, -74
        a_leg_n, a_leg_f = 70, 82
        hx, hy, hang = HX + 24, BODY_BOT - int(HH * 0.24) - int(22 * t), -62 + 14 * t
        body_top = BODY_TOP + 22

    # 先贴四肢（顶端藏进躯干下面），再贴躯干，最后贴头
    put(canvas, dark(leg, 0.70), a_leg_f, leg_f)
    put(canvas, dark(arm, 0.74), a_arm_f, arm_f)
    put(canvas, leg, a_leg_n, leg_n)
    put(canvas, arm, a_arm_n, arm_n)
    canvas.alpha_composite(BODY, (HIP_X, body_top))
    put(canvas, head, hang, (hx, hy))
    if dbg:
        c = canvas.copy()
        d = ImageDraw.Draw(c)
        d.line([(0, GROUND), (W, GROUND)], fill=(255, 0, 0, 255), width=2)
        d.rectangle((HIP_X, body_top, SHO_X, body_top + BH), outline=(0, 0, 255, 255), width=2)
        canvas = c
    return canvas


def sheet(path):
    tiles = [("crawl", 0.0), ("crawl", 0.5), ("kowtow", 0.0), ("kowtow", 1.0)]
    out = Image.new("RGB", (W * 2, H * 2), (240, 240, 242))
    for i, (p, t) in enumerate(tiles):
        im = scene(p, t)
        bg = Image.new("RGBA", (W, H), (240, 240, 242, 255))
        bg.alpha_composite(im)
        out.paste(bg.convert("RGB"), ((i % 2) * W, (i // 2) * H))
        im.save(os.path.join(OUT, "v11_%s_%d.png" % (p, int(t * 100))))
    small = Image.new("RGB", (1300, 280), (32, 34, 42))
    x = 16
    for p, t in tiles:
        im = scene(p, t)
        hh = 230
        ww = int(im.size[0] * hh / im.size[1])
        th = im.resize((ww, hh), Image.LANCZOS)
        small.paste(th, (x, 24), th)
        x += ww + 24
    small.save(os.path.join(OUT, "v11_small.png"))
    out.save(path)
    print("saved", path)


if __name__ == "__main__":
    if "--debug" in sys.argv:
        scene("crawl", 0.0, True).save(os.path.join(OUT, "v11_debug.png"))
    sheet(os.path.join(OUT, "v11_sheet.png"))
