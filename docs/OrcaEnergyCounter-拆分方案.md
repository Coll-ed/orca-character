# OrcaEnergyCounter.cs 拆分方案（2026-10-04 分析）

> **分析对象**：`OrcaCharacterCode/UI/OrcaEnergyCounter.cs`（602 行，全工程最大单文件）
> **结论摘要（先看这句）**：**建议不拆**。它是"一颗能量球的外观"这一件事的完整实现，
> 602 行里有 **187 行（31.1%）是注释**，实际代码只有 **343 行**。拆分不会降低复杂度，
> 反而会把"为什么不能这么写"的教训注释与代码分离——本项目真正的风险在那里，不在文件行数。
> 只建议 **1~2 处原地小修**（重复常量收敛 / 与具名常量不一致的日志数字），**不移动任何类型**。
> 若仍要拆，第四节给了完整风险清单，第六节 B 给了逐步可编译的执行清单。
>
> **本次分析的纪律**：没有修改任何代码文件。所有数字来自实测（工具 + 编译 + 反编译），
> 命令行与实测输出见 `## 附：本次实测记录`。
>
> **事实 / 建议的区分**：`一、二、四` 是事实（有位置依据）；`三、五、六` 是建议。

---

## 一、它现在到底装了什么（逐类型）

### 1.0 实测统计（不是估算）

| 指标 | 实测值 | 依据 |
|---|---|---|
| 文件总行数 | **602** | 按 LF 切分 601 个换行 + 末行（文件无结尾换行） |
| 其中：有效代码行 | **343** | 见 1.2 分类口径 |
| 其中：注释/文档行 | **187（31.1%）** | `///` + `//` + `/* */` |
| 其中：空行 | **72** | |
| **顶层类型** | **4 个** | 反编译构件实测 `ilspycmd -l c` |
| **嵌套类型** | **1 个**（`OrcaEnergyCounterPatch.EnergyBox`） | 同上 |
| 命名空间 | **1 个**：`OrcaCharacter`（file-scoped，`L8`） | `namespace OrcaCharacter;` |
| 本文件 `[HarmonyPatch]` 应用 | **3 处**（`L20` / `L165` / `L188`，均在列 0） | 全工程共 46 处、44 个类型 |
| 构件中本文件相关类型全名 | `OrcaCharacter.OrcaEnergyCounterPatch`、`.OrcaEnergyRefillPatch`、`.OrcaEnergyBurstPatch`、`.OrcaEnergyBurst`、`.OrcaEnergyCounterPatch.EnergyBox` | 编译后 `OrcaCharacter.dll` 反编译 |

> **类型清单的权威确认**（不是只看源码，而是看编译产物）：
> `C:\Users\34294\.dotnet\tools\ilspycmd.exe -l c <构建输出>\OrcaCharacter.dll` 实测输出：
> ```
> Class OrcaCharacter.OrcaEnergyCounterPatch
> Class OrcaCharacter.OrcaEnergyRefillPatch
> Class OrcaCharacter.OrcaEnergyBurstPatch
> Class OrcaCharacter.OrcaEnergyBurst
> Class OrcaCharacter.OrcaEnergyCounterPatch.EnergyBox
> Class OrcaCharacter.OrcaEnergyBurst.<>c__DisplayClass28_0   ← 编译器生成（闭包），不是源类型
> Class OrcaCharacter.OrcaEnergyBurst.<>c__DisplayClass29_0   ← 编译器生成（闭包），不是源类型
> ```
> ⇒ **源类型 5 个（4 顶层 + 1 嵌套）**，与背景事实给的清单一致，其中 `EnergyBox` 是背景事实里没提到的那一个。

### 1.1 逐类型明细

| # | 类型 | 行区间 | 声明 | 打在哪 | 职责 | 对外暴露面 |
|---|---|---|---|---|---|---|
| 0 | （文件头） | `L1–L19` | — | — | `using` × 6 + 文件级 xmldoc（19 行里 10 行是文档） | — |
| 1 | `OrcaEnergyCounterPatch` | `L21–L146`（127 行） | `internal static class`（顶层） | `NEnergyCounter.RefreshLabel` **postfix** | 按皮肤给能量球换色：递归挂材质 + 外框扫光材质 + 记录计数器 + 兜底复位 + "0→有"触发 | **无**（全 `private`） |
| 1a | └ `EnergyBox` | `L31–L34` | `private sealed class`（嵌套） | — | `ConditionalWeakTable` 的值类型，只装一个 `public int Value` | 仅本类 |
| 1b | └ `Postfix` | `L38–L97`（60 行） | `private static void` | — | 换色主流程 + 触发判定 | 仅本类 |
| 1c | └ `ApplyRingMaterial` | `L100–L119`（20 行） | `private static int` | — | 只给 `LayerRing` 挂"扫光材质" | 仅本类 |
| 1d | └ `ApplyTo` | `L122–L145`（24 行） | `private static int` | — | 递归给子树所有 `CanvasItem` 挂换色材质（含跳过规则） | 仅本类 |
| 2 | `OrcaEnergyRefillPatch` | `L166–L175`（代码 10 行；`L148–L165` 是它的 18 行文档） | `internal static class`（顶层） | `PlayerCombatState.ResetEnergy` **postfix** | 记"回合补满"时刻 → 延迟 `RefillDelay` 后放外框扫光 | **无** |
| 3 | `OrcaEnergyBurstPatch` | `L189–L201`（代码 13 行；`L177–L188` 是 12 行文档） | `internal static class`（顶层） | `NEnergyCounter.OnEnergyChanged` **postfix** | `new>old` 时放特效（`old==0` 或刚归零）；`new==0` 时记归零时刻 | **无** |
| 4 | `OrcaEnergyBurst` | `L209–L602`（400 行 = 代码 227 + 文档 124 + 空 49） | `internal static class`（顶层） | —（纯逻辑，不被 patch） | 特效播放器：时间闸/回合闸、残影生成与清理、外框扫光三段式 tween、硬复位、材质现场解析、按名找节点 | **12 个 `internal` 成员**（见 1.3） |

### 1.2 行数分类的统计口径

- **代码行** = 非空、非 `//`、非 `///`、非 `/*…*/` 的行（不剔除行尾注释）。
- **文档/注释行** = `///`、`//`、块注释整行。
- 分块实测（每块 code / doc / blank / total）：

