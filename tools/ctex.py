#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ctex —— 本项目 .ctex 贴图的读写模块（Godot 导入产物）。

## 格式（实测，非假设）
对 `assets/` 里全部 **93 个** `.ctex` 逐个读头统计，结果**完全统一**：

    @0   magic  4 B    b"GST2"
    @4   ver    u32    1
    @8   w      u32    宽
    @12  h      u32    高
    @16  ——     20 B   全零（保留）
    @36  fmt    u32    2 = WebP
    @40  w2     u16    宽（与 @8 重复；读这个更省事）
    @42  h2     u16    高
    @44  a      u32    常量 0
    @48  b      u32    常量 5
    @52  len    u32    载荷字节数
    @56  payload       **lossless WebP**

> ⚠️ 另有一种**原版**布局：载荷从 **52** 起、压缩格式为 **BC7/BPTC**（不是 WebP）。
> 那只出现在游戏自带的 `.pck` 里（我们要读原版图集取参考图时才会遇到）。
> 本模块的 `read()` 两种都认；`write()` 只写本项目格式（56 字节头 + WebP）。

## 用法
    from ctex import read_ctex, write_ctex
    img, meta = read_ctex("foo.ctex")     # meta: {"w","h","fmt","payload_at","payload_len"}
    write_ctex("bar.ctex", img)           # 按本项目格式写盘
"""
from __future__ import annotations

import io
import struct

from PIL import Image

# ── 格式常量（唯一来源）────────────────────────────────────────────────────
MAGIC = b"GST2"
VERSION = 1
FMT_WEBP = 2                      # @36 的取值；实测本项目 93/93 都是 2
HEADER_SIZE = 56                  # 本项目：载荷紧跟在 56 字节头之后
VANILLA_HEADER_SIZE = 52          # 原版：载荷从 52 起（配 BC7），实测于游戏自带 pck
RESERVED = b"\x00" * 20           # @16..35 全零
FIELD_A = 0                       # @44 常量（含义未知，照抄实测值）
FIELD_B = 5                       # @48 常量（含义未知，照抄实测值）

OFF_VER, OFF_W, OFF_H, OFF_FMT = 4, 8, 12, 36
OFF_W2, OFF_H2, OFF_A, OFF_B, OFF_LEN = 40, 42, 44, 48, 52


class CtexError(RuntimeError):
    """ctex 结构不符合预期。"""


def read_ctex(path: str) -> tuple[Image.Image, dict]:
    """读 .ctex；同时支持本项目格式（WebP@56）与原版格式（BC7@52）。"""
    with open(path, "rb") as f:
        raw = f.read()
    if len(raw) < HEADER_SIZE:
        raise CtexError(f"文件太小：{path}（{len(raw)} B）")
    if raw[:4] != MAGIC:
        raise CtexError(f"magic 不是 GST2，实际 {raw[:4]!r}：{path}")

    w2, h2 = struct.unpack_from("<HH", raw, OFF_W2)
    fmt = struct.unpack_from("<I", raw, OFF_FMT)[0]

    if fmt == FMT_WEBP:
        payload_at = HEADER_SIZE
        img = Image.open(io.BytesIO(raw[payload_at:])).convert("RGBA")
        kind = "webp"
    else:
        # 原版 BC7/BPTC：按需延迟导入，避免非必要依赖
        try:
            import texture2ddecoder  # type: ignore
        except ImportError as e:
            raise CtexError(
                f"{path} 是原版 BC7 格式（fmt={fmt}），读它需要 texture2ddecoder：{e}"
            ) from e
        payload_at = VANILLA_HEADER_SIZE
        arr = texture2ddecoder.decode_bc7(raw[payload_at:], w2, h2)
        import numpy as np
        a = np.frombuffer(arr, dtype=np.uint8).reshape(h2, w2, 4)
        img = Image.fromarray(a[:, :, [2, 1, 0, 3]], "RGBA")   # 解码返回 BGRA
        kind = "bc7"

    meta = {
        "w": w2, "h": h2, "fmt": fmt, "kind": kind,
        "payload_at": payload_at, "payload_len": len(raw) - payload_at,
    }
    if img.size != (w2, h2):
        raise CtexError(f"{path} 头部声明 {w2}x{h2}，实际解出 {img.size[0]}x{img.size[1]}")
    return img, meta


def encode_ctex(img: Image.Image) -> bytes:
    """把 PIL 图编码成本项目 .ctex 字节（56 字节头 + lossless WebP）。"""
    w, h = img.size
    if not (0 < w < 65536 and 0 < h < 65536):
        raise CtexError(f"尺寸超出 u16 范围：{w}x{h}")
    buf = io.BytesIO()
    # exact=True：PIL 默认会把「全透明像素」的 RGB 抹成 0，导致往返后像素不一致。
    # 实测：不加这个参数，93 个里有 21 个往返后像素漂移。
    img.convert("RGBA").save(buf, format="WEBP", lossless=True, exact=True)
    payload = buf.getvalue()

    head = bytearray()
    head += MAGIC
    head += struct.pack("<III", VERSION, w, h)
    head += RESERVED
    head += struct.pack("<I", FMT_WEBP)
    head += struct.pack("<HH", w, h)
    head += struct.pack("<II", FIELD_A, FIELD_B)
    head += struct.pack("<I", len(payload))
    if len(head) != HEADER_SIZE:
        raise CtexError(f"内部不一致：头 {len(head)} B != {HEADER_SIZE} B")
    return bytes(head) + payload


def write_ctex(path: str, img: Image.Image) -> int:
    """写 .ctex，返回载荷字节数。"""
    data = encode_ctex(img)
    with open(path, "wb") as f:
        f.write(data)
    return len(data) - HEADER_SIZE


if __name__ == "__main__":
    import sys
    for p in sys.argv[1:]:
        im, meta = read_ctex(p)
        print(f"{p}: {im.size} fmt={meta['fmt']} {meta['kind']}@{meta['payload_at']}")
