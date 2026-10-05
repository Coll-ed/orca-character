using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★ 嗜血魔剑 —— **嗜血龙剑的先古版**。
///
///     <para><b>获得方式</b>：原版遗物「<b>古老牙齿</b>」（<c>ARCHAIC_TOOTH</c>，拾起时把一张初始卡牌变成先古版）
///     ⇒ 把「嗜血龙剑 → 嗜血魔剑」注册进它的映射（见 <see cref="OrcaAncientToothPatch" />）。</para>
///
///     <para><b>★ 2026-10-04 按用户口径【整类重写】</b>（原文取自 <c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c>）：
///     <code>
///     嗜血魔剑
///     1费，永恒，敲后固有，先古
///     狂躁
///     消耗最大生命30%（具体数值），获得1层吸血，并附加全额失去生命（具体数值）点伤害
///     当被狂躁打出时，对所有敌人造成附加数值（具体数值）点伤害，
///     若只对一名敌人造成伤害，恢复（敌人受到的生命伤害）点生命
///     </code></para>
///
///     <para><b>本次删掉的旧逻辑</b>（用户："这张先古迭代了很多次但我一直没管，比如红莲强化魔剑都是很久之前的初版"）：
///     <list type="bullet">
///       <item>✗ <b>红莲淬对魔剑的倍率强化</b> —— 旧版把 <c>OrcaCrimsonTemperPower.Multiplier</c> 乘进狂躁伤害并
///         <c>Consume()</c> 掉档数。用户裁定：**彻底断开**（红莲淬只做它自己的事）。</item>
///       <item>✗ <b>「只对一名敌人 ⇒ 吸血倍率提升」</b> —— 旧版去改共享 Power 的倍率（先是 <c>PercentOverride</c>，
///         后来改成读 <c>OrcaLifestealPower.MaxBladePercent</c>），那段现在只剩一句日志。
///         用户裁定：**改成本牌自己回血**，理由是"吸血我打算再整一个流派" ⇒ 魔剑不再牵扯吸血机制。</item>
///       <item>✗ 旧口径「消耗**当前**生命 25%」⇒ 权威是「消耗**最大**生命 30%」。</item>
///     </list></para>
///
///     <para><b>与龙剑的三处差别</b>（重写后仍成立）：
///     <list type="number">
///       <item>失去生命按**最大生命** 30%（龙剑按它自己的比例、且基数不同）；</item>
///       <item>附加的是**全额**失去生命（龙剑只附加一半）；</item>
///       <item>主动打出时**额外叠 1 层吸血**。</item>
///     </list></para>
///
///     <para>⚠️ 用户口径里**没有**提到"击杀 +生命上限"（龙剑有）⇒ 本牌**不实现**该条 ✓</para>
///
///     <para>★★ <b>2026-10-05 改继承 <see cref="OrcaFrenzyCard" /></b>（原先直接继承 <see cref="OrcaCard" />）：
///     权威卡面明写本牌带「<b>狂躁</b>」，而用户 2026-10-05 把狂躁定义为
///     <i>"狂躁标签又自带了2端代码：奇巧，消耗联动（如战鼓）"</i>
///     ⇒ 凡是狂躁卡就该由 <see cref="OrcaFrenzyCard" /> 统一提供这三件事：
///     ① 回合结束自动打出；② 被丢弃 ⇒ 回抽牌堆（奇巧）；③ 被消耗 ⇒ 回抽牌堆（消耗联动）。
///     原先本类自己<b>重复实现</b>了 ①②（<c>HasTurnEndInHandEffect</c> + <c>OnTurnEndInHand</c>），
///     于是它<b>拿不到</b>③ —— 同一个"狂躁"在两处各写一份，正是踩坑指南坑 3-2「同一份数据有多个版本」。
///     ⇒ 现在两份剑共用同一个基类，改动只写一处。</para>
/// </summary>
public sealed class OrcaBloodBlade : OrcaFrenzyCard
{
    /// <summary>本场战斗内累计的伤害附加（与龙剑同一套写法：是字段，不是 DynamicVars）。</summary>
    private int _bonus;