```
header/using (1-19)                             7     10      2     19
OrcaEnergyCounterPatch (20-146)                84     25     18    127
  └ Postfix (38-97)                            38     15      7     60
  └ ApplyRingMaterial (100-119)                18      0      2     20
  └ ApplyTo (122-145)                          18      2      4     24
OrcaEnergyRefillPatch (148-175)                11     17      0     28
OrcaEnergyBurstPatch (177-201)                 14     11      0     25
OrcaEnergyBurst (203-602)                     227    124     49    400
  └ consts (211-252)                           13     17     12     42
  └ gates NoteRefill..JustHitZero (254-268)      8      4      3     15
  └ HardReset+ResetIfIdle (270-300)            22      8      1     31
  └ Play (311-373)                             41     14      8     63
  └ ClearGhosts+SpawnGhost (374-450)           52     17      8     77
  └ SweepRing (452-575)                        69     47      8    124
  └ CurrentRingMaterial+FindByName (577-602)     17      6      3     26
WHOLE FILE: code=343 doc/comment=187 blank=72 total=602  -> comment share=31.1%
```

**读法**：最"重"的 `SweepRing`（`L473–L575`，124 行）里 **47 行是注释**，代码 69 行；
`Play`（`L324–L373`）63 行里 14 行注释。**没有任何一个函数的代码行超过 70 行。**

### 1.3 对外暴露面（跨文件面的完整清单）

`OrcaEnergyBurst` 是**全文件唯一对外有面的类型**，12 个 `internal` 成员：

| 位置 | 成员 | 谁在用 |
|---|---|---|
| `L216` | `internal const float RefillDelay = 0.62f` | `OrcaEnergyRefillPatch`（本文件 `L173`）、xmldoc `L160` |
| `L219` | `internal const string TriggerRefill = "回合补满能量"` | 本文件 `L331` 判定 |
| `L255` | `NoteRefill()` | 本文件 `L170` |
| `L258` | `NoteZero()` | 本文件 `L88`、`L199` |
| `L261` | `JustRefilled()` | 本文件 `L342` |
| `L264` | `JustHitZero()` | 本文件 `L89`、`L195` |
| `L274` | `HardReset(mat, why)` | 本文件 `L299`、`L502`、`L570` |
| `L295` | `ResetIfIdle(why)` | 本文件 `L80` |
| `L324` | `Play(counter, delay, trigger)` | 本文件 `L90`、`L173`、`L196` |
| `L387` | `ClearGhosts(layers)` | 本文件 `L406` |
| `L402` | `SpawnGhost(layers, dragon, delay, trigger)` | **★ 本文件外唯一调用点：`OrcaCharacterCode/UI/OrcaOrbIcon2.cs:203`** |
| `L473` | `SweepRing(layers, delay, trigger)` | 本文件 `L367` |

**全工程扫描结果**：`OrcaOrbIcon2.cs:203` 是**唯一**一处跨文件引用：
```csharp
// OrcaCharacterCode/UI/OrcaOrbIcon2.cs:203
OrcaEnergyBurst.SpawnGhost(layers, center, 0.06f, $"切到{tag}");     // 残影（浮现 → 扩散淡出）
```
另一个方向也存在（构成双向耦合，不是单向依赖）：
```csharp
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:44       （本文件 → 调 OrbIcon）
OrcaOrbIcon.Remember(__instance);
// OrcaCharacterCode/UI/OrcaOrbIcon2.cs:83            （OrbIcon 暴露状态）
internal static NEnergyCounter? CurrentCounter => _counter;
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:171      （本文件读 OrbIcon 的状态）
var counter = OrcaOrbIcon.CurrentCounter;
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:131      （本文件读 OrbIcon 的状态）
if (OrcaOrbIcon.UserArtActive && root is TextureRect tr && ...
```
⇒ **"能量球外观"这件事从一开始就是 `OrcaEnergyCounter.cs` + `OrcaOrbIcon2.cs` 两个文件共同承担的**，
本文件已经只是其中一半。

---

## 二、职责边界分析

按"这段代码在回答什么问题"来分，602 行可切成 5 类。**这是事实层面的分类，不代表建议按它建文件。**

### 2.1 「补丁」—— 3 处，共 155 行（代码 45 行）

三个 patch 类都是**薄适配器**：把游戏的调用点翻译成"记时刻 / 放特效"两个动作。

| 补丁 | 挂点（依据） | 代码量 | 它到底做了什么 |
|---|---|---|---|
| `OrcaEnergyCounterPatch` | `[HarmonyPatch(typeof(NEnergyCounter), "RefreshLabel")]` `L20` | 60 行 `Postfix` + 44 行两个私有工具 | 换色（本文件自己的活）+ `Remember` + `ResetIfIdle` + 「0→有」判定 |
| `OrcaEnergyRefillPatch` | `[HarmonyPatch(typeof(PlayerCombatState), "ResetEnergy")]` `L165` | 10 行 | `NoteRefill()` → `Play(counter, RefillDelay, "回合补满能量")` |
| `OrcaEnergyBurstPatch` | `[HarmonyPatch(typeof(NEnergyCounter), "OnEnergyChanged")]` `L188` | 13 行 | `new>old` ⇒ 放；`new==0` ⇒ `NoteZero()` |

**注意**：只有 `OrcaEnergyCounterPatch` 是"补丁 + 业务"混合体（它自己干换色的活，占了 104/155 行）；
另外两个（23 行）是纯适配器。这是本文件内部**最真实的一条缝**，但它太窄（23 行），单独成文件不值。

### 2.2 「特效播放」—— 4 段，共 294 行（代码 227 行）：`OrcaEnergyBurst`

| 段 | 行区间 | 干什么 |
|---|---|---|
| 常量区 | `L211–L252` | 时长/阈值/窗口（13 个具名常量） |
| 时间闸（门） | `L255–L268` | `NoteRefill` / `NoteZero` / `JustRefilled` / `JustHitZero` —— 4 个 `TickCount64` 时间戳判定 |
| 兜底 | `L274–L300` | `HardReset`（4 个 shader 参数归零）+ `ResetIfIdle`（过期才复位） |
| 播放 ① | `L324–L373` `Play` | 去重门 → 找节点 → 转交 `SweepRing` |
| 播放 ② | `L387–L450` `ClearGhosts` / `SpawnGhost` | 残影生成（复制节点 + 两段 tween + `SceneTreeTimer` 兜底销毁） |
| 播放 ③ | `L473–L575` `SweepRing` | 外框扫光三段式（扫白 → 白→灰 → 灰→原色）+ 材质延迟解析 + 定时硬复位 |
| 取物 | `L582–L601` `CurrentRingMaterial` / `FindByName` | 现场取材质 / 按名递归找节点 |

