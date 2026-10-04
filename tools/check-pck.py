#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-pck.py —— 导出产物门槛校验

## 为什么需要它(而不是靠 Godot 的退出码)
Spine 资源依赖**游戏目录的 GDExtension**,独立导出时**必然**报
`Cannot get class 'SpineSkeletonDataResource'` 并返回非零退出码,
但产物其实是完整的 —— 用退出码判定会永远误报失败。

反过来,完全不校验也不行:曾实测一次失败的导出把可用的 54 MB `.pck`
覆盖成 2.5 MB 残包。故判据改为**看产物本身**:必须存在,且体量不低于门槛。

## 用法(csproj 的 GodotPublish 会调用它)
    python tools/check-pck.py <pck 路径> --min-bytes <门槛字节>
退出码 0 = 通过;非 0 = 拒绝(构建应失败)。
"""
from __future__ import annotations

import argparse
import os
import sys

# 门槛默认值(具名常量,附理由):
#   官方基准 54,635,766 B;本工程完整导出实测 52,648,248 B(96.4%)。
#   取 20,000,000 B(约官方 36.6%)—— 远高于曾出现的 2,518,204 B 残包(4.6%),
#   又留足余量,避免正常的资源差异被误判为失败。
DEFAULT_MIN_BYTES = 20_000_000


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("pck", help="待校验的 .pck 路径")
    ap.add_argument("--min-bytes", type=int, default=DEFAULT_MIN_BYTES,
                    help=f"体量下限(字节),默认 {DEFAULT_MIN_BYTES:,}")
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

    print(f"[OK] .pck {size:,} B ≥ 门槛 {args.min_bytes:,} B")
    return 0


if __name__ == "__main__":
    sys.exit(main())
