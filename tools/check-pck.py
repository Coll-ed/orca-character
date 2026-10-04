#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-pck.py —— 打包产物门槛校验（**体量 + 条目数**双门槛）

## 为什么需要它(而不是靠 Godot 的退出码)
Spine 资源依赖**游戏目录的 GDExtension**,独立导出时**必然**报
`Cannot get class 'SpineSkeletonDataResource'` 并返回非零退出码,
但产物其实是完整的 —— 用退出码判定会永远误报失败。

反过来,完全不校验也不行:曾实测一次失败的导出把可用的 54 MB `.pck`
覆盖成 2.5 MB 残包。

## 为什么"体量"这一个判据不够（2026-10-05 实测踩到）
`Godot --export-pack` 在**没有 GDExtension** 的环境里会**静默丢弃**整类资源:

    Godot 导出   261 条目 / 53,867,608 B   ← 缺 Spine `.spatlas`/`.spskel`
                                            与 `.godot/exported/**` 共 73 个文件
    build-pck.py 334 条目 / 58,158,427 B   ← 完整

53.8 MB **远高于** 20 MB 的体量门槛 ⇒ 残包**轻松过检**。
这与踩坑指南坑 3-8「文件存在且非空当成已完成」是同一类错误:**弱判据被读成成功**。
⇒ 本工具因此升级为**两个判据**,并**解析产物本身**(复用 `pcktool.read_index`,
不另写一份 pck 格式解析):

    ① 体量 ≥ `--min-bytes`
    ② 条目数 ≥ `--min-entries`

## 用法
    python tools/check-pck.py <pck 路径> [--min-bytes N] [--min-entries N]
退出码 0 = 通过;非 0 = 拒绝(构建应失败,**不许静默通过**)。
"""
from __future__ import annotations

import argparse
import os
import sys

# 与 pcktool.py 同目录 ⇒ 直接复用它的解析（单一来源）
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pcktool import PckError, read_index  # noqa: E402

# 门槛默认值(具名常量,附理由):
#   官方基准 54,635,766 B;本工程完整导出实测 52,648,248 B(96.4%)。
#   取 20,000,000 B(约官方 36.6%)—— 远高于曾出现的 2,518,204 B 残包(4.6%),
#   又留足余量,避免正常的资源差异被误判为失败。
DEFAULT_MIN_BYTES = 20_000_000

# 条目数门槛理由(2026-10-05 实测):
#   完整包 334 条目(= 资源树文件数);`Godot --export-pack` 残包 261 条目(丢 73 个)。
#   取 300 —— 高于 261(拦得住残包),又低于 334(留出正常增删资源的余量)。
DEFAULT_MIN_ENTRIES = 300


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("pck", help="待校验的 .pck 路径")
    ap.add_argument("--min-bytes", type=int, default=DEFAULT_MIN_BYTES,
                    help=f"体量下限(字节),默认 {DEFAULT_MIN_BYTES:,}")
    ap.add_argument("--min-entries", type=int, default=DEFAULT_MIN_ENTRIES,
                    help=f"条目数下限,默认 {DEFAULT_MIN_ENTRIES}")
    args = ap.parse_args()

    if not os.path.exists(args.pck):
        print(f"[FAIL] 未产出 .pck: {args.pck}", file=sys.stderr)
        return 1

    size = os.path.getsize(args.pck)
    if size < args.min_bytes:
        # 用 ASCII 箭头:Windows 控制台默认 GBK,非 ASCII 符号会被转义成 \uXXXX
        print(f"[FAIL] .pck 体量异常: {size:,} B < 门槛 {args.min_bytes:,} B"
              f" => 资源大面积缺失,拒绝覆盖可用产物", file=sys.stderr)
        return 1

    # ② 条目数：解析产物目录区（结构不对/截断 → PckError，同样是拒绝）
    try:
        header, _entries = read_index(args.pck)
    except (PckError, OSError) as ex:
        print(f"[FAIL] .pck 目录区无法解析: {ex}", file=sys.stderr)
        return 1

    count = int(header["count"])
    if count < args.min_entries:
        print(f"[FAIL] .pck 条目数异常: {count} < 门槛 {args.min_entries}"
              f" => 整类资源被静默丢弃(常见于 Godot 独立导出缺 GDExtension),"
              f"请改用 tools/build-pck.py 打包", file=sys.stderr)
        return 1

    print(f"[OK] .pck {size:,} B ≥ 门槛 {args.min_bytes:,} B"
          f";  条目数 {count} ≥ 门槛 {args.min_entries}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