**关键事实：这一段的"复杂度"不来自长度，来自 4 份共享状态必须同时正确**：

```csharp
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:231-249
private static Tween? _sweepTween;          // 当前正在播的外框 tween
private static ShaderMaterial? _sweepMat;   // 当前外框材质（兜底复位用）
private static long _sweepUntilMs;          // 预计结束时间（到点强制复位）
private static long _lastRefillMs;          // 上次「回合补满」
private static long _lastZeroMs;            // 上次「能量见底」
private static long _lastPlayMs;            // 极短去重窗口
private const long ZeroWindowMs = 200;
private const long RefillWindowMs = 1500;
```
这 8 个字段被 `OrcaEnergyCounterPatch`（读 4 个写 3 个）、`OrcaEnergyRefillPatch`（写 1 个）、
`OrcaEnergyBurstPatch`（写 2 个读 1 个）、`OrcaEnergyBurst`（全读写）**同时触碰**。
这就是"补丁"和"特效"之间真正的关系：**共享状态机**，不是调用链。

### 2.3 「共享状态/常量」—— 三类，散落在两处

**(a) 时间段常量**（`OrcaEnergyBurst` 内，`L211–L252`）：13 个具名常量，都写了来源理由：

```csharp
// L216  原版 AnimIn() 是 0.6 秒 Expo-Out 滑入，且 ResetEnergy() 跑在它之前
internal const float RefillDelay = 0.62f;
// L246 / L249  用户口径的「刚刚归零 / 刚刚补满」时间窗
private const long ZeroWindowMs = 200;
private const long RefillWindowMs = 1500;
// L252  派生量，不是第三处副本 ✔ 写法正确
private const float SweepTotal = SweepDuration + ReturnGray + ReturnColor;
```

**(b) 每颗球的能量值**（`OrcaEnergyCounterPatch` 内，`L31–L36`）：
```csharp
private sealed class EnergyBox { public int Value; }                       // L31-34
private static readonly ConditionalWeakTable<NEnergyCounter, EnergyBox> _lastEnergy = new();  // L36
```
这里有一段**必须跟着代码走**的教训注释（`L27–L30`）：值类型必须是"有参数构造器的 class"，
否则 `GetOrCreateValue` 抛 `Cannot dynamically create an instance of type 'System.Int32[]'`。

**(c) 皮肤/材质路径**：全部经 `OrcaSkin`，本文件**没有硬编码路径**（符合"不写死"）：
```csharp
// L49   var path = OrcaSkin.EnergyOrbMaterialPath;
// L111  var path = OrcaSkin.RingSweepMaterialPath;
// L66/L70/L574  OrcaSkin.Active（日志）
```
`OrcaSkin` 侧（`OrcaCharacterCode/UI/OrcaSkin.cs:96-120`）按 `_active == Wedding` 二选一，
且 `L319` 声明了**全项目唯一的"按皮肤二选一"入口** `BySkin<T>`。**本文件是这条规范的遵守者，不是违犯者。**

### 2.4 「兜底逻辑」—— 4 处，全都带"为什么必须有"的注释

| # | 位置 | 兜底什么 | 注释里的实测依据 |
|---|---|---|---|
| 1 | `L274–L289` `HardReset` | tween 被 Kill 后材质停在"累积满 + strength=1" ⇒ 外圈**永久变白** | "否则外圈会永久停在白色（用户实测）" |
| 2 | `L295–L300` `ResetIfIdle` | 过了结束时间还没归零就复位；**只在没在播时动手** | `L298` `if (TickCount64 < _sweepUntilMs) return;  // 还在播，绝不打扰` |
| 3 | `L441–L449` `SceneTreeTimer` | tween 被 Kill 时 `Finished` 不触发 ⇒ 残影永久留在场上 | 引官方教程 "VFX Playback & Caching → PlaySimple" |
| 4 | `L566–L571` 定时硬复位 | 同上，但针对外框；取"当下真正挂在渲染链上"的材质 | "不计代价保证'不会永久停在白色'" |

**另一条极重要的"反向兜底"注释**（`L73–L76`）——它记录的是**一段被撤销的自愈代码**：
```
★ 注意：这里**不做**任何"归位/自愈"——
  能量为 0 时原版会把 _layers.Modulate 压成 DarkGray（那是**原版行为**，不是残影残留），
  我一度误判成"残影没清掉"，加的"每次刷新自愈"反而把刚生成的残影当场清掉，
  导致"切换形态看不到残影"。现已撤销（用户确认那是能量 0/非 0 的变化）。
```
**这类注释是本文件最有价值的资产，它防的是"下一个人重新犯同一个错"。**

### 2.5 「按名字在场景树里抓节点」—— 6 个字符串字面量构成的隐式契约

| 字符串 | 出现位置 | 含义 |
|---|---|---|
| `"%Layers"` | `L349`（唯一名字取法） | 图层根 |
| `"Layers"` | `L349`（兜底名） | 同上 |
| `"LayerDragon"` | `L350`、`L131` | 中心图标层 |
| `"LayerRing"` | `L103`、`L107`、`L129`、`L476`、`L585` | 外框层 |
| `"OrcaBurst"` | `L393`（前缀扫描）、`L411`（`OrcaBurstGhost` 命名）、`L128`（跳过判定） | 特效节点前缀 |
| `"OrcaBurstFlash"` | **`OrcaOrbIcon2.cs:221`**（在**另一个文件**里命名） | 高亮层（也被 `L393` 的前缀扫到） |

⇒ `"OrcaBurst"` 这个前缀是**跨文件的隐式契约**，目前以纯字符串形式分散在 2 个文件的 4 处。
**这是本文件里唯一一处"同一常量定义了不止一次"的实例（`OrcaBurstGhost` / `OrcaBurstFlash` 共用前缀）。**

---

## 三、★ 拆分方案：**建议不拆**

### 3.1 结论

**不建议按类型拆分。** 理由是五条实测事实，不是"感觉"：

**① 拆分收益的上限很低——文件的实际代码量只有 343 行，没有一个函数超过 70 行代码。**
最大的方法 `SweepRing` 是 69 行代码 + 47 行注释。可读性的瓶颈是**注释密度**，不是长度。

