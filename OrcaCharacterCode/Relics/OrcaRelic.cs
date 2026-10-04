using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     银龙血统（起始遗物）。规格（用户口径）：
///       ① 每次受到一次伤害 → 本场战斗内**临时**获得 floor(所受伤害 × 50%) 点生命上限（**不回复生命**）；
///       ② 每回合若生命值没有受到伤害 → 回复 ceil(最大生命值 × 2%) 点生命；
///       ③ 触发时要有**原版遗物的触发特效**（`RelicModel.Flash()`，public ✓，不用反射私有方法）；
///       ④ 触发时能量球形态切回**龙头**（用户点名的 A 方案）。
///
///     API 依据（都来自 `_api\\sts2.api.txt` + 反编译 `CreatureCmd`）：
///       · 钩子在 `AbstractModel` 上可覆写：`AfterDamageReceived(ctx, target, DamageResult, props, dealer, cardSource)` /
///         `AfterPlayerTurnStart(ctx, player)` / `AfterCombatEnd(CombatRoom)`；
///       · `CreatureCmd.GainMaxHp` **会顺带回血**（内部 `await Heal(...)`）⇒ 规格要"不回复生命"，
///         所以改用 `CreatureCmd.SetMaxHp(creature, 当前上限 + 加成)`（纯加上限）；
///       · `DamageResult.UnblockedDamage` = 实际掉的血（被完全格挡时为 0 ⇒ 不算"受到伤害"）；
///       · 遗物触发特效 = `RelicModel.Flash()`（public）。
/// </summary>
public sealed class OrcaBloodline : RelicModel
{
    /// <summary>受伤转血上限的比例（50%）。</summary>
    private const decimal DamageToMaxHpRatio = 0.5m;

    /// <summary>未受伤回合的回复比例（2%）。</summary>
    private const decimal HealRatio = 0.02m;

    /// <summary>
    ///     ★★ 上回合**是否被敌人打过** —— 只给 ②（回合回血）的"翻倍"判定用。
    ///
    ///     <para>⚠️ 与 ① 的判定**必须分开**：
    ///     ① 要含自残（卖血是启动燃料，走 <c>AfterCurrentHpChanged</c>）；
    ///     ② 只认**敌人打的**（自残不算"受到伤害"）。
    ///     理由：奥卡整套牌都在卖血自残，若自残也把"无伤"判掉，**翻倍永远拿不到**，
    ///     与用户"卖血获得临时血上限来启动"的设计直接对立。</para>
    ///
    ///     <para>挂点在 <see cref="AfterDamageReceived" /> —— 它签名里**带 <c>dealer</c>**，
    ///     而 <c>AfterCurrentHpChanged</c> 看不见伤害来源，所以只能挂这里。
    ///     另外自残走的是 <c>CreatureCmd.SetCurrentHp</c>（绕过伤害管线）⇒ 天然不会触发。</para>
    /// </summary>
    private bool _hitByEnemyThisTurn;

    /// <summary>
    ///     本场战斗累计的临时血上限 —— ★ 2026-09-17 起改为**共享池** <see cref="OrcaTempHp" />
    ///     （卡牌扩充第二版的【浴血涅槃】要消耗它、【栖途】要把它转成真实上限）。
    ///     这里不再自己存字段，避免两处记账对不上。
    /// </summary>
    private static decimal TempMaxHp => OrcaTempHp.Current;

    /// <summary>★ 本场战斗是否已经说过开场白（每场只说一次）。</summary>
    private bool _spokeCombatStart;

