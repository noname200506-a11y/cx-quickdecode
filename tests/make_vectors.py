#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 C# 自检用的参考向量（真源：ComfyUI-CX-ImageScramble/cx_core.py，即 image-crypto.js 的等价移植）

用法（Windows python，需 numpy + Pillow）：
  python make_vectors.py --out <vectors_dir> --core <cx_core.py 所在目录>
"""
import argparse
import json
import os
import random
import sys

import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
ap.add_argument("--core", required=True)
ap.add_argument("--seed", type=int, default=20261017)
args = ap.parse_args()

sys.path.insert(0, args.core)
import cx_core as C  # noqa: E402

rng = random.Random(args.seed)
nprng = np.random.RandomState(args.seed & 0x7FFFFFFF)
OUT = args.out
B = C.META_B_PNG

for sub in ("", "png", "cases", "cases_manual"):
    os.makedirs(os.path.join(OUT, sub), exist_ok=True)


def rand_img(w, h):
    """渐变 + 噪声 + 色块 + 细线：既有平滑区也有硬边，便于看出块缝。"""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = (xx / max(w - 1, 1)) * 200 + 20
    g = (yy / max(h - 1, 1)) * 180 + 30
    b = ((np.sin(xx / 7.0) + np.cos(yy / 11.0)) * 0.5 + 0.5) * 160 + 40
    img = np.stack([r, g, b], axis=-1)
    img += nprng.normal(0, 4.0, size=(h, w, 3))
    for _ in range(max(0, (w * h) // 6000) if min(w, h) >= 24 else 0):
        x0 = rng.randrange(0, max(1, w - 16))
        y0 = rng.randrange(0, max(1, h - 16))
        maxbw, maxbh = min(60, w - x0), min(60, h - y0)
        if maxbw <= 8 or maxbh <= 8:
            continue
        bw = rng.randrange(8, maxbw + 1)
        bh = rng.randrange(8, maxbh + 1)
        img[y0:y0 + bh, x0:x0 + bw] = [rng.randrange(256), rng.randrange(256), rng.randrange(256)]
    for _ in range(3):
        y = rng.randrange(0, h)
        img[y:y + 1, :] = 250
    return np.clip(np.rint(img), 0, 255).astype(np.uint8)


def to_rgba(arr):
    if arr.shape[2] == 3:
        a = np.full(arr.shape[:2] + (1,), 255, np.uint8)
        return np.concatenate([arr, a], axis=2)
    return arr


def save_png(arr, path):
    Image.fromarray(arr, "RGBA" if arr.shape[2] == 4 else "RGB").save(path)


# ---------------------------------------------------------------- hash / perm
PASSWORDS = ["", "pw", "测试密码123", "p@ss w0rd!#%&", "🔐emoji密码", "a" * 64, "密码 with space"]
def i32(v):
    v = int(v) & 0xFFFFFFFF
    return v - 0x100000000 if v >= 0x80000000 else v


hash_cases = [{"s": s, "h": i32(C.cx_hash(s))} for s in PASSWORDS]
for _ in range(12):
    n = rng.randrange(0, 24)
    alphabet = "abcXYZ019_-中文测试★☆🙂 "
    s = "".join(rng.choice(alphabet) for _ in range(n))
    hash_cases.append({"s": s, "h": i32(C.cx_hash(s))})
with open(os.path.join(OUT, "hash.json"), "w", encoding="utf-8") as f:
    json.dump(hash_cases, f, ensure_ascii=False, indent=1)

perm_cases = []
for seed in [0, 1, -1, 12345, i32(C.cx_hash("pw#0")), i32(C.cx_hash("测试密码123#1"))]:
    for ln in [1, 2, 3, 8, 64, 4096]:
        perm_cases.append({"seed": int(seed), "len": ln, "p": [int(v) for v in C._perm(ln, seed)]})
with open(os.path.join(OUT, "perm.json"), "w", encoding="utf-8") as f:
    json.dump(perm_cases, f, ensure_ascii=False, indent=1)

# ---------------------------------------------------------------- PNG roundtrip
for i, (w, h) in enumerate([(1, 1), (7, 5), (64, 64), (129, 77), (256, 200)]):
    a = to_rgba(rand_img(w, h))
    if i == 2:
        a[..., 3] = 128
    save_png(a, os.path.join(OUT, "png", f"rt{i}_{w}x{h}.png"))

# ---------------------------------------------------------------- 元数据解码用例
def clamp_rect(x, y, w, h, W, H):
    x0 = max(0, min(x, W - 1))
    y0 = max(0, min(y, H - 1))
    x1 = max(x0 + 1, min(x + w, W))
    y1 = max(y0 + 1, min(y + h, H))
    return x0, y0, x1 - x0, y1 - y0


case_idx = 0
for trial in range(10):
    W = rng.choice([48, 64, 96, 120, 160, 200])
    H = rng.choice([48, 64, 96, 120, 160, 200])
    base = rand_img(W, H)
    nreg = rng.randrange(1, 4)
    regions = []
    for _ in range(nreg):
        bw = rng.randrange(8, max(9, W // 2))
        bh = rng.randrange(8, max(9, H // 2))
        x = rng.randrange(0, max(1, W - bw))
        y = rng.randrange(0, max(1, H - bh))
        regions.append(clamp_rect(x, y, bw, bh, W, H))
    pw = rng.choice(PASSWORDS[1:])
    aligned = C.align_regions_web(regions, W, H, B)
    if not aligned:
        continue
    canvas = C.pad_with_border(to_rgba(base), B)
    shifted = [(x + B, y + B, w, h) for (x, y, w, h) in aligned]
    C.scramble_array(canvas, shifted, 0, pw, B, False, True)
    assert C.v3_write_meta(canvas, aligned, C.secret_text(0, pw), B)
    name = f"case{case_idx:02d}"
    save_png(canvas, os.path.join(OUT, "cases", name + ".png"))
    # 期望：用同一份 cx_core 解码
    dec = canvas.copy()
    C.scramble_array(dec, shifted, 0, pw, B, False, False)
    expect = np.ascontiguousarray(dec[B:-B, B:-B])
    save_png(expect, os.path.join(OUT, "cases", name + ".expect.png"))
    with open(os.path.join(OUT, "cases", name + ".json"), "w", encoding="utf-8") as f:
        json.dump({"password": pw, "regions": aligned, "size": [W, H],
                   "sig": C.v3_signature(len(aligned), aligned, C.secret_text(0, pw))}, f,
                  ensure_ascii=False)
    case_idx += 1

# ---------------------------------------------------------------- 手动区域用例（unit16 / soft）
for j, (unit, soft) in enumerate([(8, False), (16, True), (16, False), (8, True)]):
    W, H = 96, 80
    base = rand_img(W, H)
    regions = [(0, 0, W, H)]
    if j % 2 == 1:
        regions = [(8, 8, 64, 48)]
    enc = to_rgba(base).copy()
    pw = "pw"
    C.scramble_array(enc, regions, 0, pw, unit, soft, True)
    dec = enc.copy()
    C.scramble_array(dec, regions, 0, pw, unit, soft, False)
    save_png(enc, os.path.join(OUT, "cases_manual", f"m{j}.png"))
    save_png(dec, os.path.join(OUT, "cases_manual", f"m{j}.expect.png"))
    with open(os.path.join(OUT, "cases_manual", f"m{j}.json"), "w", encoding="utf-8") as f:
        json.dump({"password": pw, "regions": regions, "unit": unit, "soft": soft,
                   "size": [W, H]}, f, ensure_ascii=False)

print("vectors written to", OUT, "cases:", case_idx)