**② 31.1% 的行是"为什么不能那么写"的教训注释，它们必须贴着代码。**
`L73–L76`（撤销自愈）、`L484–L511`（`.From()` 与初值提前，两次失败修复）、
`L462–L471`（起点随机的真因不是随机数）、`L27–L30`（`int[]` 会抛）、
`L160–L163`（为什么必须等 0.62 秒）、`L439–L440`（为什么不能只靠 `tween.Finished`）。
**拆成 4 个文件后，这些注释会与它们所解释的那行代码分居两处**；
而按"改动最小"的纪律，新文件里通常只搬最小注释 ⇒ **教训会在搬运中丢失**。
对一个已经为"同一个坑踩两次"付过代价的工程（`L499–L518` 记着修复 B 无效、修复 C 才是真根因），这是**净损失**。

**③ 真正的复杂度是"跨 3 个补丁 + 1 个播放器共享的 8 个静态状态"，拆文件不会减少它。**
`_sweepTween` / `_sweepMat` / `_sweepUntilMs` / `_lastRefillMs` / `_lastZeroMs` / `_lastPlayMs`
加上 `_lastEnergy`（`ConditionalWeakTable`）与 `_ghostSeq`，是**一个必须整体审阅的状态机**。
拆开后，审"同一回合会不会放两次"这件事要同时开 3 个文件——
而"每回合放两次、互相 Kill"正是 `L333–L336` 明确记着的**已发生过的事故**。

**④ "能量球外观"是同一个功能，且已经跨 2 个文件分工了。**
`OrcaEnergyCounter.cs` + `OrcaOrbIcon2.cs`（260 行）合起来才是一件事：换色 / 形态 / 残影 / 扫光。
再从本文件内部切出去一块，只会让"改一个外观要开 3 个文件"。

**⑤ 拆分的确定性收益只有"单文件行数变小"这一个指标，而那个指标在本工程没有硬约束。**
没有 CI（无 `.github/`）、没有 lint 规则限制文件长度、csproj 用 SDK 默认 glob
（实测 67 个 `Compile` 项全部 `DefiningProjectName = Microsoft.NET.Sdk.DefaultItems`，无一处显式 Include）
⇒ **行数本身不带任何工程性代价**。

> 一句直白的话：**"最大的单文件"是个统计事实，不是技术债。**
> 把它拆成 4 个 60~250 行的文件，行数不变、状态不变、耦合不变，**唯一变化是注释与代码分离**。

### 3.2 那"该做"的是什么？—— 原地小修（2 项，都是本文件内、都能立刻编译）

拆分不做，但下面两处是**实测存在的真问题**，且符合"单一来源 / 不要魔法数"的硬规矩：

**修 1（对应规矩 2「单一来源」）：`"0 能量后获得"` 是一个定义了两次的常量。**

```csharp
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:90      （OrcaEnergyCounterPatch.Postfix 内）
OrcaEnergyBurst.Play(__instance, 0f, "0 能量后获得");
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:196     （OrcaEnergyBurstPatch.Postfix 内）
OrcaEnergyBurst.Play(__instance, 0f, "0 能量后获得");
```
处置：在 `OrcaEnergyBurst` 里加一个 `internal const string TriggerGain = "0 能量后获得";`
（与已有的 `TriggerRefill`（`L219`）并列），两处改引用。
**注意方向**：它现在**只是日志/显示用的标签**（`L331` 的 `isRefill` 只比对 `TriggerRefill`），
所以收敛**不改变任何行为**——这一点必须先核实再改，否则会误伤判定逻辑。

**修 2（对应规矩 3「不要魔法数」）：日志里写死的 `120` 与具名常量不一致的风险。**

```csharp
// OrcaCharacterCode/UI/OrcaEnergyCounter.cs:340
if (now - _lastPlayMs < 120) { OrcaLog.Info($"[Orca] 外框：{trigger} 与上一次间隔过近（<120ms），跳过", 2); return; }
```
`120` 出现两次（判定 + 日志文案），而旁边就有具名常量（`ZeroWindowMs = 200`、`RefillWindowMs = 1500`，`L246/L249`）。
处置：加 `private const long DuplicateSkipMs = 120;`（**注明来源：用户口径"同一回合补满与 0→有只能放一次"，实测 120ms 足够覆盖同一次能量变化的双报**），
判定与日志文案都引用它。
**为什么值得改**：改常量的那天，日志会继续坚称 `<120ms` ⇒ 日志撒谎，而本工程的排错**严重依赖日志**（见 `docs/踩坑指南.md`）。

**可选修 3（跨文件，需另开决定）**：把 `"OrcaBurst"` 前缀提成一个共享常量
（它连接 `OrcaEnergyCounter.cs:393` 的清理扫描与 `OrcaOrbIcon2.cs:221` 的命名）。
这**会碰第二个文件**，超出"改动最小且集中"，**建议单独立项，不要顺手做**。

### 3.3 如果仍然决定要拆（供决策参考，不是推荐）

| 方案 | 切法 | 结果 | 评价 |
|---|---|---|---|
| A. 按类型切 | 3 个补丁 1 个文件 + `OrcaEnergyBurst` 1 个文件 | 155 行 / 400 行 | 收益 0：状态机仍跨 2 文件，反而多一层跳转 |
| B. 按"触发 / 播放"切 | `OrcaEnergyTriggers.cs`（3 补丁 + 门 + 残影，约 210 行）+ `OrcaEnergyBurst.cs`（扫光 + 复位 + 取物，约 390 行） | 2 文件 | **若必须拆，选这个**：切在 2.2 里最窄的那条缝上，且 `SweepRing` 与 `HardReset` 同文件（它们靠 `_sweepUntilMs` 联动） |
| C. 按"节点操作工具"切 | 把 `ApplyTo`/`ClearGhosts`/`FindByName`/`CurrentRingMaterial` 抽成一个 helper | 约 90 行新文件 | **不要**：这 4 个方法各只被调用 1~2 次，抽出去是"为对称而抽"，且它们**必须知道 `"OrcaBurst"` 前缀契约**（见 2.5），抽走反而把契约藏进工具类 |

---

## 四、★ 后果与风险（逐条）

### 4.1 命名空间要不要改？—— **不用改，调用点完全不受影响（已核实）**

**事实**：全工程 **67/67** 个 `.cs` 都在同一个命名空间下（实测 `^namespace ` 命中 67 次），
本文件是 **file-scoped namespace** `namespace OrcaCharacter;`（`L8`）。
`internal static class` + 同命名空间 ⇒ 类型的**完全限定名不变**（实测反编译构件：
`OrcaCharacter.OrcaEnergyBurst` 等）。

**推论**：
- `OrcaOrbIcon2.cs:203` 的 `OrcaEnergyBurst.SpawnGhost(...)` —— **一个字符都不用改**。
- 唯一的例外：如果把类型挪进 `.editorconfig` 新增的子命名空间（如 `OrcaCharacter.UI`），
  则 `OrcaOrbIcon2.cs`（在 `OrcaCharacter`）会**编译失败**——但这个失败是**编译期的**，不是运行期隐患。
  ⇒ **不要新建命名空间**，保持 `OrcaCharacter`。

