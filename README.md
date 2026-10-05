# 银龙奥卡 · Slay the Spire 2 角色 mod

> **V0.1** ｜ 把「银龙奥卡」加入《杀戮尖塔2》作为**独立可选角色**。
> 一位用剑与血换力量的银龙少女：**卖血、焚烧、吸血、把临时生命上限铸成真实上限**。

---

## 一、前置依赖（**必装**）

| 前置 | 版本 | 说明 |
|---|---|---|
| **BaseLib** | ≥ `v3.4.7` | **必需**。Steam 创意工坊订阅即可。缺失或版本过低时本模组不会加载。 |

**除 BaseLib 外不需要任何其它模组** —— 本模组自带角色立绘、26 张卡图、两套皮肤动画、
能力图标、音效与全部本地化文本。

其他要求：**游戏版本 ≥ 0.111.0**（见 `OrcaCharacter.json` 的 `min_game_version`）。

---

## 二、安装

1. 在创意工坊**订阅 BaseLib**。
2. 到本仓库的 [Releases](../../releases) 下载 **`银龙奥卡-v0.1-运行包.zip`**。
3. 解压到游戏的 `mods\` 目录下，最终结构应形如：

   ```
   <游戏目录>\mods\奥卡角色-Orca\
   ├── OrcaCharacter.dll     ← 角色逻辑
   ├── OrcaCharacter.pck     ← 资源包（卡图 / 动画 / 音效 / 本地化）
   └── OrcaCharacter.json    ← 模组清单
   ```

   > 文件夹名可以不同，但三个文件必须在**同一个** `mods\` 的直接子目录里。

4. 启动游戏 → 角色选择界面出现「**银龙奥卡**」。

**卸载**：删掉上面那个目录即可。

---

## 三、这个角色有什么

| 项 | 内容 |
|---|---|
| 生命上限 | **60** |
| 起始遗物 | **银龙血统** |
| 起始卡组 | 打击 · 防御 · 龙族魔典 · 嗜血龙剑 |
| 内容量 | **26 张卡牌** · **14 个能力** · **14 个关键词** · **7 个纪元** |
| 皮肤 | **板甲** · **婚纱**（两套战斗 / 选人 / 商店 / 火堆动画） |

### 核心机制

- **【银龙血统】** 每受到一次伤害，本场战斗内**临时获得**相当于该伤害 50% 的**生命上限**（不回复生命）。
  每回合开始回复最大生命值 2%；若上回合**未被敌人攻击**则再回复一次（合计 4%）。自己卖血失去生命**不影响**这条翻倍。
- **【焚烧】** 敌方回合结束时按层数炸开，波及**带焚烧的敌人**；
  打出【生死一线】（熔渊枯骨）后改为**波及场上所有人（含奥卡自己）**，且每次只消耗一半层数。
- **【吸血】** 打出攻击牌造成未被格挡的伤害时回血：龙剑类型（嗜血龙剑 / 嗜血魔剑）按伤害的 50%×层数一口气结清；
  其它攻击牌按伤害的 25% 折算、不消耗层数。
- **【狂躁】** 龙剑 / 魔剑被狂躁打出时，对**所有敌人**造成伤害。
- **【翱翔】** 奥卡受到的单次伤害减半，按层数消耗。
- **临时生命上限**是这套角色的账本核心：血统给的临时上限在战斗结束时结算，
  【栖途】可以把其中一部分**转化为真实（永久）生命上限**。

> 完整卡牌 / 能力文案见游戏内，或 `OrcaCharacter/localization/zhs/`。

---

## 四、V0.1 更新概要

首个公开版本，包含此前全部修复。本轮（2026-10-05）落地的重点：

- **修复**「临时生命上限」账本族缺陷三处：栖途抬基准、龙剑数值去重、
  以及**跨局泄漏**（上一局敲过栖途，下一局开局就按敲后比例结算 ✗ → 已改为按本局卡牌状态现推）。
- **修复**归墟 buff 浮窗硬写 50%（覆盖 `PowerModel.Description` 注入实际比例；卡面与浮窗同源）。
- **修复**焚烧烤开在【生死一线】下的范围：**对没有焚烧标记的敌人也会造成伤害**（并按此订正日志，便于排查）。
- **修复**击杀奖励被战斗收尾覆盖、欧洛巴斯之触事件软锁、飞火披肩比例、面板夹屏等一批实机问题。
- **新增**「**特殊死亡台词**」：与敌人被同一次焚烧一起烧死时，游戏结束画面显示专属文案
  （默认 `银龙奥卡与{enemies}一同被火焰烧成了灰烬`，可在模组设置里改成你自己的、多条随机抽）。

---

## 五、从源码构建（开发者）

```powershell
dotnet build      # 编译 → 自动把 dll / json / pdb 复制进 mods 目录
dotnet publish    # 上面 + 重建 .pck（资源包）
```

`OrcaCharacter.csproj` 里的 `CopyToModsFolderOnBuild` / `GodotPublish` 两个 Target
保证**游戏目录里的构件一定是源码编出来的那一份**，不存在手工拷贝导致的漂移。

### 依赖与路径

依赖 dll **不在本仓库**（版权原因）：`sts2.dll` / `0Harmony.dll` 取自游戏安装目录，
`BaseLib.dll` 取自 Steam 创意工坊。位置由 `Sts2PathDiscovery.props` **自动发现**：

1. 注册表 `HKLM\...\Uninstall\Steam App 2868840` 的 `InstallLocation`
2. `HKCU\Software\Valve\Steam@SteamPath` 下的 `steamapps`
3. 都失败时用 `/p:Sts2Path=...`、环境变量或 `local.props` 指定

**任何文件里都不应出现本机绝对路径。** 需要覆盖时复制 `local.props.example` 为 `local.props`。

### 打包与自检

`.pck` 由 `tools/build-pck.py` 自建（不依赖 Godot 导出，避免官方导出静默丢资源），
`dotnet publish` 会自动跑它并做**双门槛校验**（体积 ≥ 20 MB、条目数 ≥ 300）。
另有 `tools/check-placeholders.py` 检查卡面占位符是否有注入点。

---

## 六、工程约定

### ★ 同源重建（硬规矩）

**`dll` 与 `pck` 必须来自同一棵源码树的同一次重建。** 两者是同一工程编出的两个独立产物
（`pck` 装资源、`dll` 装代码），混用不会当场报错，但会让**任何行为差异都无法归因**。

```powershell
dotnet publish      # 一次把 dll 与 pck 都重建到 mods 目录，不要手工替换其中一个
```

历史上本工程踩过这个坑：同一份 `cards.json` 同时存在三个版本，
导致「改了 X 所以 Y 变了」这类推理全部建立在乱账上。

### 其他纪律

**不写死路径 / 常量单一来源 / 先读再改 / 一次只改一个变量 / 结论由实机验证后下。**

### Godot 版本约束

游戏内置 MegaDot **4.5.1**；用更新版本 Godot 导出的 `.pck` 游戏**不加载**，
故 `OrcaCharacter.csproj` 固定 `Sdk="Godot.NET.Sdk/4.5.1"`。
`.godot/imported/`、`.godot/exported/` **必须入库**（`.pck` 里含这些导入产物，
不提交就无法在没装 Godot 的机器上复现打包），`.gitignore` 因此只忽略 `.godot/mono/`。

---

## 七、仓库结构

```
OrcaCharacter.csproj / .sln        ← MSBuild 入口
Directory.Build.props              ← 工程常量
Sts2PathDiscovery.props            ← 游戏安装位置自动发现（零绝对路径）
local.props.example                ← 本机覆盖模板（local.props 已 gitignore）
OrcaCharacter.json                 ← 模组清单（mod id / 依赖 BaseLib / 版本）

