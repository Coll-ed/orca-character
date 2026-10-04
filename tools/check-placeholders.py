#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""★ 占位符自检：卡面文案里**带格式化器**的占位符，是否都由**该卡类自己**注入了 DynamicVar。

为什么要单独一个工具（本项目已因此踩过四次，见审计 §2.5 与 `Cards-Sword.cs:61-67`）：
  SmartFormat 只要**缺一个键**就报 `No suitable Formatter` ⇒ **整条卡面**回退成未替换的原文，
  不存在"只坏那一处"的中间状态。而漏报的典型原因就是**全工程搜字符串**：
  `Percent` 在栖途里声明过，于是血焰剑鞘被误判为 OK。
  ⇒ 本工具的判据是**按卡类自己的声明**：只看"该卡 → 自己的文件"里出现的注入点。

判定规则（与引擎行为一一对应）：
  ① 文案里的 `{Key:...}`（**带格式化器**）⇒ 必须是 DynamicVar 注入的：
     - `new DynamicVar("Key", …)`（显式键名）
     - `new DamageVar(…)` / `new BlockVar(…)`（引擎规定键名分别是 Damage / Block）
     - `CanonicalVars` 里注入的（会被 `DynamicVars.AddTo(description)` 一起注入）
  ② 文案里的 `{Key}`（**无格式化器**，裸值）⇒ 走 `description.Add("Key", 值)` 这类裸值注入，
     两类**不可互串**（裸值配 `:diff()` 同样会 No suitable Formatter）。

用法：
  python tools/check-placeholders.py --repo .                  # 检查，退出码 0/1
  python tools/check-placeholders.py --repo . --verbose         # 打印每张卡的键集合