### 4.2 Harmony 补丁注册会不会受影响？—— **不会，但有一个必须遵守的约束**

**先看它是怎么注册的**（`OrcaCharacterCode/Core/OrcaBootstrap.cs:84-97`）：
```csharp
foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
{
    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0) continue;
    try { new PatchClassProcessor(harmony, type).Patch(); ok++; }
    catch (Exception ex) { fail++; Log.Error($"[Orca] Harmony patch failed for {type.Name}: ...", 2); }
}
```

**逐条回答**：

1. **不依赖文件名、不依赖目录、不依赖顺序** —— 它靠 `Assembly.GetTypes()` 反射扫类型。
   把 `OrcaEnergyCounterPatch` 从 `UI/OrcaEnergyCounter.cs` 搬到任何 `.cs`、放任何目录，**注册结果完全相同**。
2. **本项目已有跨文件先例，且已实测通过** —— `OrcaSceneSkin.cs` 里有 `OrcaMerchantSkinPatch`（`L119`）、
   `OrcaRestSiteSkinPatch`（`L150`）；`OrcaCharacterIcon.cs` 里有 5 个补丁类（`L124/L179/L198/L219/L236`）；
   `OrcaParticlesGuard.cs` 的 2 个补丁类是**嵌套**在容器类里的（`L49/L60`）。
   ⇒ "一个文件里多类"和"补丁类与业务类同文件"**都是本工程已验证的既有风格**（见 4.6）。
3. **约束（唯一的真约束）**：`PatchClassProcessor` 只处理**被反射枚举到的类型**。
   实测证明嵌套类也会被枚举到（`OrcaCharacter.OrcaEnergyCounterPatch.EnergyBox` 出现在类型列表里），
   所以**没有"嵌套类被漏掉"的风险**。
4. **不会新增/减少补丁数量** —— 搬迁不增删类型，`ok/fail` 计数不变。
   实测：本文件贡献 3 个带 `[HarmonyPatch]` 的顶层类型（`L21/L166/L189`）；
   全工程共 46 处属性、**44 个类型携带 `[HarmonyPatch]`**（35 顶层 + 9 嵌套）。
5. **一个真正需要留意的点（不是风险，是`fail++`会吞成日志）**：
   `catch` 分支只记日志、不抛出（`L95`）⇒ 如果搬迁时**打错程序集名/方法名**，游戏不会崩，只是补丁静默失效。
   ⇒ 所以验证必须**读游戏日志里的 `[Orca] Harmony 补丁: N 成功 / M 失败`**（`L99`），不能只看"游戏能启动"。

### 4.3 git 历史表现：重命名 vs 删除+新增

**事实（本仓库已实测）**：
- `.gitattributes:2` = `* text=auto eol=lf` ⇒ 仓库内统一 LF；本文件实测 601 LF / 601 CR，**无 BOM（首 3 字节 `117,115,105` = `usi`）**。
- 前例：提交 `9aab57a`（"工程骨架重建"）已经做过一次全树移动（`src/*.cs` → `OrcaCharacterCode/*/*.cs`），
  实测 `git log --name-status --diff-filter=RC` 显示**全部是 `R100`**（100% 相似度重命名，历史完整保留）。
- 本文件当前在 HEAD 的对象哈希：`d46742dd45285ba3cbbc7b86bf19b6150bb8984d`；工作区干净（`git status -sb` = `## main`）。

**结论与操作要求**：
- **必须用 `git mv`**（或"先删后加、内容逐字节不变"也一样能被识别为 `R100`，因为识别靠 blob 哈希比对）。
- **第一步只 `git mv`，不要同时改内容** —— 带内容修改的移动会退化成 `R0xx`（部分相似），
  之后 `git log --follow` 仍能追，但 `git blame` 会整片翻新。**分两个 commit：① 纯移动 ② 语义修改。**
- **不要**"删掉旧文件 + 新建文件并顺手调整格式/换行/注释" —— 那会让相似度掉到阈值以下，历史断掉
  （而本工程的 `docs/卡死二分方案.md:36` 恰恰把"从错误的树编译/不知道跑的是哪一版"列为教训）。

### 4.4 编译与部署验证要怎么做（具体命令，本机实测过）

**前置事实（两条，来自 `docs/踩坑指南.md`）**：
- 坑 6-7（`踩坑指南.md:444-453`）：**本工程里任何 `dotnet build` 都会写游戏目录** ⇒ 纯验证必须把输出改道。
  ```powershell
  # 踩坑指南.md:452-453 给的标准做法
  dotnet build   $Repo\OrcaCharacter.sln -t:Rebuild -p:CustomModsPath=$env:TEMP\verify
  dotnet publish $Repo\OrcaCharacter.csproj        -p:CustomModsPath=$env:TEMP\verify
  ```
- 坑 2-5（`踩坑指南.md:194-197`）：增量构建可能**跳过 CoreCompile**、报 0 错误却没重编 ⇒ 验证编译性**必须 `-t:Rebuild`**。

**步骤 1（每次落盘都做）：强制重编 + 改道输出，确认 0 错**
```powershell
Push-Location "D:\AI\AI实验室\杀戮尖塔2mod开发\银龙奥卡\银龙奥卡-待上传-源码与说明"
dotnet build OrcaCharacter.sln -t:Rebuild -v:m --nologo -p:CustomModsPath="$env:TEMP\orca-verify"
Pop-Location
```
**预期（实测基线，2026-10-04 20:25）**：`已成功生成。` + `0 个警告` + `0 个错误` + exit 0 +
出现 `[Orca] 复制 dll / json / pdb → C:\Users\...\orca-verify`。
**反向判据**：若日志里出现 `正在跳过目标 CoreCompile`，这次结果**不能**作为"编译通过"的证据。