    /// <summary>
    ///     主动打出时失去**最大生命**的百分比。权威口径：<i>"消耗最大生命30%（具体数值）"</i>。
    ///     ⚠️ 旧代码这里写的是"当前生命 25%" —— 已按用户裁定更正。
    /// </summary>
    private const int LifePercent = 30;

    /// <summary>
    ///     单敌时的**吸血倍率**（%）。文案里写 <c>{LifestealPercent:diff()}</c>。
    ///
    ///     <para>★★ 这里有个**文字游戏**，用户 2026-10-04 亲口说明：
    ///     <i>"这个其实玩文字游戏，卡面介绍这么说，因为你想想实际就是对单有一个额外100%吸血倍率，
    ///     换算数值其实就是我们代码里的生命恢复"</i>。</para>
    ///
    ///     <para>⇒ 推导：吸血 = 伤害 × 倍率；倍率 100% ⇒ 回血 = 伤害本身，
    ///     与"恢复（该敌人受到的生命伤害）点生命"**完全等价** ✓
    ///     ⇒ 所以实现走 <see cref="HealFromSingleTarget" />（本牌自己回血），
    ///     **不去动共享的 <see cref="OrcaLifestealPower" />** —— 用户还要单独做吸血流派，别在这里缠死。</para>
    /// </summary>
    private const int SingleTargetLifestealPercent = 100;

    /// <summary>★ 形态标签：魔剑体系（打出时能量球切成魔剑形态）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    /// <summary>「永恒」＝ 原版 <c>CardKeyword.Eternal</c>。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Eternal };

