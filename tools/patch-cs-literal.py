#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按"旧串 → 新串"精确替换 C# 源码里的**兜底文案字面量**。

为什么不用手改：兜底文案与本地化 JSON 是**同一句话的两份副本**（审计 §2.1.2），
两边必须逐字一致；用脚本改可以顺手断言"旧串恰好出现 1 次"，
避免改漏一处、或改到了同名的别处。

用法：
  python tools/patch-cs-literal.py --file <cs> --old <旧串> --new <新串>

退出码：0 成功；非 0 表示旧串命中次数 != 1（那就不动文件，避免误伤）。
"""
import argparse
import sys
from pathlib import Path

ENC = "utf-8"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", required=True)
    ap.add_argument("--old", required=True)
    ap.add_argument("--new", required=True)
    args = ap.parse_args()

    path = Path(args.file)
    if not path.is_file():
        sys.exit(f"[FAIL] 文件不存在：{path}")

    raw = path.read_bytes()
    if raw[:3] == b"\xef\xbb\xbf":
        sys.exit(f"[FAIL] {path} 有 UTF-8 BOM")
    text = raw.decode(ENC)

    hits = text.count(args.old)
    if hits != 1:
        sys.exit(f"[FAIL] 旧串命中 {hits} 次（期望 1），未改动：{path.name}")
    if args.old == args.new:
        sys.exit("[FAIL] 新旧串相同")

    path.write_text(text.replace(args.old, args.new, 1), encoding=ENC, newline="")
    print(f"[OK] {path.name}")
    print(f"     旧：{args.old}")
    print(f"     新：{args.new}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
