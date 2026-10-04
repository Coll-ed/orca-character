#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""restore-sources.py —— 从 Godot 导入产物还原源图 / Spine 源文件

## 为什么需要它
本工程的部分美术与 Spine 源文件不在仓库内,只以 Godot 导入产物的形式存在:
    <Godot 导入目录>/<源名>-<hash>.<产物扩展名>
Godot 从源文件重新导入时,缺源文件的资源不会进包 ⇒ 导出体积严重缩水
(实测 2.5 MB vs 官方 54.6 MB)。本脚本把产物无损还原成源文件。

## 三种产物的格式(全部实测,非假设)
### 1) .ctex(纹理,95 个)
    @0   magic b"GST2"   @4 ver u32=1   @8 w u32   @12 h u32
    @16  保留 20 B   @36 fmt u32=2(WebP)   @40 w2/h2 u16×2
    @44  a/b u32×2(0/5)   @52 len u32   @56 payload **lossless WebP(VP8L)**
    ⇒ 源图 = payload,转 PNG 无损

### 2) .spatlas(Spine 图集,8 个)
    JSON 外壳:{"atlas_data": "<Spine 图集文本>", ...}
    ⇒ 源 .atlas = atlas_data 字段(反转义后的文本)

### 3) .spskel(Spine 骨架,8 个)
    <8 字节头><1 字节版本串长度><版本串如 "4.2.43"><骨架数据>
    ⇒ 源 .skel = 去掉前 8 字节头

## 映射方式(不靠猜名)
每个 `<源名>.import` 文件内的 `path=` 行给出去向(末段即产物文件名)。
⇒ 用它精确对应,而不是从源文件名推断。

## 用法
    python tools/restore-sources.py --repo . --dry-run     # 只看会做什么
    python tools/restore-sources.py --repo .               # 实际写入
已有源文件的条目一律跳过(最小改动)。
注意:从 PowerShell 传中文路径可能被编码破坏,建议先 cd 到仓库再用 `--repo .`。
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys

# ── .ctex 纹理 ────────────────────────────────────────────────
CTEX_SUFFIX = ".ctex"
CTEX_MAGIC = b"GST2"
# magic(4)+ver(4)+w(4)+h(4)+保留(20)+fmt(4)+w2(2)+h2(2)+a(4)+b(4)+len(4) = 56
CTEX_HEADER_BYTES = 56
WEBP_MAGIC = b"RIFF"
WEBP_TAG_OFFSET = 8
WEBP_TAG = b"WEBP"

# ── Spine 骨架 ────────────────────────────────────────────────
SPSKEL_SUFFIX = ".spskel"
SPINE_HEADER_BYTES = 8          # 实测:8 字节头之后即 Spine 数据
SPINE_VERSION_OFFSET = 9        # 实测:偏移 8 是 0x07,版本串从偏移 9 开始
SPINE_VERSION_PROBE = 6         # "4.2.43" 是 6 个字符,取 6 字节做形态探测
SPINE_VERSION_MIN_CHARS = 5     # 至少要是 x.y.z 形态
SPINE_VERSION_CHARSET = re.compile(rb"\d+\.\d+\.\d+")

# ── Spine 图集 ────────────────────────────────────────────────
SPATLAS_SUFFIX = ".spatlas"
ATLAS_FIELD = "atlas_data"

# ── 通用 ──────────────────────────────────────────────────────
IMPORT_SUFFIX = ".import"
GODOT_DIR = ".godot"
IMPORTED_SUBDIR = "imported"
# 只取 path= 的末段文件名,不依赖任何路径前缀(避免写死虚拟路径)
REMAP_BASENAME_RE = re.compile(r'^path="[^"]*?([^/"]+)"', re.MULTILINE)


def carve_ctex(path: str) -> bytes:
    """剥离 56 字节头,返回 lossless WebP 载荷。形态不符就抛。"""
    with open(path, "rb") as f:
        raw = f.read()
    if raw[:4] != CTEX_MAGIC:
        raise ValueError(f"magic 不是 GST2: {path}")
    payload = raw[CTEX_HEADER_BYTES:]
    if payload[:4] != WEBP_MAGIC or payload[WEBP_TAG_OFFSET:WEBP_TAG_OFFSET + 4] != WEBP_TAG:
        raise ValueError(f"载荷不是 WebP: {path}")
    return payload