**步骤 2（拆分的确定性凭证）：比对类型集合，必须一模一样**
```powershell
$ilspy = "C:\Users\34294\.dotnet\tools\ilspycmd.exe"
& $ilspy -l c "$env:TEMP\orca-verify\OrcaCharacter.dll" |
  Select-String "OrcaCharacter\.OrcaEnergy" | ForEach-Object { $_.Line.Trim() }
```
**预期（实测基线）**：
```
Class OrcaCharacter.OrcaEnergyCounterPatch
Class OrcaCharacter.OrcaEnergyRefillPatch
Class OrcaCharacter.OrcaEnergyBurstPatch
Class OrcaCharacter.OrcaEnergyBurst
Class OrcaCharacter.OrcaEnergyCounterPatch.EnergyBox
```
**判据**：这 5 行**逐字不变**（`<>c__DisplayClass*` 这类编译器生成名可能因闭包布局变化而编号不同，
只要"用户类型"部分不变即可）。**基线：构件内用户类型 129 个 / 编译器生成 100 个 / 合计 229 个。**
> 为什么这一步是关键：本工程**没有任何 CI**（实测无 `.github/`），
> 类型集合比对是**唯一能在不启动游戏的情况下**证明"反射注册面没变"的手段。

**步骤 3（部署，只在步骤 1 通过后做）**
```powershell
Push-Location "D:\AI\AI实验室\杀戮尖塔2mod开发\银龙奥卡\银龙奥卡-待上传-源码与说明"
dotnet build OrcaCharacter.csproj -v:m --nologo      # 不带 -p: → 落到游戏 mods 目录
Pop-Location
```
本机实测解析出的部署目标（`dotnet msbuild ... -getProperty:CustomModsPath`）：
`d:\steam\steamapps\common\Slay the Spire 2\mods\奥卡角色-Orca`。
**只改 `.cs` 时不要 `publish`** —— `publish` 会重建 58.8 MB 的 `.pck`（本机实测现存的 `OrcaCharacter.pck` = 58,866,726 B）；
纯代码改动不需要它，且 `踩坑指南.md:139` 明确"任何非代码改动必须用 publish"（反过来说代码改动不必）。

**步骤 4（游戏内验证，本工程的最终判据）**
1. 启动游戏，`grep "[Orca] Harmony 补丁:"` 日志行 → **`N 成功 / 0 失败`**，
   且 `N` 与本次改动前**完全相同**（`OrcaBootstrap.cs:99` 的计数器；历史值见
   `docs/排查结论汇总-20261004.md:113` 记的 `41 成功 / 0 失败`——该值早于当前源码，
   **请以本次改动前实测到的 N 为基准，不要拿 41 当判据**）。
2. `[Orca] 构建标记 = handwritten+merchant-skin`（`OrcaBootstrap.cs:32` 的 `BuildTag`）——
   确认跑的是这一版（这是本工程为"跑错版本"专门加的标记，`docs/卡死二分方案.md:36`）。
3. 人工看 5 件事：① 板甲/婚纱换色；② 能量花光 → 再获得（外框扫光）；③ 回合结束补满（延迟 0.62s 后扫光）；
   ④ 切换武器形态（回弹 + 残影 + 高亮）；⑤ 魔典形态下中心图标**不被刷成单色**（`L131` 的跳过规则）。
4. 重点观察**不会被编译期发现**的两件事：**外圈是否永久停在白色**（兜底失效）、
   **残影是否叠加/久留**（`ClearGhosts` 失效）。

### 4.5 拆错了怎么回滚（具体命令）

**事实**：当前 HEAD = `eaa87a01948b19b5a1242cf1d9e1febf4cc2fe2a`，工作区干净（`git status -sb` = `## main`，无未提交改动）。
⇒ **现在是干净的还原点**，先记下它。

```powershell
# 0) 记录还原点
Push-Location "D:\AI\AI实验室\杀戮尖塔2mod开发\银龙奥卡\银龙奥卡-待上传-源码与说明"
git rev-parse HEAD    # → eaa87a01948b19b5a1242cf1d9e1febf4cc2fe2a

# 情况 A：已提交（推荐路径 —— 与"每一步都能编译"配套，一步一提交）
git log --oneline -3                       # 找到拆分那个提交
git revert --no-edit <拆分提交 sha>         # 不重写历史，回滚本身也有记录
# 或若确认没有别的改动需要留：
git reset --hard eaa87a0                   # ← 只有在"确认要丢弃后续全部提交"时才用

# 情况 B：未提交（还在工作区）
git restore --source=HEAD --staged --worktree -- OrcaCharacterCode/UI/OrcaEnergyCounter.cs
git status --short                          # 期望输出为空

# 情况 C：已提交，且新建了文件（restore 只还原被追踪的文件，新文件要单独清）
git clean -nd -- OrcaCharacterCode/UI/     # ★ 先 dry-run 看清楚要删什么
git clean -fd  -- OrcaCharacterCode/UI/     # 确认无误再执行
```
**回滚后必须重做验证**（否则不知道游戏里跑的是哪一版）：
```powershell
dotnet build OrcaCharacter.sln -t:Rebuild -v:m --nologo -p:CustomModsPath="$env:TEMP\orca-verify"
dotnet build OrcaCharacter.csproj -v:m --nologo      # 重新部署回 mods
# 再用 ilspycmd -l c 比对类型集合恢复到 5 行原样（见 4.4 步骤 2）
```
> ⚠️ **回滚的坑**：`dotnet build` 已经把新 dll 写进游戏 mods 目录（`OrcaCharacter.dll`，本机现为 201,728 B）。
> **只做 `git restore` 而不重新 build，游戏里跑的仍是坏版本。** 必须"回滚源码 → 重编 → 重新部署"三步齐做。

### 4.6 与"本工程已有拆分风格"的对照（回答"另一个文件是不是更好的风格"）

实测三种既有风格并存，**没有一种占主导**：

| 风格 | 实例（实测位置） | 是否"一个文件一个类" |
|---|---|---|
| 一文件一主类 + 补丁类**同文件顶层** | `UI/OrcaSceneSkin.cs`：`OrcaSceneSkin`(`L48`) + `OrcaMerchantSkinPatch`(`L119`) + `OrcaRestSiteSkinPatch`(`L150`)；`UI/OrcaCharacterIcon.cs`：`OrcaCharacterIcon`(`L22`) + 5 个补丁类 | ✗ |
| 一文件一主类 + 补丁类**嵌套** | `Patches/OrcaParticlesGuard.cs`：`OrcaParticlesContainerGuard`(`L30`) + 嵌套 `RestartPatch`(`L49`) / `SetEmittingPatch`(`L60`) | ✗ |
| **只做补丁**的文件（Patches/ 下 10 个） | `OrcaEnergyIcon.cs`（34 行）、`OrcaCardPlayLog.cs`（32 行）、`OrcaPowerIconSkin.cs`（83 行）… 平均 87 行，**每个都只有 1 个补丁类 + 1 段长注释** | ✓（一文件一类） |

