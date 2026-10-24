#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""sortRegion（色块排序，网页端 PNG 增强开关 flag bit1）参考向量

逻辑逐行照抄 cx-E2EE src/image-crypto.js 的 P.sortRegion（块恒 8×8，rbit = ceil(log2(bl)) || 1）。
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
args = ap.parse_args()
os.makedirs(args.out, exist_ok=True)


def sort_region(d, w, h, x0, y0, bx, by, bl, enc):
    """d: (h,w,4) uint8 原地；语义与 JS 版一致（只动 RGB，不动 alpha）。"""
    rbit = math.ceil(math.log2(bl)) if bl > 1 else 0
    if rbit < 1:
        rbit = 1
    U = 8

    def blk_xy(i):
        return x0 + (i % bx) * U, y0 + (i // bx) * U

    if enc:
        lum = np.zeros(bl, np.float64)
        for i in range(bl):
            bxx, byy = blk_xy(i)
            s = 0.0
            for y in range(U):
                for l in range(U):
                    px = d[byy + y, bxx + l]
                    s += px[0] * .299 + px[1] * .587 + px[2] * .114
            lum[i] = s / 64.0
        ord_ = sorted(range(bl), key=lambda i: lum[i])
        tmp = d.copy()
        for j in range(bl):
            src = ord_[j]
            sbx, sby = blk_xy(src)
            dbx, dby = blk_xy(j)
            for y in range(U):
                for l in range(U):
                    d[dby + y, dbx + l, :3] = tmp[sby + y, sbx + l, :3]
            for b in range(rbit):
                d[dby, dbx + b, 0] = (d[dby, dbx + b, 0] & 254) | ((src >> b) & 1)
    else:
        tmp = d.copy()
        idx = np.zeros(bl, np.int32)
        for j in range(bl):
            bxx, byy = blk_xy(j)
            v = 0
            for b in range(rbit):
                v |= (int(tmp[byy, bxx + b, 0]) & 1) << b
            idx[j] = v
        for j in range(bl):
            dst = int(idx[j])
            if dst < 0 or dst >= bl:
                continue
            sbx, sby = blk_xy(j)
            dbx, dby = blk_xy(dst)
            for y in range(U):
                for l in range(U):
                    d[dby + y, dbx + l, :3] = tmp[sby + y, sbx + l, :3]
    return d


rs = np.random.RandomState(4242)
cases = [
    (128, 96, 0, 0, 4, 3),       # bl = 12 → rbit 4
    (96, 96, 8, 8, 3, 3),        # bl = 9  → rbit 4
    (64, 64, 0, 0, 1, 1),        # bl = 1  → rbit 1
    (160, 120, 16, 0, 8, 5),     # bl = 40 → rbit 6
]

for k, (w, h, x0, y0, bx, by) in enumerate(cases):
    bl = bx * by
    img = np.zeros((h, w, 4), np.uint8)
    img[..., :3] = rs.randint(0, 256, (h, w, 3))
    img[..., 3] = 255
    Image.fromarray(img, "RGBA").save(os.path.join(args.out, f"sort{k}.in.png"))

    enc = img.copy()
    sort_region(enc, w, h, x0, y0, bx, by, bl, True)
    Image.fromarray(enc, "RGBA").save(os.path.join(args.out, f"sort{k}.enc.png"))

    dec = enc.copy()
    sort_region(dec, w, h, x0, y0, bx, by, bl, False)
    Image.fromarray(dec, "RGBA").save(os.path.join(args.out, f"sort{k}.dec.png"))

    with open(os.path.join(args.out, f"sort{k}.json"), "w", encoding="utf-8") as f:
        json.dump({"w": w, "h": h, "x0": x0, "y0": y0, "bx": bx, "by": by, "bl": bl}, f)

print("sort vectors written to", args.out)
