# 银龙奥卡 · Slay the Spire 2 角色 mod

把「银龙奥卡」做成《杀戮尖塔2》里独立可选的角色。

---

## 一、这是什么 / 不是什么（重要边界）

「银龙奥卡」在游戏里由**三个模组**共同构成，本仓库只是其中之一：

| 模组 | 提供 | 本仓库 |
|---|---|---|
| **本仓库** `OrcaCharacter` | 角色逻辑：卡牌 / 能力 / 遗物 / 关键词 / 纪元 / 音效挂点 | ✅ 就是它 |
| `奥卡卡图`（`战士卡图mod1.1`） | 卡牌立绘等美术资源 | ❌ **不属于本工程，不读不改不复制** |
| `奥卡皮肤-Orca` | 角色皮肤：`animations/`、`materials/`、`CharacterSkinManager` | ❌ **不属于本工程，不读不改不复制** |

因此代码里的 `res://images/...`、`res://animations/...`、`res://materials/...` 这类路径
**由上面两个美术模组提供**，本仓库的 `.pck` 只负责自己的 `res://OrcaCharacter/localization/...`。
**修改本工程时不要往 `images/ animations/ materials/ shaders/` 里放东西** —— 那是在越界改别人的资源。

---

## 二、目录结构

```
OrcaCharacter.csproj        ← Godot.NET.Sdk；MSBuild 入口
OrcaCharacter.sln
Directory.Build.props       ← 工程常量（模组目录名、Godot 路径来源）
Sts2PathDiscovery.props     ← 游戏安装位置自动发现（注册表 / Steam），零绝对路径
local.props.example         ← 本机覆盖模板（local.props 已 gitignore）
OrcaCharacter.json          ← 模组清单（mod id / 依赖 BaseLib / has_pck / has_dll）
project.godot               ← Godot 工程文件（只为导出 .pck 而存在）
export_presets.cfg          ← Godot 导出预设 "BasicExport"
.gitignore

OrcaCharacterCode/          ← 全部 C# 源码，按功能分目录
├── Core/        角色本体、启动、配置、日志、卡牌基类、关键词、卡池
├── Cards/       卡牌定义
├── Powers/      能力（Power）模型
├── Patches/     Harmony 补丁
├── Epochs/      纪元
├── UI/          图标、能量珠、卡面 UI、皮肤挂点
├── Relics/      遗物
├── Audio/       音频与台词
└── _空文件/     0 字节残留文件（隔离保留，待确认后删除）

OrcaCharacter/              ← Godot 资源根 = res://
└── localization/zhs/*.json

docs/
├── 官方wiki-modding/       ← 官方 modding wiki 摘录
└── 参考-好工程-StS2-NotEnoughDifficulty/   ← 工程约定的参照标准（见 §五）
```

---

## 三、构建与部署（一条链，改的就是跑的）

```powershell
dotnet build          # 编译 → 自动把 dll / json / pdb 复制进 mods 目录
dotnet publish        # 上面 + 用 Godot 导出 .pck（需要 GodotPath）
```

`OrcaCharacter.csproj` 里的 `CopyToModsFolderOnBuild` / `GodotPublish` 两个 Target
保证**游戏目录里的构件一定是源码编出来的那一份**，不存在手工拷贝导致的漂移。

**这条链是硬要求，不是便利设施。** 本工程此前长期存在「改了源码、游戏里跑的却是旧构件」
的问题，排查任何行为差异前，先确认部署态与源码一致。

### 依赖与路径

依赖 dll **不在本仓库**（版权原因）：`sts2.dll` / `0Harmony.dll` 取自游戏安装目录，
`BaseLib.dll` 取自 Steam 创意工坊。位置由 `Sts2PathDiscovery.props` **自动发现**：

1. 注册表 `HKLM\...\Uninstall\Steam App 2868840` 的 `InstallLocation`
2. `HKCU\Software\Valve\Steam@SteamPath` 下的 `steamapps`
3. 都失败时用 `/p:Sts2Path=...`、环境变量或 `local.props` 指定

**任何文件里都不应出现本机绝对路径。** 需要覆盖时复制 `local.props.example` 为 `local.props`。

---

## 四、当前状态 / 已知缺口（不粉饰）

本仓库**即工程本体**，不依赖任何反编译/恢复阶段的外部产物：`dotnet build` 即可从本目录
编出可部署的构件。