**读法**：`Patches/` 的"一文件一类"成立，是因为**每个补丁独立、各有各的挂点、彼此无共享状态**。
`OrcaEnergyCounter.cs` 的三个补丁**共享同一个状态机**（4.2/2.2），
**恰好不满足** `Patches/` 风格的成立条件 ⇒ 按 `Patches/` 风格拆它，是**误用了那个风格的适用前提**。
这一点也和 `docs/代码分类清单.md:110-112` 自己记的结论一致：
> "`Patches/` 与其它目录的界限是'这个文件是否只做补丁' —— 但实际上 `Core/OrcaPoolRegistry.cs`、
> `UI/OrcaCharacterIcon.cs` 等也含补丁类 ⇒ **这个界限目前是约定而非结构保证**。"

**另注（`docs/代码分类清单.md:1-4`）**：该清单明确写了本次"**只出清单，不移动文件、不改命名空间**"，
并在 `L102-103` 把本文件列为"拆分的第一候选"。
**本方案是对那条建议的回答：经逐行核实后，建议否掉按类型拆，只做原地小修。**

---

## 五、不建议动的部分（能拆但别拆）

| # | 位置 | 为什么不拆（具体理由，不是原则口号） |
|---|---|---|
| 1 | `L31–L36` `EnergyBox` + `_lastEnergy` | 唯一的"为什么不能用 `int[]`"证据（`L27–L30` 记着真实抛错）。挪进工具类后，下一个人很可能会把它"优化"回值类型字典，**重新引回同一个异常**。 |
| 2 | `L582–L587` `CurrentRingMaterial` | 它的契约是**"任何时候都不能缓存材质引用，必须现场取"**（`L577–L581`）。抽成"通用取材质助手"会诱使别人给它加缓存 ⇒ 直接毁掉"修复 C"（`L504–L518`）。 |
| 3 | `L295–L300` `ResetIfIdle` 与 `L473` `SweepRing` | 二者通过 `_sweepUntilMs` 联动（前者"还在播就不打扰"依赖后者写的时间戳）。**必须同一文件审阅**——分开后"外圈永久变白"会重现（`L562–L565` 记着实测）。 |
| 4 | `L255–L268` 4 个时间闸 | 只有 15 行，且被 3 个补丁 + `Play` 共同调用。抽出去会让"同一回合放几次"这个判断**跨文件才能看清**。 |
| 5 | `L73–L76` 那段"不做自愈"的注释 | 这是**被撤销代码的墓碑**。拆文件时最容易"因为新文件开头空着就顺手删掉"。**必须逐字随代码走**。 |
| 6 | `L148–L165`、`L177–L188`（两段类级文档） | `OrcaEnergyRefillPatch` 的文档（17 行）比它的代码（10 行）还长，里面是 `CombatManager.SetupPlayerTurn` 的反编译证据。**这类"行数倒挂"是资产，不是缺陷**。 |
| 7 | `L49–L51` / `L111–L116` 材质路径取值 | 已正确经 `OrcaSkin.EnergyOrbMaterialPath` / `RingSweepMaterialPath`，**没有硬编码路径**。不要"为了整齐"把路径搬进本文件。 |
| 8 | `L492` `const float soft = 40f;` | **方法内局部常量**，与"同一常量只定义一次"不冲突（无第二处副本），且注释已给理由（前缘柔化宽度）。**不要**为了"集中常量"把它提到类级——会拉开与使用点的距离。 |
| 9 | 整个 `OrcaEnergyBurst` 与 `OrcaOrbIcon2` 的边界 | 已经稳定（两文件双向引用 4 处、契约明确）。**再切只会增加跨越次数。** |
| 10 | `OrcaSkin.cs`（324 行）的访问器 | 有人可能想"顺手把 `Tint`（`OrcaSkin.cs:180`）挪到能量球文件"。**不动**：它是四套皮肤共用面，挪走会制造第二处皮肤配色来源。 |

---

## 六、执行清单

### 6.A 推荐路径：不拆，只做 2 处原地小修（每步都能编译）

> 每步结束都必须跑一次 6.C 的验证命令。**说明**：以下步骤是**建议**，本次分析**未执行、未修改任何文件**。

| 步 | 动作 | 位置 | 完成判据 |
|---|---|---|---|
| 0 | 记录还原点：`git rev-parse HEAD` → `eaa87a0`；确认 `git status --short` 里**没有任何 ` M` / ` M` 行**（只有 `??` 未追踪的 `docs/*.md` 是正常的） | — | 已跟踪文件全部干净 |
| 1 | 跑基线验证（6.C 步骤 1+2），把"改动前的类型清单 + 补丁日志 N"写进提交信息 | — | 0 错 0 警，类型 5 行原样 |
| 2 | **修 1**：在 `OrcaEnergyBurst` 常量区（`L219` 附近）加 `internal const string TriggerGain = "0 能量后获得";`，把 `L90`、`L196` 两处字面量改为引用 | 本文件内 | 编译 0 错；**先确认它只用于日志/显示**（`L331` 只比对 `TriggerRefill`） |
| 3 | **修 2**：加 `private const long DuplicateSkipMs = 120;`（注释写来源），`L340` 的判定与日志文案都改引用 | 本文件内 | 编译 0 错；日志文案不再自带数字 |
| 4 | 全量验证 + 部署（6.C 步骤 1→2→3），游戏内按 4.4 步骤 4 人工过 5 条外观路径 | — | 补丁 N 不变、外观 5 条全对 |
| 5 | 若做了**可选修 3**（`"OrcaBurst"` 前缀），**单独立项**、单独提交，且必须同时改 `OrcaOrbIcon2.cs:221` | 2 文件 | 明确记录"这次动了两个文件" |

**预估工作量**：2 处小修合计改动 ≈ 6 行（含 2 行常量定义与注释），风险极低。

### 6.B 备选路径：如果坚持要拆（方案 B：按"触发 / 播放"切）

> **不推荐**。列在这里是为了"决策留痕"，不是为了执行。**每一步结束都必须编译通过**。