OrcaCharacterCode/                 ← 全部 C# 源码（69 个文件）
├── Core/        角色本体、启动、配置、日志、卡牌基类、关键词、卡池
├── Cards/       卡牌定义          ├── Powers/     能力（Power）模型
├── Patches/     Harmony 补丁      ├── Epochs/     纪元
├── UI/          图标、能量珠、卡面 UI、皮肤挂点
├── Relics/      遗物              └── Audio/      音频与台词

OrcaCharacter/                     ← Godot 资源根 = res://
├── localization/zhs/*.json        ← 本地化（卡牌 / 能力 / 遗物 / 关键词 / 设置界面）
└── audio/*.tres

images/ animations/ materials/ scenes/ skins/   ← 本模组自己的美术资源（打 .pck 用）
.godot/imported/ .godot/exported/               ← Godot 导入产物（必须入库，见上）
tools/                             ← 打包与自检脚本（Python）
docs/                              ← 工程文档与踩坑记录
```

> `images/`、`animations/` 等目录是**本模组打包 `.pck` 所需的资源**，请只增删本模组自己的资源。

---

## 八、反馈与已知情况

- **反馈**：提 [Issue](../../issues) 或到创意工坊页面留言。
- **日志**：出问题时请附游戏目录下的 `godot.log`（模组日志带 `[Orca]` 前缀，
  设置界面可打开「输出详细日志」）。
- **已知情况**：
  - 特殊死亡台词、归墟 buff 浮窗比例等属于 V0.1 新改动，欢迎实机反馈。
  - 帧率敏感场景（大量敌人 + 多层焚烧）仍在观察。

---

## 九、许可与致谢

- **代码**：本仓库暂未附开源许可，转载 / 二次分发请先联系作者。
- **美术资源**：版权归原作者，随本模组一起分发仅用于游戏内运行。
- **致谢**：[BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)（前置依赖）；
  工程骨架参照 `Coll-ed/StS2-NotEnoughDifficulty`（MIT），副本见 `docs/参考-好工程-StS2-NotEnoughDifficulty/`。