| 项 | 状态 |
|---|---|
| `OrcaCharacterCode/Cards/Cards.cs` | **内容不完整、从方法中间截断，已排除出编译** ⇒ 含 `OrcaBloodSword` 等卡牌，待按设计补写 |
| `OrcaCharacterCode/_空文件/` | 3 个 0 字节文件（`OrcaEpochs` / `OrcaNirvanaSelfHarm` / `OrcaOverlookCompromise`），待确认后删除 |
| 本地化 | `OrcaCharacter/localization/zhs/` 部分文件内容少于游戏内实际版本，待整理 |
| 待验证改动 | 源码中残留 `TOGGLE-OFF-A2/A3/A4/A5/A7` 开关，对应的修复**尚未逐项实机验证** |
| `.pck` | 本机未安装 Godot，暂时无法重新导出；当前沿用既有 `.pck` |
| `LICENSE` | 尚未选定 |

---

## 五、工程约定

骨架（`.sln` / 根 `csproj` / `Sts2PathDiscovery.props` / Godot 导出 Target / 目录分法）
参照 `Coll-ed/StS2-NotEnoughDifficulty`（公开仓库，MIT）。副本见
`docs/参考-好工程-StS2-NotEnoughDifficulty/`，仅作阅读，不参与编译。

### ★ 同源重建（硬规矩）

**`dll` 与 `pck` 必须来自同一棵源码树的同一次重建。**

两者是同一工程编出的两个独立产物：`pck` 由 Godot 导出（装资源），`dll` 由 MSBuild 编出（装代码）。
因此它们**不是同一条编译输出** —— 混用不会当场报错，但会让**任何行为差异都无法归因**。

本工程曾长期踩这个坑：同一份 `OrcaCharacter/localization/zhs/cards.json` 同时存在三个版本
（官方 `pck` 里 6189 字节 / 中途重打包的 6288 / 仓库 `assets/` 里 6293），
导致「改了 X 所以 Y 变了」这类推理全部建立在乱账上。

**操作要求：**

```powershell
dotnet publish      # 一次把 dll 与 pck 都重建到 mods 目录，不要手工替换其中一个
```

需要单独改动某一边时，**必须先确认另一边的产物也来自当前源码**，否则这次改动的结果不可采信。

### 其他纪律

**不写死路径 / 常量单一来源 / 先读再改 / 一次只改一个变量 / 结论由实机验证后下。**

### Godot 版本约束

游戏内置 MegaDot **4.5.1**。**用更新版本 Godot 导出的 `.pck` 游戏不加载** ——
所以 `OrcaCharacter.csproj` 固定 `Sdk="Godot.NET.Sdk/4.5.1"`，导出也须用 Godot 4.5.1 mono。

`.godot/imported/` 与 `.godot/exported/` **必须入库**：`.pck` 里就包含这些导入产物，
不提交它们就无法在没有 Godot 的机器上复现打包。`.gitignore` 因此只忽略 `.godot/mono/`（编译产物）。

---

## 六、待查问题：一打出卡牌就卡死

> 保留此节供排查参考。**注意：此前的二分表是在「源码与部署不一致」的前提下做的，
> 结论需重新验证。**

### 现象

- **过回合完全正常** ✓（回合开始补能量、外框特效、敌方回合都正常）
- **一「打出卡牌」就卡死** ✗ —— 画面冻住、**进程还在**（Godot 主线程死锁形态）
- **无异常、无堆栈、无弹窗**；百科大全正常 ✓
- **打任何一张牌都会**（含最基础的「打击」），**冻在 `OnPlay` 之后**（`OnPlay` 内日志全部打出）
- 正常退出有 `resources still in use at exit`；卡死时**没有**这一行

### 日志特征

```
[INFO] Player 1 playing card ORCA_STRIKE (targeting …)
[INFO] [Orca] 打出 ORCA_STRIKE（手动）→ 目标=…，当前能量=2
[INFO] [Orca] 打击效果：对 … 造成 6 点伤害
[MeleeDebug][AnimPatch] …（攻击动画走原版 ✓）
[FastWait] … → 原版放行
（此后无任何输出 ⇒ 冻住）
```

### 已排除

最纯净环境（移除其它 mod）仍卡死 ⇒ 非 mod 冲突；修 `No suitable Formatter`（SmartFormat 报错
2 次 → 0 次）后仍卡死；`TweenProperty` 起点 `Nil` 的 `Type mismatch` 每回合都报但过回合不冻
⇒ 大概率只是噪声；`Orca : CharacterModel` → `CustomCharacterModel` 后仍卡死（且引入角色选择
界面多出皮肤项的副作用）。

### 希望得到的帮助

1. 「打出卡牌」链路（`OnPlay` → 结算 → `AfterCardPlayed` → 卡牌移动/消耗 → UI 刷新）里，
   哪些环节可能造成 Godot 主线程静默死锁？
2. 有没有已知的「自定义卡 + Harmony patch」在出牌路径上造成死锁的模式？
3. 有没有办法让这种冻结留下痕迹？（关键路径加日志无效，冻结点在日志之外）
4. 是否与 `CardPileCmd` / `CardSelectCmd` / `AfterCardPlayed` 这类异步钩子有关？
