#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""只读校验：pck 里某个资源的内容是否包含指定文本。

用途：改了本地化/资源后，确认**部署产物**里真的是新内容（不是"我以为打包了"）。
判据取自产物本身（踩坑指南坑 3-1：任何关于"游戏实际读到了什么"的判断必须从部署产物取证）。

用法：python verify-pck-content.py <pck> <资源路径> <要查找的文本>
退出码：0 = 命中；1 = 未命中（不许静默通过）。
"""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pcktool import read_index  # noqa: E402


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2

    pck, res_path, needle = sys.argv[1], sys.argv[2], sys.argv[3]
    header, entries = read_index(pck)

    entry = next((e for e in entries if e.path == res_path), None)
    if entry is None:
        # 路径不符就模糊匹配一次，把候选打出来方便定位
        cands = [e.path for e in entries if res_path.split("/")[-1] in e.path]
        print(f"[FAIL] 产物里没有 {res_path}；同名候选 {cands[:5]}", file=sys.stderr)
        return 1

    with open(pck, "rb") as f:
        f.seek(header["file_base"] + entry.offset)
        data = f.read(entry.size)

    text = data.decode("utf-8", errors="replace")
    if needle not in text:
        print(f"[FAIL] {res_path}（{entry.size:,} B）里未找到：{needle!r}", file=sys.stderr)
        return 1

    print(f"[OK] {res_path}（{entry.size:,} B）命中：{needle!r}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
