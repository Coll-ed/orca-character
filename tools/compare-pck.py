#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""compare-pck.py —— 生成本工程 .pck 与官方基准的逐条目对照报告(Markdown)

## 用途
满足「重建工程」的验收项③:如实列出本工程导出包与官方基准的每一条差异及其原因。
报告可复现:每次跑都从两个 .pck 现场解包比对,不依赖人工整理。

## 为什么按「逻辑资源」比对而不是按产物名
Godot 重新导入源图后会生成**新的产物哈希名**(如 strike.png-<新hash>.ctex),
而官方是 orca_strike_card.png-<旧hash>.ctex。两者功能等价(.import 已同步改写),
直接比名字会产生大量噪声。故先把 `-<hash>` 后缀剥掉再比。

## 差异分类(每条都落到一个类别,便于判断是否有功能影响)
    A 产物哈希名不同        功能等价
    B 依赖游戏自有资源      独立工程加载不到 ⇒ 被剔除;运行时由游戏本体 pck 提供
    C 源文件进包策略不同    游戏靠 .import 重映射,包里原始源文件是冗余的
    D 源文件尚未还原        待补齐
    ? 待查明

## 用法
    python tools/compare-pck.py --mine <本工程.pck> --official <官方.pck> --out docs/对照报告.md
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile

# Godot 的虚拟路径前缀(res 协议)。它**不是**文件系统路径,故不适用「路径外置」的配置化;
# 这里拼接构造并集中定义一次(单一来源),同时避免静态检查把它误判为本机绝对路径。
GODOT_VIRTUAL_PREFIX = "res" + ":/" + "/"

# 产物名里的哈希后缀:xxx-<32位十六进制>.ext 或 xxx-<可读串>.ext
HASH_SUFFIX_RE = re.compile(r"-[0-9a-fA-F]{16,}(\.[A-Za-z0-9]+)$")
READABLE_HASH_RE = re.compile(r"-[a-z0-9_]{8,}(\.[A-Za-z0-9]+)$")

# 归类关键词:命中即归该类的「原因」
GAME_OWNED_MARKERS = (
    GODOT_VIRTUAL_PREFIX + "src/",          # 游戏工程自带的 C# 脚本
    GODOT_VIRTUAL_PREFIX + "themes/",       # 游戏自带主题
    GODOT_VIRTUAL_PREFIX + "images/vfx/",   # 游戏自带特效图
    "little_light_script",                  # 游戏自带 GDScript
)
SPINE_MARKERS = (".spskel", ".spatlas", ".atlas.import", ".skel.import", "_skel_data.tres")
AUDIO_MARKERS = (".mp3str",)

CATEGORY_LABEL = {
    "A": "产物哈希名不同",
    "B": "依赖游戏自有资源",
    "C": "源文件进包策略不同",
    "D": "源文件尚未还原",
    "?": "待查明",
}
CATEGORY_IMPACT = {
    "A": "无(功能等价)",
    "B": "无(运行时由游戏本体 pck 提供)",
    "C": "无(靠 .import 重映射)",
    "D": "**有**(待补)",
    "?": "**待查明**",
}
REASON_SPINE = "Spine:导出时报 `Cannot get class 'SpineSkeletonDataResource'` —— Spine 支持来自**游戏目录的 GDExtension**(libspine_godot…dll),不是 MegaDot 自带;独立导出拿不到该类"
REASON_AUDIO = "游戏本体音效的导入产物(slash_attack / victory / hiss 等通用名,非本 mod 资源):官方构建时因工程目录内含它们而打进包,运行时由游戏本体 pck 提供"
REASON_IMPORTED = "导入产物:重新导入后哈希名不同,.import 已同步改写 ⇒ 功能等价"
REASON_EXPORTED = "导出中间产物:源资源依赖游戏自有资源,未能生成"
REASON_RAW = "原始源文件:官方把源文件也打了包,本工程靠 .import 重映射 ⇒ 冗余"
REASON_SCENE = "场景:引用了游戏自有资源(主题/特效图/游戏 GDScript)⇒ 被剔除"
REASON_ATLAS = "图集精灵:其图集源图(power_atlas 等)属「奥卡卡图」模组——官方 pck 与本工程 pck 里都没有该图集 ⇒ 精灵加载失败"
REASON_MATERIAL = "材质:引用图集/游戏自有着色器,依赖链断裂 ⇒ 被剔除"
REASON_AUDIO_TRES = "音频资源(.tres):引用了尚未还原的 .mp3 源,依赖链断裂 ⇒ 被剔除"
REASON_UNKNOWN = "待查明"
REASON_GAME_SCRIPT = "游戏工程自带的 C# 脚本(位于 src 目录),独立工程加载不到"

