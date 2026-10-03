# 银龙奥卡 · Slay the Spire 2 角色 mod —— 求助：**一打出卡牌就卡死**

> 现象 100% 稳定复现，二分已做多轮。**请帮我判断"打牌"这条链路。**

## 一、环境

| 项 | 值 |
|---|---|
| 游戏 | Slay the Spire 2（Steam 版，分支 **public-beta**） |
| 程序集 | `Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll`（Godot 4.5 + .NET 9） |
| 本 mod | 自研角色 mod「银龙奥卡」，C# / .NET 9 / Harmony ⇒ 产物 `<mod>.dll` + `<mod>.pck` + `<mod>.json` |
| 依赖 | **BaseLib**（Steam Workshop `3737335127`） |
| 编译 | `dotnet 9.0.315`，引用 `sts2.dll` / `GodotSharp.dll` / `0Harmony.dll` / `BaseLib.dll` |

## 二、现象

- **过回合完全正常** ✓（回合开始补能量、外框特效、敌方回合都正常）
- **一"打出卡牌"就卡死** ✗ —— 画面冻住、**进程还在**（Godot 主线程死锁形态）
- **无异常、无堆栈、无弹窗** ✗
- **百科大全正常** ✓

## 三、已做的二分（每次只换一个变量）

| 测试 | 结果 |
|---|---|
| **官方 dll（188,416 B）+ 同一套 pck** | ✅ **完全正常** ⇒ 问题在我们重编的 dll ✗ |
| **最纯净环境**（去掉其它 mod） | ❌ 仍卡死 ⇒ **不是 mod 冲突** |
| 去掉"注册自检"类 | ❌ 仍卡死 |
| 修 `No suitable Formatter`（龙剑战况浮窗 SmartFormat，报错 2 次 → **0 次** ✓） | 报错消失，**卡死依旧** |
| 修 `OrcaEnergyBurst.SweepRing` 的 `TweenProperty` 起点 `Nil`（`Type mismatch … Nil and float`）<br>（该报错**每回合都出现，而"过回合不冻"** ⇒ 很可能只是噪声） | ❌ 仍卡死 |
| `Orca : CharacterModel` → `CustomCharacterModel` + `[CustomID("ORCA")]`（按 BaseLib wiki 接入注册体系） | ❌ 仍卡死（且引入角色选择界面多出几项皮肤的副作用） |
| 与官方 dll **逐文件 diff**（反编译两份 dll 后比对） | 文件集合仅差 1 处：缺 `OrcaNirvanaSelfHarmPatch` ⇒ **已按官方补齐** ✓；其余"内容不同"绝大多数是**编译器闭包/状态机编号偏移**的假差异 ✓ |

## 四、日志特征（每次一样）

```
[INFO] Player 1 playing card ORCA_STRIKE (targeting …)
[INFO] [Orca] 打出 ORCA_STRIKE（手动）→ 目标=…，当前能量=2
[INFO] [Orca] 打击效果：对 … 造成 6 点伤害
[MeleeDebug][AnimPatch] …（攻击动画走原版 ✓）
[FastWait] … → 原版放行
（此后无任何输出 ⇒ 冻住）
```

- **打任何一张牌都会**（连最基础的 `ORCA_STRIKE`「打击」也是）
- **冻在 `OnPlay` 之后**：`OnPlay` 里的日志**全部打出来了**，冻在其后
- 正常退出会有 `resources still in use at exit`；卡死时**没有**这一行

## 五、希望得到的帮助

1. "打出卡牌"这条链路（`OnPlay` → 结算 → `AfterCardPlayed` → 卡牌移动/消耗 → UI 刷新）里，**哪些环节可能造成 Godot 主线程静默死锁**？
2. 有没有已知的「自定义卡 + Harmony patch」在**出牌路径**上造成死锁的模式？
3. 有没有办法**让这种冻结留下痕迹**？（关键路径加日志无效 —— 冻结点在日志之外；Godot 会吞托管异常）
4. 是否与 `CardPileCmd` / `CardSelectCmd` / `AfterCardPlayed` 这类**异步钩子**的死锁有关？

## 六、如何编译

```powershell
# 1) 依赖 dll 不在本仓库（版权原因），请自备：
#    sts2.dll / GodotSharp.dll / 0Harmony.dll  ← 游戏目录 data_sts2_windows_x86_64\
#    BaseLib.dll                              ← 创意工坊 2868840\3737335127\BaseLib\
# 2) 改 Directory.Build.props 的 Sts2GameDir / Sts2WorkshopDir（支持同名环境变量覆盖）
# 3) dotnet build src/OrcaCharacter.csproj -c Release
```
