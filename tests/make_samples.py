#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成给用户实际测试用的样例图（打码图 + 原图对照 + 手动区域样例）

用法：
  python make_samples.py --out <samples_dir> --core <cx_core.py 所在目录>
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
ap.add_argument("--core", required=True)
args = ap.parse_args()
sys.path.insert(0, args.core)
import cx_core as C  # noqa: E402

os.makedirs(args.out, exist_ok=True)
B = C.META_B_PNG
PW = "demo2026"


def make_photo(w, h, seed=7):
    """画一张有文字、色块、渐变的"假截图"，还原时要能一眼看出对错。"""
    rs = np.random.RandomState(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    base = np.stack([
        40 + (xx / max(w - 1, 1)) * 180,
        60 + (yy / max(h - 1, 1)) * 150,
        90 + ((np.sin(xx / 40.0) * np.cos(yy / 37.0)) * 0.5 + 0.5) * 130,
    ], axis=-1)
    img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB")
    dr = ImageDraw.Draw(img)
    # 棋盘 + 色条（细节多，好坏一眼能看出）
    for i in range(0, w, 40):
        for j in range(0, h, 40):
            if (i // 40 + j // 40) % 2 == 0:
                dr.rectangle([i, j, i + 39, j + 39], outline=(255, 255, 255), width=1)
    for k, col in enumerate([(230, 60, 60), (60, 200, 120), (70, 130, 240), (250, 200, 60)]):
        dr.rectangle([20, 20 + k * 26, 20 + 160, 40 + k * 26], fill=col)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 34)
        font2 = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 20)
    except Exception:
        font = font2 = ImageFont.load_default()
    dr.text((22, h - 70), "CX 打码测试图 demo2026", font=font, fill=(255, 255, 255))
    dr.text((22, h - 32), "把这张图拖进「CX 快速解码」试试", font=font2, fill=(235, 235, 235))
    # 一段"敏感内容"区（打码区）
    dr.rectangle([w // 2 - 150, h // 2 - 90, w // 2 + 150, h // 2 + 90], fill=(28, 32, 44))
    dr.text((w // 2 - 130, h // 2 - 40), "SECRET 区", font=font, fill=(255, 220, 120))
    dr.text((w // 2 - 130, h // 2 + 4), "ID: 2026-1017-0042", font=font2, fill=(180, 220, 255))
    dr.text((w // 2 - 130, h // 2 + 34), "密码会把这个区域打乱", font=font2, fill=(190, 190, 190))
    arr = np.array(img)
    arr = np.clip(arr.astype(np.int16) + rs.randint(-3, 4, arr.shape), 0, 255).astype(np.uint8)
    return arr


def rgba(a):
    return np.concatenate([a, np.full(a.shape[:2] + (1,), 255, np.uint8)], axis=2)


def save(a, name):
    Image.fromarray(a, "RGBA").save(os.path.join(args.out, name))


def meta_scramble(base, regions, pw, name):
    H, W = base.shape[0], base.shape[1]
    aligned = C.align_regions_web(regions, W, H, B)
    canvas = C.pad_with_border(rgba(base), B)
    shifted = [(x + B, y + B, w, h) for (x, y, w, h) in aligned]
    C.scramble_array(canvas, shifted, 0, pw, B, False, True)
    assert C.v3_write_meta(canvas, aligned, C.secret_text(0, pw), B)
    save(canvas, name + ".scrambled.png")
    save(rgba(base), name + ".original.png")
    dec = canvas.copy()
    C.scramble_array(dec, shifted, 0, pw, B, False, False)
    save(np.ascontiguousarray(dec[B:-B, B:-B]), name + ".expect.png")
    return aligned


# 1) 单区域
base1 = make_photo(720, 480, 11)
w1, h1 = base1.shape[1], base1.shape[0]
meta_scramble(base1, [(w1 // 2 - 150, h1 // 2 - 90, 300, 180)], PW, "01-single-region")

# 2) 两个区域
base2 = make_photo(720, 480, 12)
w2, h2 = base2.shape[1], base2.shape[0]
meta_scramble(base2, [(60, 250, 220, 160), (w2 - 300, 60, 240, 160)], PW, "02-two-regions")

# 3) 大图（性能测试）
base3 = make_photo(1280, 800, 13)
w3, h3 = base3.shape[1], base3.shape[0]
meta_scramble(base3, [(w3 // 2 - 200, h3 // 2 - 120, 400, 240)], PW, "03-large-1280x800")

# 4) 无元数据（JPEG 路径风格：unit=16 / soft）—— 用 --aabb 或自动模式还原
base4 = make_photo(640, 400, 14)
w4, h4 = base4.shape[1], base4.shape[0]
regions4 = [(160, 110, 320, 200)]
enc4 = rgba(base4).copy()
C.scramble_array(enc4, regions4, 0, PW, 16, True, True)
save(enc4, "04-manual-unit16-soft.png")
save(rgba(base4), "04-manual-unit16-soft.original.png")
dec4 = enc4.copy()
C.scramble_array(dec4, regions4, 0, PW, 16, True, False)
save(dec4, "04-manual-unit16-soft.expect.png")

with open(os.path.join(args.out, "README.txt"), "w", encoding="utf-8") as f:
    f.write(f"""CX 快速解码 —— 测试样例（全部密码：{PW}）

01-single-region.scrambled.png   网页端 PNG 口径打码图（外扩 8px 边框 + 区域表在 LSB）
                                 直接拖进悬浮窗 / 复制到剪贴板 / Ctrl+Alt+Q，密码 {PW}
01-single-region.original.png    原图（对照）
01-single-region.expect.png      本工具应当给出的结果（有损，肉眼与原图几乎一致）

02-two-regions.*                 两个区域的打码图（验证多区域逆序还原）
03-large-1280x800.*              1280×800 大图（看耗时）
04-manual-unit16-soft.png        没有任何元数据的打码图（模拟 JPEG/拍屏风格：unit=16、soft=1）
                                 → 用「截图解码」框住整张图，或命令行：
                                   CxQuickDecode.exe --decode 04-manual-unit16-soft.png --password {PW} --aabb "160,110,320,200" --unit 16 --soft 1 --out out.png
                                 → 也可以只给 --decode 让它自动择优（会打印 unit/soft/偏移）
""")
print("samples written to", args.out)