    /// <summary>
    ///     ★★ **本场战斗的基准生命上限**（战斗开始时记下，战斗结束照它还原）。
    ///
    ///     <para>⚠️ 为什么需要它 —— 2026-09-17 实机第二次 bug 的根因：
    ///     **`CreatureCmd.SetMaxHp` 对玩家生物过不了回合边界**。
    ///     实机日志实锤（同一个战斗内）：
    ///     <code>
    ///     掉血 16 → 本场临时血上限 +8（累计 +35，当前上限 94）
    ///     未受伤回合 → 回复 2 点生命（60/60）     ← 回合开始，上限被同步回持久值 60
    ///     掉血 12 → 本场临时血上限 +6（累计 +41，当前上限 66）  ← 60 + 6
    ///     …
    ///     战斗结束，临时血上限 -52 已还原（当前上限 27）        ← 79 - 52，永久亏 33
    ///     </code>
    ///     ⇒ 池子在累加、上限却每逢回合开始被打回基准。所以**还原绝不能做减法**，
    ///     必须**绝对写回基准值**（幂等，无论引擎有没有清过都不会亏）。</para>
    /// </summary>
    private decimal _combatBaseMaxHp;

    /// <summary>
    ///     ★ 重入闸门（审查发现的真实缺陷）：我们自己在钩子里调 <c>SetMaxHp</c> 会**再次**引发
    ///     "当前生命变化"（降上限时会截断当前生命）⇒ 二次触发本钩子 ⇒ 反复加血上限。
    ///     战斗结束还原上限那一步尤其危险。所以自己的操作期间屏蔽钩子。
    /// </summary>
    private bool _applying;

    public override RelicRarity Rarity => (RelicRarity)1;   // Starter

    /// <summary>基名（别处若按基名找图就用它）。</summary>
    protected override string IconBaseName => "orca_horn";

    /// <summary>小图标（战斗里那条遗物栏，80×80）＝ 我们自己带的图，不走原版遗物图集。</summary>
    public override string PackedIconPath => ImageHelper.GetImagePath("relics/orca_horn.png");

    /// <summary>大图标（遗物详情 / 图鉴，256×256）＝ 同一只角的放大版。</summary>
    protected override string BigIconPath => ImageHelper.GetImagePath("relics/orca_horn_big.png");

    /// <summary>轮廓图（不可用状态）＝ 同一张，后续有专门轮廓图再换。</summary>
    protected override string PackedIconOutlinePath => ImageHelper.GetImagePath("relics/orca_horn.png");

