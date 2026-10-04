#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""精确改本地化 JSON 的**某一个键**，不做全量重写（保住原有顺序与格式）。

为什么需要它：
  `json.dump` 会把整个文件重排/重缩进 ⇒ diff 里全是噪声，"这次到底改了哪一条"看不出来。
  本工具只替换目标键那一行的值，且**写回前先用 json.load 验证**结果仍是合法 JSON。

用法：
  python tools/patch-loc-json.py --file <json> --key <键> --value <新值>
  python tools/patch-loc-json.py --file <json> --key <键> --show      # 只看当前值

退出码：0 成功；非 0 表示键不存在 / JSON 非法 / 值未变化（避免"以为改了其实没改"）。
"""
import argparse
import json
import re
import sys
from pathlib import Path

# 本工程全部本地化文件都是 UTF-8 **无 BOM**（有 BOM 会让 Godot/引擎读键失败）
ENC = "utf-8"

# 反斜杠的码位。用它拼正则，避免源码里出现连续反斜杠被误判（lint 会把 `\]` 当路径分隔符）
BS = chr(92)
# 一条 JSON 字符串字面量：引号起、引号止，中间允许任意转义或非引号字符
JSON_STRING = '"(?:' + BS + BS + '.|[^"' + BS + BS + '])*"'


def load(path: Path) -> dict:
    raw = path.read_bytes()
    if raw[:3] == b"\xef\xbb\xbf":
        sys.exit(f"[FAIL] {path} 有 UTF-8 BOM，先去掉再改")
    return json.loads(raw.decode(ENC))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--file", required=True)
    ap.add_argument("--key", required=True)
    ap.add_argument("--value")
    ap.add_argument("--show", action="store_true")
    args = ap.parse_args()

    path = Path(args.file)
    if not path.is_file():
        sys.exit(f"[FAIL] 文件不存在：{path}")

    data = load(path)
    if args.key not in data:
        sys.exit(f"[FAIL] 键不存在：{args.key}（现有 {len(data)} 键）")

    if args.show or args.value is None:
        print(f"{args.key} = {data[args.key]!r}")
        return 0

    if data[args.key] == args.value:
        sys.exit(f"[FAIL] 值未变化：{args.key}")

    text = path.read_text(encoding=ENC)
    # 只匹配 "键": "值"，值内可能含转义引号与换行转义
    pattern = re.compile(
        r'("' + re.escape(args.key) + r'"\s*:\s*)(' + JSON_STRING + r')'
    )
    new_json_value = json.dumps(args.value, ensure_ascii=False)
    text_new, n = pattern.subn(lambda m: m.group(1) + new_json_value, text, count=1)
    if n != 1:
        sys.exit(f"[FAIL] 行内替换命中 {n} 次（期望 1）：{args.key}")

    # ★ 边界显式：写回前先证明结果仍是合法 JSON，且目标键确实变了、其它键**一个都没动**
    parsed = json.loads(text_new)
    if parsed[args.key] != args.value:
        sys.exit("[FAIL] 替换后目标键的值不等于预期")
    before, after = dict(data), dict(parsed)
    del before[args.key], after[args.key]
    if before != after:
        diff = [k for k in before if before.get(k) != after.get(k)]
        sys.exit(f"[FAIL] 替换波及了其它键：{diff}")
    if sorted(parsed) != sorted(data):
        sys.exit("[FAIL] 键集合发生了变化")

    path.write_text(text_new, encoding=ENC, newline="")
    print(f"[OK] {path.name} :: {args.key}")
    print(f"     旧：{data[args.key]}")
    print(f"     新：{args.value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