    /// <summary>★ 1 费 · 技能 · **先古 Ancient** · 目标是自己。</summary>
    public OrcaBloodBlade() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self) { }

    /// <summary>本牌**没有任何自己的伤害**（基础伤害 0），所以不放 <c>DamageVar</c>。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    /// <summary>
    ///     本牌此刻会失去的生命值 = <c>floor(最大生命 × 30%)</c>。
    ///     **单一来源**：卡面文案（<see cref="AddExtraArgsToDescription" />）与出牌结算（<see cref="OnPlay" />）共用它，
    ///     避免"卡面显示 30%、实际扣 25%"这类不一致。
    /// </summary>
    /// <remarks>
    ///     取不到生物时（百科 / 牌库浏览）返回 0 —— 那时没有 Owner，属正常情况而非错误。
    /// </remarks>
    private int LifeLossOf(Creature? creature)
        => creature == null ? 0 : (int)Math.Floor(creature.MaxHp * LifePercent / 100.0);

    /// <summary>卡面文案的动态变量 + 关键词标签置顶（狂躁 / 永恒）。</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int lifeLoss = 0;
        try
        {
            lifeLoss = LifeLossOf(Owner?.Creature);
        }
        catch (Exception ex)
        {
            // 百科里看卡面时还没有 Owner ⇒ 按 0 显示（引擎对规范模型访问 Owner 会抛守卫异常）
            Log.Warn($"[Orca] 魔剑卡面数字：取最大生命失败（按 0 显示）：{ex.Message}", 2);
        }

        // ★★ 必须注入 DynamicVar：文案里带格式化器（{Key:diff()}）的占位符若缺一个，
        //    SmartFormat 会报 `No suitable Formatter` ⇒ **整条卡面**回退成未格式化的原文。
        //    （2026-10-04 已因此踩过两次：睥睨缺 DrawCount、血焰剑鞘缺 Percent。）
        description.Add(new DynamicVar("LifePercent", (decimal)LifePercent));
        description.Add(new DynamicVar("HpLoss", (decimal)lifeLoss));   // 失去的生命（具体数值）
        description.Add(new DynamicVar("Bonus", (decimal)_bonus));      // 当前累计附加 ⇒ 狂躁伤害
        description.Add(new DynamicVar("Dealt", (decimal)_bonus));      // 同上的别名（文案用词）
        // ★ 文案里的 {LifestealPercent:diff()}。它的**数值效果**就是下面 HealFromSingleTarget 的回血量
        //   （吸血倍率 100% ⇔ 回血 = 伤害本身），见 SingleTargetLifestealPercent 的注释。
        description.Add(new DynamicVar("LifestealPercent", (decimal)SingleTargetLifestealPercent));
        // ★★ 2026-10-05 补上「狂躁」（用户实测：「魔剑的狂躁标签没有在卡面介绍体现」）：
        //    本牌类摘要已写明 —— 权威卡面**明写本牌带「狂躁」**，且用户把狂躁定义为
        //    「狂躁标签又自带了2端代码：奇巧，消耗联动（如战鼓）」⇒ 凡是狂躁卡卡面就该有这个词。
        //    而这里此前只拼了 {状态} 与「永恒」⇒ 卡面**看不到狂躁** ✗
        //
        //    ⚠️ 待办（单一来源）：这个「狂躁」目前是**字面量**。若别的狂躁卡也各拼一份，
        //    就违反"同一份数据只有一处定义"——应当抽到 OrcaKeyword 里做一个 FrenzyTag
        //    （与 EternalTag 同一套做法）。本轮先按最小改动补上可见性。
        description.Add("KeywordTags",
            $"[gold]{OrcaSwordState.Title}[/gold]。\n[gold]狂躁[/gold]。\n[gold]永恒[/gold]。");
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ══════════ 狂躁（回合结束自动打出 / 被召唤出来直接打出）⇒ 全体敌人伤害 ══════════
        // ★ 用基类的 IsFrenzyPlay（与龙剑同一套判据）：它会**消费**魔典标记，所以只调一次。
        bool isFrenzy = IsFrenzyPlay(play);
        bool viaCodex = ConsumeViaCodex();
        if (isFrenzy)
        {
            await OnFrenzyPlay(ctx, play);
            return;
        }

        // ══════════ 主动打出 / 魔典打出 ⇒ 最大生命 30% 换全额附加 + 1 层吸血 ══════════
        if (viaCodex)
            Log.Info("[Orca] 嗜血魔剑：本次由**龙族魔典**打出 → 走正常使用的强化（不是狂躁的全体伤害）", 2);

        var me = Owner.Creature;
        int lifeLoss = LifeLossOf(me);
        if (lifeLoss <= 0)
        {
            Log.Info("[Orca] 嗜血魔剑·主动打出：算出的生命代价为 0，本次不结算（附加不变）", 2);
            return;
        }

        // 生命"失去"（非伤害）⇒ 直接设当前生命，绕开伤害管线（与龙剑同套写法）。
        // ⚠️ 代价是**最大生命的百分比**，可能超过当前生命 ⇒ 引擎侧按 0 兜底（不能扣成负数）。
        int newHp = Math.Max(0, me.CurrentHp - lifeLoss);
        await CreatureCmd.SetCurrentHp(me, newHp);

        _bonus += lifeLoss;                      // ★ 全额附加（龙剑只附加一半）
        Log.Info($"[Orca] 嗜血魔剑·主动打出：失去生命 {lifeLoss}（最大生命 {me.MaxHp} × {LifePercent}%）"
                 + $" → 伤害附加 +{lifeLoss}（累计 {_bonus}），本次不造成伤害", 2);

        // ★ 额外获得 1 层吸血（权威口径："获得1层吸血"）
        await PowerCmd.Apply<OrcaLifestealPower>(ctx, me, 1m, me, this);
        Log.Info("[Orca] 嗜血魔剑：获得 1 层【吸血】", 2);

        OrcaCardUi.Refresh(this);                // 卡面数字立刻重绘
    }

    /// <summary>
    ///     狂躁：对**所有敌人**造成（伤害附加）点伤害。（基类 <see cref="OrcaFrenzyCard.OnFrenzyPlay" /> 的实现。）
    ///
    ///     <para>★ 2026-10-04 重写：**不再乘红莲淬倍率**（用户裁定"彻底断开" —— 那是很久之前的初版设计）。</para>
    ///
    ///     <para>★ 第五条口径的实现：**若只对一名敌人造成伤害** ⇒ 恢复（该敌人受到的生命伤害）点生命。
    ///     注意这里是"本牌自己回血"，**不碰吸血机制**（用户："吸血我打算再整一个流派"）。
    ///     "受到的生命伤害"取的是**未格挡伤害**（<c>UnblockedDamage</c>），即真正掉血的那部分。</para>
    /// </summary>
    protected override async Task OnFrenzyPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;
        if (combat == null)
        {
            Log.Warn("[Orca] 魔剑·狂躁：不在战斗中，本次不结算", 2);
            return;
        }

        int aliveEnemies = combat.Enemies.Count(e => !e.IsDead);
        int damage = _bonus;                     // ★ 不再有倍率

        if (damage <= 0)
        {
            Log.Info("[Orca] 魔剑·狂躁：附加为 0（没先主动打出过）⇒ 本次不造成伤害", 2);
            return;
        }

        // ★ 单敌时先记下它的当前生命 —— 攻击后取差值，就是它**真正掉掉的血**
        //   （比"预期伤害"精确：自动扣掉格挡、减伤等所有因素；
        //     也不用 DamageResult —— 那个类型只从引擎钩子给到 Power，攻击指令的 Execute 不返回它。）
        var soleTarget = aliveEnemies == 1 ? combat.Enemies.FirstOrDefault(e => !e.IsDead) : null;
        int hpBefore = soleTarget?.CurrentHp ?? 0;

        try
        {
            var attack = DamageCmd.Attack((decimal)damage).FromCard(this, play)
                .TargetingAllOpponents(combat)
                .WithHitFx("vfx/vfx_attack_slash", null, null);

            await attack.Execute(ctx);

            Log.Info($"[Orca] 嗜血魔剑·狂躁：对 {aliveEnemies} 个敌人各造成 {damage} 点伤害（附加 {_bonus}）", 2);

            // ★ 击杀敌人 +生命上限（权威卡牌说明1.txt「嗜血魔剑」末行，逐字）：
            //   "击杀敌人+2生命上限，击杀精英+3生命上限，击杀BOSS＋５生命上限"
            //   ⇒ **分档按房间档位**，档位与数值的单一来源是 OrcaKillReward（本实现原先漏了这条）。
            var kills = attack.Results.SelectMany(r => r).Count(r => r.WasTargetKilled);
            if (kills > 0)
            {
                int perKill = OrcaKillReward.MaxHpFor(Owner);
                int gain = kills * perKill;
                await CreatureCmd.GainMaxHp(Owner.Creature, gain);
                Log.Info($"[Orca] 嗜血魔剑击杀 {kills} 个敌人 → 生命上限 +{gain}"
                         + $"（{OrcaKillReward.Describe(Owner)}，每个 +{perKill}）", 2);
            }

            // ★ 只对一名敌人造成伤害 ⇒ 恢复（该敌人受到的生命伤害）点生命
            if (soleTarget != null)
                await HealFromSingleTarget(ctx, Math.Max(0, hpBefore - soleTarget.CurrentHp));
        }
        catch (Exception ex)
        {
            Log.Error($"[Orca] 魔剑·狂躁结算抛异常（本次没造成伤害）：{ex}", 2);
        }
    }

    /// <summary>
    ///     单敌狂躁的收尾：恢复该敌人**真正掉掉的血**（攻击前后生命之差）。
    ///
    ///     <para>治疗量上限 = 缺失的生命（<c>最大生命 − 当前生命</c>）—— 显式夹取，
    ///     不让 <c>SetCurrentHp</c> 收到超过上限的值（边界要显式）。</para>
    /// </summary>
    private async Task HealFromSingleTarget(PlayerChoiceContext ctx, int dealt)
    {
        var me = Owner.Creature;
        if (dealt <= 0)
        {
            Log.Info("[Orca] 嗜血魔剑·狂躁（单敌）：敌人未受到生命伤害 ⇒ 本次不回复", 2);
            return;
        }

        int missing = Math.Max(0, me.MaxHp - me.CurrentHp);
        int heal = Math.Min(dealt, missing);
        if (heal <= 0)
        {
            Log.Info($"[Orca] 嗜血魔剑·狂躁（单敌）：生命已满 ⇒ 本次不回复（本可回复 {dealt}）", 2);
            return;
        }

        await CreatureCmd.SetCurrentHp(me, me.CurrentHp + heal);
        Log.Info($"[Orca] 嗜血魔剑·狂躁（单敌）：恢复 {heal} 点生命"
                 + $"（敌人受到的生命伤害 {dealt}，缺失 {missing}）", 2);
    }

    /// <summary>敲后追加**固有**（用户口径："龙剑和魔剑都是敲后固有"）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, CardKeyword.Innate);
}