| 步 | 动作 | 校验 |
|---|---|---|
| 1 | `git mv OrcaCharacterCode/UI/OrcaEnergyCounter.cs OrcaCharacterCode/UI/OrcaEnergyTriggers.cs` **（只改名，不改一个字节）** | `git status --short` 显示 **`R100`**；`git log --follow --oneline -- OrcaCharacterCode/UI/OrcaEnergyTriggers.cs` 能追到 `8ee5837`；编译 0 错 |
| 2 | 提交：`git commit -m "refactor(ui): 纯重命名，内容零改动（为拆分留 R100 记录）"` | `git show --stat` 只显示 1 个文件、0 增 0 删 |
| 3 | 新建 `OrcaCharacterCode/UI/OrcaEnergyBurst.cs`，把 `OrcaEnergyBurst` 类（`L209–L602`，含它的整段 xmldoc）**整块搬过去**，保留 `namespace OrcaCharacter;` + 所需 `using`；`OrcaEnergyTriggers.cs` 删掉这一段 | 编译 0 错；**`SpawnGhost`/`Play`/`RefillDelay` 必须仍是 `internal`**（`OrcaOrbIcon2.cs:203` 要靠它） |
| 4 | 不新增命名空间；不动 `OrcaSkin`；不动任何常量值 | `ilspycmd -l c` 比对：用户类型 5 个、全名逐字不变，仅**所在文件**变了 |
| 5 | 提交；跑 6.C 全部验证 + 游戏内 5 条外观路径 | 补丁 N 不变；外圈不永久变白；残影不叠加 |
| 6 | 若任一外观行为回归 ⇒ 按 4.5 回滚（`git revert` 步骤 3 的提交即可，步骤 1-2 的纯改名可保留） | 回滚后重编 + 重新部署 |

**预估**：拆完是 **208 行 / 394 行**两个文件（合计仍 602 行）。
**必须同时修改的注释**：`L489` 里写着 `[2] OrcaEnergyBurst.SweepRing`（栈帧引用）——类名不变所以不用改；
但 `L84` 的 `← 见 OrcaEnergyRefillPatch（ResetEnergy）` 与 `L362` 的 `见 OrcaOrbIcon.Swap` 是**跨文件指引**，
拆分后要检查这些指引是否还指向正确文件（**这正是拆分最容易被忽略的代价**）。

### 6.C 通用验证命令（两条路径都要跑）

```powershell
$Repo = "D:\AI\AI实验室\杀戮尖塔2mod开发\银龙奥卡\银龙奥卡-待上传-源码与说明"

# 1) 编译性（强制重编 + 改道，不碰游戏目录）
dotnet build "$Repo\OrcaCharacter.sln" -t:Rebuild -v:m --nologo -p:CustomModsPath="$env:TEMP\orca-verify"
#    期望：已成功生成。/ 0 个警告 / 0 个错误 / 出现 "[Orca] 复制 dll / json / pdb → ...\orca-verify"
#    反向判据：出现 "正在跳过目标 CoreCompile" ⇒ 本次结果无效

# 2) 类型集合比对（拆分的确定性凭证；无 CI 工程的唯一替代）
& "C:\Users\34294\.dotnet\tools\ilspycmd.exe" -l c "$env:TEMP\orca-verify\OrcaCharacter.dll" |
  Select-String "OrcaCharacter\.OrcaEnergy"
#    期望：5 行原样（CounterPatch / RefillPatch / BurstPatch / Burst / CounterPatch.EnergyBox）
#    基线对照：构件用户类型 129 个，编译器生成 100 个，合计 229 个

# 3) 部署（仅在 1) 通过后）
dotnet build "$Repo\OrcaCharacter.csproj" -v:m --nologo
#    期望：出现 "[Orca] 复制 dll / json / pdb → d:\steam\steamapps\common\Slay the Spire 2\mods\奥卡角色-Orca"

# 4) 游戏内：查日志 "[Orca] Harmony 补丁: N 成功 / M 失败"（M 必须为 0，N 必须与改动前一致）
#           查 "[Orca] 构建标记 = ..." 确认版本；再人工过 4.4 步骤 4 的 5 条外观路径
```

---

## 附：本次实测记录（可复现）

| 实测项 | 命令 / 方法 | 结果 |
|---|---|---|
| 文件行数 | Python 按 LF 切分 + 末行 | 602 行（601 LF / 601 CR，无 BOM） |
| 全工程规模 | 同上，遍历 `OrcaCharacterCode/**/*.cs` | **67 文件 / 9,512 行**（比 `docs/代码分类清单.md:9` 的"约 9,459 行"多 53 行：该清单用的是 `Get-Content` 口径，**漏算每个文件最后一行**——`Get-Content` 实测 8,795 行，差异即由此而来） |
| 类型清单 | `ilspycmd -l c` 反编译自建 dll | 本文件贡献 4 顶层 + 1 嵌套，全名如上 |
| 构件类型总数 | 同上 | 229（用户 129 + 编译器生成 100） |
| 命名空间 | `grep '^namespace '` | 67/67 全部 `OrcaCharacter` |
| 补丁属性 | 全工程 `[HarmonyPatch]` 扫描 | 46 处属性 / 44 个类型携带（35 顶层 + 9 嵌套）；本文件 3 处，均列 0 |
| 编译项来源 | `dotnet msbuild OrcaCharacter.csproj -getItem:Compile` | 67 项，**全部 `DefiningProjectName = Microsoft.NET.Sdk.DefaultItems`**（SDK 默认 glob，csproj 无显式 Include）⇒ **新增 `.cs` 不需要改 csproj** |
| 基线编译 | `dotnet build OrcaCharacter.sln -t:Rebuild -p:CustomModsPath=$env:TEMP\orca-verify-baseline` | **0 错误 / 0 警告 / exit 0**，4.23 秒，产出 dll+json+pdb |
| 改道是否生效 | 比对游戏 mods 目录时间戳 | mods 内 dll 保持 20:07:48，本次构建 20:25:16 ⇒ **未触碰游戏目录 ✔** |
| git 状态 | `git status -sb` / `git rev-parse HEAD` | `## main` 干净；HEAD = `eaa87a0`；本文件 blob = `d46742dd…` |
| 重命名前例 | `git log --name-status --diff-filter=RC` | `9aab57a` 的 69 个文件移动**全部 `R100`** ⇒ 本仓库 rename 识别正常 |
| 工具可用性 | `dotnet --version` / `ilspycmd` / `python` | dotnet **9.0.315**；ilspycmd **8.2.0**；python 自带运行时 |
| 路径不写死 | `dotnet msbuild -getProperty:Sts2Path,ModsPath,CustomModsPath,GodotPath` | 全部由 `Sts2PathDiscovery.props` 注册表/Steam 自动发现解析出来，**仓库内无本机绝对路径**（`GodotPath` 在 gitignore 的 `local.props` 里） |

**本文件没有做的事**：没有修改任何 `.cs`；没有改 csproj；没有跑 `publish`（因此 `.pck` 未被触碰）；
没有向游戏 mods 目录写入任何东西（全程 `-p:CustomModsPath=$env:TEMP\...`）。