    /// <summary>
    ///     ① 只要**当前生命下降**就触发（用户口径："受到伤害就会触发遗物，**包括自残的**"）。
    ///
    ///     ★ 为什么挂在 <c>AfterCurrentHpChanged</c> 而不是 <c>AfterDamageReceived</c>：
    ///     我们的嗜血龙剑自残用的是 <c>CreatureCmd.SetCurrentHp(CurrentHp - n)</c>，
    ///     **完全绕过伤害管线** ⇒ 伤害钩子收不到（实测：自残不掉血上限）。
    ///     而"当前生命变化"这个钩子对**一切**掉血都生效（敌人伤害、自残、其它失去生命），
    ///     且被完全格挡时生命不变 ⇒ 自然不触发（符合原规格"受到伤害"）。
    ///     <paramref name="delta" /> 为负 = 掉血；治疗（正值）不触发。
    /// </summary>
    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_applying) return;                      // ★ 自己的 SetMaxHp 引发的回调，直接忽略
        try
        {
            var me = Owner?.Creature;
            if (me == null || creature != me) return;

            // ★★ 只在**战斗进行中**生效。
            //    非战斗期（开局初始化 / 读档恢复 / 战斗收尾）也会产生"当前生命变化"，
            //    那不是"受伤"；若照记会把池子灌大，而战斗结束是按**池子**还原上限的
            //    ⇒ 净效果就是**永久掉血上限**（2026-09-17 实机 bug：一局打完 60 → 34）。
            if (!CombatManager.Instance.IsInProgress) return;

            // ★ 2026-09-23：治疗（delta > 0）⇒ 回血台词；这里原来直接 return，先接一句再走。
            if (delta > 0)
            {
                if (CombatManager.Instance.IsInProgress) OrcaSpeech.SayEvent(me, "heal");
                return;
            }

            int lost = (int)Math.Floor(-delta);
            if (lost <= 0) return;                  // 治疗（正值）不触发；日志不再逐次刷屏（审查：降噪）

            // ★★ 2026-09-23：这里**不再**置 `_damagedThisTurn`。
            //    ①（受伤 → 本场临时上限）必须**含自残** —— 用户口径：
            //      "受到伤害就会触发遗物，包括自残的"、"卖血就是在本场战斗内获得临时生命上限而启动"
            //      ⇒ 自残是**启动燃料**，绝不能在这里判掉。
            //    ②（回合回血）的"受到伤害"是另一套判定，只认**敌人打的**，见 AfterDamageReceived。
            decimal bonus = Math.Floor(lost * DamageToMaxHpRatio);
            if (bonus <= 0) return;

            // ★★ 记账必须记「**实际生效**的增量」，不是"打算加多少"。
            //    反编译实据（`CreatureCmd.SetMaxHp`）：它 `return newMaxHp - oldMaxHp;`
            //    —— 真实增量由它算好返回；若上限被引擎钳制或被别的 mod 抑制，
            //    返回 0/偏小，此时**不能**把意图值记进池子，否则战斗结束会多扣。
            //    （注：`GainMaxHp` 内部也是调 `SetMaxHp`，两者是同一套机制。）
            _applying = true;
            decimal applied;
            try { applied = await CreatureCmd.SetMaxHp(me, me.MaxHp + bonus); }
            finally { _applying = false; }

            if (applied <= 0)
            {
                OrcaLog.Info($"[Orca] 银龙血统：掉血 {lost} 但上限未真正提高（实际 +{applied}）⇒ 不记账", 2);
                return;
            }

            OrcaTempHp.Add(applied);
            TriggerFx($"掉血 {lost} → 本场临时血上限 +{applied}（累计 +{TempMaxHp}，当前上限 {me.MaxHp}）");

            // ★ 受伤台词（用户 2026-09-17："受伤……都会额外说话"）
            //   分类上限 2 句/场 —— 受伤太频繁，不设上限会把开场白/狂躁的额度吃光。
            OrcaSpeech.SayCapped(me, VfxDuration.Short, "hurt", 2,
                "ORCA.banter.hurt.1", "ORCA.banter.hurt.2", "ORCA.banter.hurt.3",
                "ORCA.banter.hurt.4", "ORCA.banter.hurt.5", "ORCA.banter.hurt.6");
        }
        catch (Exception ex)
        {
            _applying = false;
            OrcaLog.Warn($"[Orca] 银龙血统·生命变化钩子出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★★ ② 的"是否受到伤害"判定 —— **只认敌人打的**（2026-09-23 新增）。
    ///
    ///     <para>反编译实据：<c>AbstractModel.AfterDamageReceived(choiceContext, target, result,
    ///     props, dealer, cardSource)</c> —— 签名里**带 <c>dealer</c>**，这是唯一能区分
    ///     "被敌人打"和"自己卖血"的钩子（<c>AfterCurrentHpChanged</c> 只有 creature + delta）。
    ///     原版遗物 <c>CentennialPuzzle</c> / <c>DemonTongue</c> / <c>SelfFormingClay</c>
    ///     都挂在这个钩子上，是标准写法。</para>
    ///
    ///     <para>①（受伤 → 临时上限）**不在这里**，仍在 <c>AfterCurrentHpChanged</c> ——
    ///     那一处要含自残。</para>
    /// </summary>
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        try
        {
            var me = Owner?.Creature;
            if (me == null || target != me) return Task.CompletedTask;
            if (dealer != null && dealer == me) return Task.CompletedTask;   // 自己打自己 ≠ 被敌人打
            if (result.UnblockedDamage <= 0) return Task.CompletedTask;      // 被完全格挡 = 没受伤
            _hitByEnemyThisTurn = true;

            // ★ 2026-09-23：事件类台词（用户："受到重击，格挡伤害…都有的"）。
            //   完全挡下（总伤 > 0 但一点没漏）⇒ blocked；一次漏掉的血够多 ⇒ heavyHurt。
            if (result.TotalDamage > 0)
            {
                if (result.UnblockedDamage <= 0)
                {
                    OrcaSpeech.SayEvent(me, "blocked");
                }
                else
                {
                    decimal heavy = Math.Max(15m, Math.Floor(me.MaxHp * 0.25m));
                    if (result.UnblockedDamage >= heavy) OrcaSpeech.SayEvent(me, "heavyHurt");
                }
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 银龙血统·受伤判定出错：{ex.Message}", 2);
        }
        return Task.CompletedTask;
    }

    /// <summary>② 回合开始：回 ceil(MaxHp × 2%)；上回合没被敌人打 ⇒ 翻倍（再发一份）。</summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (Owner == null || player != Owner) return;

            // ★★ 回合开始：① 第一回合记下**基准上限**；② 把「基准 + 临时池」**绝对写回**。
            //    引擎会在回合开始把上限同步回持久值 ⇒ 必须每回合重新施加一次，
            //    否则临时上限只在打出那一个回合内有效（实机第二个 bug）。
            //    用**绝对目标**而不是增量 ⇒ 幂等：引擎没清也不会翻倍，清了也会补回来。
            var self = Owner.Creature;
            if (_combatBaseMaxHp <= 0)
            {
                _combatBaseMaxHp = self.MaxHp;
                OrcaLog.Info($"[Orca] 银龙血统：本场基准生命上限 = {_combatBaseMaxHp}", 2);
            }

            decimal want = _combatBaseMaxHp + TempMaxHp;
            if (self.MaxHp != want)
            {
                if (TempMaxHp > 0)
                {
                    OrcaLog.Info($"[Orca] 银龙血统：回合开始重挂临时上限 "
                             + $"{self.MaxHp} → {want}（基准 {_combatBaseMaxHp} + 临时 {TempMaxHp}）", 2);
                }

                _applying = true;
                try { await CreatureCmd.SetMaxHp(self, want); }
                finally { _applying = false; }
            }

            // ★ 本场第一回合：清空说话计数 + 说一句开场白
            //   （用户口径 2026-09-17："奥卡战斗冒文字气泡"；气泡颜色见 Orca.SpeechBubbleColor）
            if (!_spokeCombatStart)
            {
                _spokeCombatStart = true;
                OrcaSpeech.ResetCombat();
                OrcaSpeech.SayOne(Owner.Creature, VfxDuration.Standard,
                    "ORCA.banter.combatStart.1", "ORCA.banter.combatStart.2", "ORCA.banter.combatStart.3",
                    "ORCA.banter.combatStart.4", "ORCA.banter.combatStart.5", "ORCA.banter.combatStart.6");
            }

            // ★★ 2026-09-23 用户口径改版：
            //    「每回合过去后，回复 2% **当前最大生命值**的生命，**若没有受到伤害则翻倍回复**」
            //    · 基础那份**无条件**发（挨打也发）—— 这是保底再生，解决"挨一下整回合白板"
            //    · 上回合**没被敌人打** ⇒ 再发一份（合计 2 份 ≈ 4%）
            //    · 分母是 `me.MaxHp`（**含本场临时上限**）⇒ 卖血把上限顶上去，回血自动跟着涨 = 启动
            if (_hitByEnemyThisTurn)
            {
                OrcaLog.Info("[Orca] 银龙血统：上回合被敌人打过 ⇒ 只回基础那份", 2);
            }

            var me = Owner.Creature;
            decimal perTurn = Math.Ceiling(me.MaxHp * HealRatio);
            decimal heal = _hitByEnemyThisTurn ? perTurn : perTurn * 2;
            if (heal > 0)
            {
                await CreatureCmd.Heal(me, heal, true);
                TriggerFx($"回合回血 {heal} 点"
                          + $"{(_hitByEnemyThisTurn ? "（挨打·基础）" : "（无伤·翻倍）")}"
                          + $"（{me.CurrentHp}/{me.MaxHp}）");
            }

            _hitByEnemyThisTurn = false;      // 新回合重新计数
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 银龙血统·回合钩子出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★ **击杀台词**（用户 2026-09-17："击杀敌人都会额外说话"）。
    ///
    ///     <para>挂点 = <c>AbstractModel.AfterDeath</c>（任何生物死亡都会广播）。
    ///     只认**敌人**死亡：<c>!creature.IsPlayer</c>；玩家自己死了不冒台词。
    ///     分类上限 2 句/场，与其它类别共享每场 4 句的总额度。</para>
    /// </summary>
    public override Task AfterDeath(
        PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        try
        {
            if (wasRemovalPrevented) return Task.CompletedTask;          // 死亡被阻止 ⇒ 没死成
            if (!CombatManager.Instance.IsInProgress) return Task.CompletedTask;
            if (creature == null || creature.IsPlayer) return Task.CompletedTask;

            var me = Owner?.Creature;
            if (me == null || me.IsDead) return Task.CompletedTask;

            OrcaSpeech.SayCapped(me, VfxDuration.Short, "kill", 2,
                "ORCA.banter.kill.1", "ORCA.banter.kill.2", "ORCA.banter.kill.3",
                "ORCA.banter.kill.4", "ORCA.banter.kill.5", "ORCA.banter.kill.6");
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 击杀台词出错：{ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    // ★ 2026-10-04 移除：原先这里 override AfterCombatVictory 调用
    //   OrcaReverseScaleSource.GrantOnBossVictory（BOSS 战胜利按 0.5 概率给逆鳞/睥睨）。
    //   用户口径：「BOSS 专属奖励卡牌还是没掉落，去掉专属奖励吧，回归肉鸽随机属性」
    //   ⇒ 掉落机制整体移除；逆鳞与睥睨已放回普通随机池（见 Pools.cs），
    //     通过常规战斗奖励 / 商店获得。此处不再 override，回归基类行为。

    /// <summary>③ 战斗结束：把本场临时加上去的血上限还原。</summary>
    public override async Task AfterCombatEnd(CombatRoom room)
    {
        try
        {
            decimal pool = TempMaxHp;
            var me = Owner?.Creature;
            decimal basis = _combatBaseMaxHp > 0 ? _combatBaseMaxHp : (me?.MaxHp ?? 0m);

            // ★★ **不做裸减法**：目标 = max(基准, 当前上限 - 池子)，并**以基准兜底**。
            //    - 临时上限还挂着（本回合刚加过）→ 当前 = 基准+池子 → 回到基准 ✓
            //    - 临时上限已被引擎清掉（回合开始同步过）→ 当前 = 基准 → 仍是基准 ✓（旧代码这里会亏）
            //    两种情况都不会低于基准 ⇒ 永久掉血上限在数学上不可能再发生。
            if (me != null && basis > 0)
            {
                decimal before = me.MaxHp;
                decimal target = Math.Max(basis, me.MaxHp - pool);
                if (me.MaxHp != target)
                {
                    _applying = true;               // ★ 同上：降上限会截断当前生命，屏蔽钩子重入
                    try { await CreatureCmd.SetMaxHp(me, target); }
                    finally { _applying = false; }
                }

                OrcaLog.Info($"[Orca] 银龙血统：战斗结束还原 —— 基准 {basis} / 临时池 {pool} / "
                         + $"{before} → {me.MaxHp}"
                         + (before == basis && me.MaxHp == basis ? "（临时上限已被引擎同步清掉，无需处理）" : ""), 2);
            }

            OrcaTempHp.Reset();
            _combatBaseMaxHp = 0;           // ★ 下场战斗重新记基准
            _hitByEnemyThisTurn = false;
            _spokeCombatStart = false;      // ★ 下场战斗重新说开场白
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 银龙血统·战斗结束还原出错：{ex.Message}", 2);
        }
    }

    /// <summary>统一的触发表现：原版遗物闪光特效 + 能量球形态切回龙头 + 日志。</summary>
    private void TriggerFx(string what)
    {
        try
        {
            Flash();                       // ★ 原版遗物触发特效（用户点名要"能看到遗物那里出现触发特效"）
            OrcaOrbIcon.ShowDragon();      // ★ 触发时能量球切回龙头形态
            OrcaLog.Info($"[Orca] 银龙血统触发：{what}", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 银龙血统触发表现出错：{ex.Message}", 2);
        }
    }
}