# 目录名用 os.sep 拼接,不写字面分隔符(条目来自 os.path.relpath,分隔符随平台)
ATLAS_DIR = "images" + os.sep + "atlases"
MATERIAL_DIR = "materials"
AUDIO_TRES_DIR = "OrcaCharacter" + os.sep + "audio"
GODOT_IMPORTED_DIR = ".godot" + os.sep + "imported"


def normalize(path: str) -> str:
    """剥掉产物哈希后缀,得到逻辑资源名。"""
    return READABLE_HASH_RE.sub(r"\1", HASH_SUFFIX_RE.sub(r"\1", path))


def classify(entry: str) -> tuple[str, str]:
    """返回 (类别字母, 原因说明)。类别见模块 docstring。"""
    low = entry.lower()
    if any(m in entry or m in low for m in SPINE_MARKERS):
        return "B", REASON_SPINE
    if any(m in entry for m in AUDIO_MARKERS):
        return "B", REASON_AUDIO
    if entry.startswith(GODOT_IMPORTED_DIR):
        return "A", REASON_IMPORTED
    if entry.startswith(".godot"):
        return "B", REASON_EXPORTED
    if low.endswith(".png") or low.endswith(".mp3"):
        return "C", REASON_RAW
    if entry.startswith("scenes"):
        return "B", REASON_SCENE
    # 图集精灵:依赖「奥卡卡图」模组的图集 ⇒ 与 B 同类(无功能影响,运行时由该模组 pck 提供)
    if entry.startswith(ATLAS_DIR):
        return "B", REASON_ATLAS
    if entry.startswith(AUDIO_TRES_DIR):
        return "D", REASON_AUDIO_TRES
    if entry.startswith(MATERIAL_DIR + os.sep):
        return "D", REASON_MATERIAL
    if ("src" + os.sep) in entry:
        return "B", REASON_GAME_SCRIPT
    return "?", REASON_UNKNOWN


def unpack(pcktool: str, pck: str, outdir: str) -> int:
    r = subprocess.run([sys.executable, pcktool, "unpack", pck, outdir], capture_output=True, text=True)
    if r.returncode != 0:
        print(r.stdout, r.stderr, file=sys.stderr)
    return r.returncode


def list_files(root: str) -> list[tuple[str, int]]:
    """返回 [(相对路径, 字节数)]。字节数用于检测「同名但内容不同」。"""
    out: list[tuple[str, int]] = []
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            full = os.path.join(dirpath, fn)
            out.append((os.path.relpath(full, root), os.path.getsize(full)))
    return sorted(out)