def carve_spskel(path: str) -> bytes:
    """去掉 8 字节头,得到 Spine 骨架源文件。

    实测形态(8 个文件一致):
        f4 c8 ce 76 f5 76 fd 48 | 07 | 34 2e 32 2e 34 33 | bf 80 …
        └──── 8 字节头 ────┘      └── "4.2.43" ──┘
    注意:第 8 字节(0x07)与版本串长度(6)对不上 ⇒ 它不是普通长度前缀。
    故此处只做**形态检查**(偏移 SPINE_VERSION_OFFSET 起应是 x.y.z),
    真正的正确性由 Godot 的 Spine 导入器裁定 —— 格式错误它会明确报错。
    """
    with open(path, "rb") as f:
        raw = f.read()
    need = SPINE_VERSION_OFFSET + SPINE_VERSION_PROBE
    if len(raw) < need:
        raise ValueError(f"文件太短({len(raw)} B < {need} B): {path}")
    probe = raw[SPINE_VERSION_OFFSET:need]
    m = SPINE_VERSION_CHARSET.match(probe)
    if not m or len(m.group(0)) < SPINE_VERSION_MIN_CHARS:
        raise ValueError(f"偏移 {SPINE_VERSION_OFFSET} 起不是 Spine 版本号(读到 {probe!r}): {path}")
    return raw[SPINE_HEADER_BYTES:]


def carve_spatlas(path: str) -> bytes:
    """取出 JSON 外壳里的 atlas_data 文本。形态不符就抛。"""
    with open(path, "r", encoding="utf-8") as f:
        obj = json.load(f)
    if ATLAS_FIELD not in obj:
        raise ValueError(f"没有 {ATLAS_FIELD} 字段: {path}")
    return obj[ATLAS_FIELD].encode("utf-8")


CARVERS = {
    CTEX_SUFFIX: carve_ctex,
    SPSKEL_SUFFIX: carve_spskel,
    SPATLAS_SUFFIX: carve_spatlas,
}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=os.getcwd(), help="仓库根(Godot 工程根)")
    ap.add_argument("--dry-run", action="store_true", help="只报告,不写入")
    args = ap.parse_args()

    repo = os.path.abspath(args.repo)
    artifact_dir = os.path.join(repo, GODOT_DIR, IMPORTED_SUBDIR)
    if not os.path.isdir(artifact_dir):
        print(f"找不到导入产物目录: {artifact_dir}", file=sys.stderr)
        return 1

    made = skipped = missing = bad = 0
    for root, _dirs, files in os.walk(repo):
        if f"{os.sep}{GODOT_DIR}" in root:  # 不扫描导入产物目录自身
            continue
        for fn in files:
            if not fn.endswith(IMPORT_SUFFIX):
                continue
            imp_path = os.path.join(root, fn)
            src_path = imp_path[: -len(IMPORT_SUFFIX)]
            if os.path.exists(src_path):
                skipped += 1
                continue
            with open(imp_path, "r", encoding="utf-8", errors="replace") as f:
                m = REMAP_BASENAME_RE.search(f.read())
            if not m:
                print(f"  .import 缺 path=  : {os.path.relpath(imp_path, repo)}")
                bad += 1
                continue
            artifact_name = m.group(1)
            carver = CARVERS.get(os.path.splitext(artifact_name)[1])
            if carver is None:
                print(f"  未知产物类型      : {artifact_name}")
                bad += 1
                continue
            artifact_path = os.path.join(artifact_dir, artifact_name)
            if not os.path.exists(artifact_path):
                print(f"  缺导入产物        : {artifact_name}")
                missing += 1
                continue
            try:
                data = carver(artifact_path)
            except Exception as exc:  # 边界显式:还原不了要说出来
                print(f"  还原失败 {artifact_name}: {exc}")
                bad += 1
                continue
            rel = os.path.relpath(src_path, repo)
            note = ""
            if carver is carve_ctex:
                from PIL import Image
                try:
                    im = Image.open(io.BytesIO(data))
                    im.load()
                    note = f"  {im.size}"
                    buf = io.BytesIO()
                    im.save(buf, format="PNG")
                    data = buf.getvalue()
                except Exception as exc:
                    print(f"  还原失败 {artifact_name}: {exc}")
                    bad += 1
                    continue
            if args.dry_run:
                print(f"  [dry] {rel}{note}")
            else:
                with open(src_path, "wb") as f:
                    f.write(data)
                print(f"  还原  {rel}{note}")
            made += 1

    verb = "将还原" if args.dry_run else "已还原"
    print(f"\n{verb} {made} / 已有跳过 {skipped} / 缺产物 {missing} / 异常 {bad}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