退出码：0 = 全过；1 = 有缺键（**不许静默通过**）。
"""
import argparse
import json
import re
import sys
from pathlib import Path

ENC = "utf-8"

# 卡 id → 该卡"自己的文件"（模板类 id 由类名去掉 ORCA_ 前缀小写得到）。
# 判据必须限制在这些文件里，**不能全工程搜**（见头注释）。
CARD_FILES = {
    "ORCA_STRIKE": ["Cards/OrcaStrike.cs"],
    "ORCA_DEFEND": ["Cards/OrcaDefend.cs"],
    "ORCA_BLOOD_FORGE": ["Cards/Cards-Dragon.cs"],
    "ORCA_NIRVANA_GRASP": ["Cards/Cards-Dragon.cs"],
    "ORCA_TRAMPLE": ["Cards/Cards-Dragon.cs"],
    "ORCA_BLOOD_SCABBARD": ["Cards/Cards-Sword.cs"],
    "ORCA_EMBER_CHASE": ["Cards/Cards-Codex.cs"],
    "ORCA_CODEX_EMBER": ["Cards/Cards-Codex.cs"],
    "ORCA_REVERSE_SCALE": ["Powers/OrcaReverseScale.cs"],
    "ORCA_EMBER_WING": ["Cards/OrcaEmberWing.cs"],
    "ORCA_ROLLING_FLAME": ["Cards/Cards-Pack2.cs"],
    "ORCA_RUIN_BURN": ["Cards/Cards-Pack2.cs"],
    "ORCA_FIRE_CLOAK": ["Cards/Cards-Pack2.cs"],
    "ORCA_OVERLOOK": ["Cards/Cards-Pack2-B.cs"],
    "ORCA_VOID_RETURN": ["Cards/Cards-Pack2-B.cs"],
    "ORCA_HOMESTEAD": ["Powers/OrcaHomestead.cs"],
    "ORCA_DRAGON_DIGNITY": ["Powers/OrcaDragonDignity.cs"],
    "ORCA_BLOOD_SWORD": ["Cards/OrcaBloodSword.cs"],
    "ORCA_BLOOD_BLADE": ["Cards/Cards-BloodBlade.cs"],
    "ORCA_BLOOD_NIRVANA": ["Powers/OrcaBloodNirvana.cs"],
}

# 引擎规定的键名：这些 Var 类型注入的键名是固定的，不由我们起名
IMPLICIT_KEY_VARS = {"DamageVar": "Damage", "BlockVar": "Block"}

# 引擎自己注入、不需要我们声明的内建键（`CardModel.GetDescriptionForPile` 追加）
ENGINE_KEYS = {"IfUpgraded", "KeywordTags"}

# 带格式化器：{Key:xxx} / {Key:xxx()}
FMT_RE = re.compile(r"\{([A-Za-z_][A-Za-z0-9_]*)\s*:")
# 裸值：{Key}
RAW_RE = re.compile(r"\{([A-Za-z_][A-Za-z0-9_]*)\s*\}")
# DynamicVar 的字面量键名（必须字面量；变量键名无法静态核对 ⇒ 由工具显式报出）
DYN_KEY_RE = re.compile(r'new\s+DynamicVar\(\s*"([A-Za-z_][A-Za-z0-9_]*)"')
# DamageVar / BlockVar
DYN_TYPED_RE = re.compile(r"new\s+(DamageVar|BlockVar)\s*\(")
# 注入点：**只认 LocString 的 Add**。接收者限定为 LocString 变量惯用名
# （`description` 是 AddExtraArgsToDescription 的参数名，`loc` 是浮窗/选牌提示里的局部名），
# 且 `Add` 后不跟泛型参数 —— 这样才能排除 `CardPileCmd.Add(...)` 这类同名方法。
# 踩过的假阳性：选牌提示的 `loc.Add("Amount", 1)`（那是 PromptString，不是卡面文案）。
ADD_CALL_RE = re.compile(r'\b(?:description|desc|loc)\.Add\s*\(')


def injected_keys(text: str) -> tuple[set[str], set[str], list[str]]:
    """返回（DynamicVar 注入的键，裸值注入的键，无法静态判定的注入点行）。

    为什么要按**方法名**分流，而不是"全文搜键名"：
      裸值走 `LocString.Add(string, string?)`，DynamicVar 走 `LocString.Add(DynamicVar)`，
      两者都落到同一本字典里 ⇒ 键"存在"不等于"类型对"。
    """
    dyn = set(DYN_KEY_RE.findall(text))
    for var in DYN_TYPED_RE.findall(text):
        dyn.add(IMPLICIT_KEY_VARS[var])

    raw: set[str] = set()
    opaque: list[str] = []
    for lineno, line in enumerate(text.splitlines(), 1):
        stripped = line.lstrip()
        if stripped.startswith("//") or stripped.startswith("*"):
            continue                                    # 注释里的示例不算注入点
        m = ADD_CALL_RE.search(line)
        if not m:
            continue
        arg = line[m.end():].lstrip()                   # m.end() 落在 'Add(' 的 '(' 之后
        if arg.startswith("new "):
            continue                                    # `new DynamicVar("X"` 已被上面收走
        if arg.startswith('"'):
            lit = re.match(r'"([A-Za-z_][A-Za-z0-9_]*)"', arg)
            if lit:
                raw.add(lit.group(1))
            else:
                opaque.append(f"L{lineno}: {line.strip()}")
        else:
            opaque.append(f"L{lineno}: {line.strip()}")   # 键名不是字面量 ⇒ 静态判不了
    return dyn, raw, opaque


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    repo = Path(args.repo).resolve()
    cards_json = repo / "OrcaCharacter" / "localization" / "zhs" / "cards.json"
    if not cards_json.is_file():
        sys.exit(f"[FAIL] 找不到 {cards_json}")

    loc = json.loads(cards_json.read_text(encoding=ENC))
    code_root = repo / "OrcaCharacterCode"

    failures: list[str] = []
    checked = 0
    total_fmt = 0

    for card_id, rels in sorted(CARD_FILES.items()):
        desc_key = f"{card_id}.description"
        if desc_key not in loc:
            failures.append(f"{card_id}: 文案键 {desc_key} 不存在")
            continue

        texts = [(desc_key, loc[desc_key])]
        state_key = f"{card_id}.state.description"
        if state_key in loc:
            texts.append((state_key, loc[state_key]))

        dyn: set[str] = set()
        raw: set[str] = set()
        for rel in rels:
            path = code_root / rel
            if not path.is_file():
                failures.append(f"{card_id}: 声明的文件不存在 {rel}")
                continue
            d, r, opaque = injected_keys(path.read_text(encoding=ENC))
            dyn |= d
            raw |= r
            for line in opaque:
                failures.append(
                    f"{card_id}: {rel} 有无法静态判定的注入点，请改为字面量键名 → {line}"
                )

        for key, text in texts:
            fmt = set(FMT_RE.findall(text)) - ENGINE_KEYS
            bare = set(RAW_RE.findall(text)) - ENGINE_KEYS
            checked += 1
            total_fmt += len(fmt)

            # 带格式化器的占位符：键必须在**本卡类自己**声明的集合里
            # （DynamicVar 与裸值都进同一本字典，引擎按"键在不在"决定能不能格式化）
            missing_fmt = sorted(fmt - (dyn | raw))
            missing_raw = sorted(bare - (dyn | raw))
            if missing_fmt:
                failures.append(
                    f"{key}: 带格式化器的占位符缺注入 → {missing_fmt}"
                    f"（本卡类已注入 dyn={sorted(dyn)} raw={sorted(raw)}）"
                )
            if missing_raw:
                failures.append(
                    f"{key}: 裸值占位符缺注入 → {missing_raw}"
                    f"（本卡类已注入 dyn={sorted(dyn)} raw={sorted(raw)}）"
                )
            if args.verbose:
                print(f"  {key}: fmt={sorted(fmt)} bare={sorted(bare)} "
                      f"| dyn={sorted(dyn)} raw={sorted(raw)}")

    print(f"[check-placeholders] 文案条目 {checked} 条 · 带格式化器占位符 {total_fmt} 个")
    if failures:
        print(f"[FAIL] {len(failures)} 条：")
        for f in failures:
            print("  - " + f)
        return 1
    print("[OK] 零缺键 —— 每张卡用到的键都由它自己的文件注入")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