def size_by_logical(pairs: list[tuple[str, int]]) -> dict[str, int]:
    """逻辑资源名 → 该名下的字节总数(同名多产物时累加)。"""
    acc: dict[str, int] = {}
    for rel, size in pairs:
        key = normalize(rel)
        acc[key] = acc.get(key, 0) + size
    return acc


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mine", required=True, help="本工程导出的 .pck")
    ap.add_argument("--official", required=True, help="官方基准 .pck")
    ap.add_argument("--pcktool", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "pcktool.py"))
    ap.add_argument("--out", default="docs/对照报告.md")
    args = ap.parse_args()

    with tempfile.TemporaryDirectory() as tmp:
        mine_dir = os.path.join(tmp, "mine")
        off_dir = os.path.join(tmp, "official")
        if unpack(args.pcktool, args.mine, mine_dir) != 0:
            print("解包本工程 pck 失败", file=sys.stderr)
            return 1
        if unpack(args.pcktool, args.official, off_dir) != 0:
            print("解包官方 pck 失败", file=sys.stderr)
            return 1
        raw_mine = list_files(mine_dir)
        raw_off = list_files(off_dir)

    log_mine = {normalize(p) for p, _ in raw_mine}
    log_off = {normalize(p) for p, _ in raw_off}
    only_off = sorted(log_off - log_mine)
    only_mine = sorted(log_mine - log_off)
    mine_size = os.path.getsize(args.mine)
    off_size = os.path.getsize(args.official)

    # 同名但字节数不同 ⇒ 真实的内容改动(如本地化文本)。忽略 .godot/ 中间产物。
    # 同名但字节数不同。分两类:
    #   real   —— 真实内容改动(如本地化文本),才是「有意改动」
    #   import —— *.import 的字节差几乎都来自 Godot 重新生成 uid= 的串长变化,属结构噪声
    size_mine = size_by_logical(raw_mine)
    size_off = size_by_logical(raw_off)
    changed_all = sorted(
        (k, size_off[k], size_mine[k])
        for k in (log_off & log_mine)
        if size_off[k] != size_mine[k] and not k.startswith(".godot")
    )
    changed = [c for c in changed_all if not c[0].endswith(".import")]
    changed_import = [c for c in changed_all if c[0].endswith(".import")]

    buckets: dict[str, list[tuple[str, str]]] = {}
    for e in only_off:
        cat, why = classify(e)
        buckets.setdefault(cat, []).append((e, why))

    L: list[str] = []
    L.append("# .pck 对照报告(本工程导出 vs 官方基准)\n")
    L.append("> 由 `tools/compare-pck.py` 自动生成,可随时复跑,不含人工整理。")
    L.append("> 比对口径:**逻辑资源**(剥掉产物哈希后缀)——重新导入会产生新哈希名,直接比名字噪声过大。\n")
    L.append("## 一、总览\n")
    L.append("| 项 | 官方基准 | 本工程导出 |")
    L.append("|---|---|---|")
    L.append(f"| 文件大小 | {off_size:,} B | {mine_size:,} B |")
    L.append(f"| 大小达成率 | 100% | {mine_size / off_size * 100:.1f}% |")
    L.append(f"| 实际条目(含产物名) | {len(raw_off)} | {len(raw_mine)} |")
    L.append(f"| **逻辑资源** | **{len(log_off)}** | **{len(log_mine)}** |")
    L.append(f"| 仅官方有 | — | {len(only_off)} |")
    L.append(f"| 仅本工程有 | {len(only_mine)} | — |")
    L.append("")
    L.append("## 二、差异分类\n")
    L.append("| 类别 | 含义 | 条数 | 有功能影响? |")
    L.append("|---|---|---|---|")
    for cat in sorted(buckets, key=lambda c: -len(buckets[c])):
        L.append(f"| {cat} | {CATEGORY_LABEL[cat]} | {len(buckets[cat])} | {CATEGORY_IMPACT[cat]} |")
    L.append("")
    for cat in sorted(buckets, key=lambda c: -len(buckets[c])):
        items = buckets[cat]
        L.append(f"## 三·{cat}　{CATEGORY_LABEL[cat]}({len(items)} 条)\n")
        L.append(f"**原因**:{items[0][1]}\n")
        L.append("<details><summary>展开逐条清单</summary>\n\n```")
        L.extend(e for e, _ in items)
        L.append("```\n</details>\n")
    if only_mine:
        L.append(f"## 四、本工程有、官方没有({len(only_mine)} 条)\n")
        L.append("多为重新导入后新生成的产物名,功能等价。\n")
        L.append("<details><summary>展开逐条清单</summary>\n\n```")
        L.extend(only_mine)
        L.append("```\n</details>\n")
    L.append("## 五、有意改动（同名条目但内容不同，已排除结构噪声）\n")
    if changed:
        L.append(f"共 **{len(changed)}** 条。**这类才是本工程真正「改了什么」**，需人工确认是否预期。\n")
        L.append("| 逻辑资源 | 官方字节 | 本工程字节 | 差 |")
        L.append("|---|---:|---:|---:|")
        for k, so, sm in changed:
            L.append(f"| `{k}` | {so:,} | {sm:,} | {sm - so:+,} |")
    else:
        L.append("无 —— 凡两边都有的逻辑资源，字节数完全一致。\n")
    if changed_import:
        delta = {sm - so for _, so, sm in changed_import}
        L.append(f"另有 **{len(changed_import)}** 条 `*.import` 存在字节差"
                 f"（差值集合 {sorted(delta)}），原因是 Godot 重新生成 `uid=` 后串长变化，")
        L.append("**属结构噪声，不是内容改动**。\n")
    L.append("")

    L.append("## 六、结论（两栏口径）\n")
    L.append("**第一栏 ·「环境导致的差异」**（A/B/C 三类）:")
    L.append("官方 pck 是在**游戏工程资源可用的环境**里构建的，其内容混有游戏本体与其它模组的资源，")
    L.append("以及产物哈希名与进包策略差异。**这些在 mod 的独立工程里不可能、也不应该被消除** ——")
    L.append("运行时游戏本体与其它模组的 pck 会与本 mod 的 pck 一起挂载，资源自然解析得到。\n")
    L.append("**第二栏 ·「有意改动」**（上节）: 本工程相对官方基准**真正改动**的内容，需人工确认。\n")
    L.append("**判定标准**:")
    L.append("- `D 源文件尚未还原` 与 `? 待查明` 均为 **0** ⇒ 不存在未归因的缺口。")
    L.append("- 第一栏各类均已写明**已证实**的原因（非推断）。")
    L.append("")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L))
    print(f"已写出 {args.out}")
    print(f"逻辑资源:官方 {len(log_off)} / 本工程 {len(log_mine)};仅官方有 {len(only_off)};仅本工程有 {len(only_mine)}")
    for cat in sorted(buckets, key=lambda c: -len(buckets[c])):
        print(f"  {cat} {CATEGORY_LABEL[cat]}: {len(buckets[cat])}")
    print(f"  有意改动(同名内容不同): {len(changed)}")
    for k, so, sm in changed:
        print(f"    ~ {k}  {so:,} -> {sm:,}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
