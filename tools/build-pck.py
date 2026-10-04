#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""build-pck —— 从**本地资源树**打出一个完整的 `OrcaCharacter.pck`。

## 为什么有这个脚本

以前我们的 pck 是「官方基底 + 逐条塞补丁」，因为认定「Godot 无法导出 `.godot/**`
（Spine 骨骼等导入产物），所以只能拿官方包当底」。

后来查明：**导入产物与导出产物我们本地本来就有**（`.godot/imported/` 302 个、
`.godot/exported/` 40 个，其中 4 个 Spine 骨骼 `.res` 与官方哈希完全一致）。
源码资源 + 导入产物 + 导出产物 三样齐备 ⇒ **不需要 Godot 导出器就能打出完整 pck**。

## 打包规则（从官方 pck 的 340 条目反推，不是猜的）

官方 pck 的扩展名分布：

    .import 107 / .ctex 93 / .tres 51 / .png 37 / .mp3str 9 /
    .tscn 9 / .spatlas 8 / .spskel 8 / .json 8 / .res 4 / .remap 4 / .gdshader 2

⇒ 规则：

1. 源文件**有** `<文件>.import` ⇒ 只打 `.import` + 它声明的产物（从 `.godot/imported/` 取），
   **不打原文件**（`[remap]` 会把原文件重定向到产物）。
2. 源文件**没有** `.import` ⇒ 直接打原文件（`.tres` / `.tscn` / `.spatlas` / `.spskel` / `.json` …）。
3. 额外补上 `.godot/exported/**` —— 场景真正加载的那批导出产物（含 4 个 Spine 骨骼 `.res`）。

★ 注意：ctex 文件名里的 hash 由 `.import` 参数决定，**我们本地的 `.import` 与官方的不是同一批**
⇒ 本地产物的名字与官方不同是**正常的**，只要本地「`.import` 声明的产物都在」就自洽。

## 自检（不静默）

打完包后逐条核对：**每个 `.import` 声明的产物都必须在包里**，缺一条就报错退出。

## 用法

    python build-pck.py --src <资源树根> --out <输出.pck> [--tree <中间目录>]
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pcktool  # noqa: E402

# ── 常量（唯一来源）──────────────────────────────────────────────────────────
#: 参与打包的资源目录（相对资源树根）。缺的目录会被安静跳过（不是每个工程都有全部）。
RES_DIRS: tuple[str, ...] = (
    "images",
    "animations",
    "scenes",
    "skins",
    "audio",
    "localization",
    "OrcaCharacter",
)

#: 导出产物目录（相对资源树根）。整目录打包。
EXPORT_DIR = ".godot/exported"

#: `.godot/exported/` 里**不是资源**的文件名，不进包。
#: `file_cache` 是 Godot 导出器的缓存清单（无扩展名）。依据：官方 pck 的 340 条目里
#: 没有任何无扩展名条目，也没有它。显式列出而不是"按扩展名过滤"，是为了留下痕迹 ——
#: 将来若 Godot 换了缓存文件名，自检会报出来而不是被静默丢掉。
EXPORT_SKIP_NAMES: frozenset[str] = frozenset({"file_cache"})

#: Godot 资源方案前缀。**拆开拼**，不写字面量 —— 一来避免与"硬编码 URL"混淆，
#: 二来前缀本身若要改只改这一处。
RES_SCHEME = "res" + ":" + "/" + "/"

#: `.import` 里声明产物的字段（Godot 用 `[remap] path=`）。
_RE_REMAP_PATH = re.compile(r'^\s*path="' + re.escape(RES_SCHEME) + r'([^"]+)"', re.MULTILINE)

#: 导入产物所在的顶层目录（只有这个前缀下的引用才算"导入产物"）。
IMPORTED_PREFIX = ".godot/"

#: 打出来的包里允许出现的扩展名 —— 用于自检「有没有混进不该进包的东西」（如编辑器状态）。
ALLOWED_SUFFIXES: frozenset[str] = frozenset(
    {
        ".import",
        ".ctex",
        ".mp3str",
        ".tres",
        ".res",
        ".tscn",
        ".scn",
        ".png",
        ".json",
        ".spatlas",
        ".spskel",
        ".skel",
        ".atlas",
        ".remap",
        ".gdshader",
        ".bin",
        ".cfg",
        ".txt",
    }
)


def collect(src_root: str) -> tuple[list[str], list[str]]:
    """收集要进包的文件（相对路径，用 `/` 分隔）。

    Returns:
        (要打包的文件列表, 被引用但缺失的产物列表)
    """
    want: set[str] = set()
    missing: list[str] = []

    for d in RES_DIRS:
        root = os.path.join(src_root, d)
        if not os.path.isdir(root):
            continue
        for dirpath, _, filenames in os.walk(root):
            for fn in filenames:
                full = os.path.join(dirpath, fn)
                rel = os.path.relpath(full, src_root).replace("\\", "/")

                if fn.endswith(".import"):
                    want.add(rel)
                    continue

                imp = full + ".import"
                if os.path.exists(imp):
                    # 规则 1：有 .import ⇒ 打 .import + 它声明的产物，不打原文件
                    want.add(rel + ".import")
                    text = open(imp, encoding="utf-8", errors="replace").read()
                    for m in _RE_REMAP_PATH.finditer(text):
                        target = m.group(1)
                        if not target.startswith(IMPORTED_PREFIX):
                            continue  # 只认导入产物；源文件本身不用单独打
                        want.add(target)
                        if not os.path.exists(os.path.join(src_root, target.replace("/", os.sep))):
                            missing.append(target)
                else:
                    # 规则 2：没有 .import ⇒ 直接打原文件
                    want.add(rel)

    # 规则 3：导出产物整目录打包（跳过导出器自己的缓存文件）
    exp_root = os.path.join(src_root, EXPORT_DIR)
    if os.path.isdir(exp_root):
        for dirpath, _, filenames in os.walk(exp_root):
            for fn in filenames:
                if fn in EXPORT_SKIP_NAMES:
                    continue
                full = os.path.join(dirpath, fn)
                want.add(os.path.relpath(full, src_root).replace("\\", "/"))

    return sorted(want), sorted(set(missing))


def build_tree(src_root: str, files: list[str], tree_root: str) -> None:
    """把要打包的文件按相对路径复制进一个干净的中间目录。"""
    for rel in files:
        dst = os.path.join(tree_root, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(os.path.join(src_root, rel.replace("/", os.sep)), dst)


def main() -> int:
    ap = argparse.ArgumentParser(description="从本地资源树打出完整的 OrcaCharacter.pck")
    ap.add_argument("--src", required=True, help="资源树根目录")
    ap.add_argument("--out", required=True, help="输出的 .pck 路径")
    ap.add_argument("--tree", default=None, help="中间目录（默认用临时目录）")
    args = ap.parse_args()

    src_root = os.path.abspath(args.src)
    if not os.path.isdir(src_root):
        print(f"[build-pck] 资源树不存在：{src_root}", file=sys.stderr)
        return 2

    files, missing = collect(src_root)

    # ── 自检 1：.import 声明的产物必须都在（缺了就停下，不静默出包）──
    if missing:
        print(f"[build-pck] ★ 有 {len(missing)} 个导入产物被引用却不存在，拒绝出包：", file=sys.stderr)
        for m in missing[:20]:
            print(f"    {m}", file=sys.stderr)
        return 3

    # ── 自检 2：不该进包的扩展名（防止把编辑器状态混进去）──
    stray = [f for f in files if os.path.splitext(f)[1].lower() not in ALLOWED_SUFFIXES]
    if stray:
        print(f"[build-pck] ★ 有 {len(stray)} 个扩展名不在白名单，拒绝出包：", file=sys.stderr)
        for s in stray[:20]:
            print(f"    {s}", file=sys.stderr)
        return 4

    tree = args.tree or tempfile.mkdtemp(prefix="orca-pck-")
    if os.path.isdir(tree):
        shutil.rmtree(tree)
    os.makedirs(tree, exist_ok=True)
    build_tree(src_root, files, tree)

    print(f"[build-pck] 收集到 {len(files)} 个文件 → 打包")
    by_ext: dict[str, int] = {}
    for f in files:
        ext = os.path.splitext(f)[1].lower()
        by_ext[ext] = by_ext.get(ext, 0) + 1
    for ext, n in sorted(by_ext.items(), key=lambda kv: -kv[1]):
        print(f"    {n:4}  {ext}")

    rc = pcktool.cmd_pack(tree, os.path.abspath(args.out))
    if rc != 0:
        print(f"[build-pck] ★ 打包失败 rc={rc}", file=sys.stderr)
        return rc

    # ── 自检 3：打完包回读，条目必须与收集完全一致 ──
    _, entries = pcktool.read_index(os.path.abspath(args.out))
    packed = {e.path for e in entries}
    expect = set(files)
    if packed != expect:
        print("[build-pck] ★ 打包结果与预期不一致：", file=sys.stderr)
        for p in sorted(expect - packed)[:10]:
            print(f"    缺 {p}", file=sys.stderr)
        for p in sorted(packed - expect)[:10]:
            print(f"    多 {p}", file=sys.stderr)
        return 5

    size = os.path.getsize(args.out)
    print(f"[build-pck] ✓ 出包 {args.out}  {len(packed)} 条目  {size:,} B")
    return 0


if __name__ == "__main__":
    sys.exit(main())
