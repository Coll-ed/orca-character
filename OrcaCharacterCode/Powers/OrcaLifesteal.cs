using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     ★★「吸血」——**龙族魔典**的联动效果（用户口径 2026-09-17 最终定稿）。
///
///     <para>
///     ★★★ 关键归属：<b>吸血是魔典的逻辑，不是魔剑（龙剑）的逻辑</b>。
///     叠层发生在 <see cref="OrcaDragonCodex.OnPlay" /> 里（"IF 卡牌带有【魔剑】标签 → 获得一层吸血"），
///     **不是**挂在龙剑身上，也不是"任何魔剑卡打出都叠" —— 必须是**魔典打出的**才算。
///     </para>
///
///     <para>
///     结算口径（用户原话）：<i>"IF 打出的卡牌带有【魔剑】and 造成伤害：回复对应生命"</i>
///     ⇒ 钩子照抄原版同类 power（官方 wiki：*"Think about what base game content does something similar;
///     something almost always exists"*）—— 参照 <c>MegaCrit.Sts2.Core.Models.Powers.ReaperFormPower</c>
///     的 <c>AfterDamageGiven</c>：
///       · 判定 <c>dealer == Owner</c>（是本玩家打出的）
///       · 判定 <c>cardSource is OrcaBloodSword</c>（带【魔剑】标签的那张牌）
///       · 判定是攻击、且**未被格挡**的伤害 &gt; 0
///       · 回复 <c>ceil(未格挡伤害 × 倍率)</c>（龙剑类型再 × 当前层数），然后按档位决定是否清层
///     </para>
///
///     <para>
///     ★★ <b>2026-10-06 用户口径（合计）</b>：<i>"回血是总伤害 × 倍率，总生命伤害已经包括了各个敌人结算"</i>
///     ⇒ <b>同一次出牌内</b>的所有伤害实例（多个敌人 / 多次命中）先把**实际打进生命的伤害**
///     （<c>DamageResult.UnblockedDamage</c>）**合计**起来，出牌结束后**只结算一次**回血 ——
///     不再"每个伤害实例各回一次"。
///     实现：<see cref="AfterDamageGiven" /> 只累计，<see cref="AfterCardPlayed" /> 结算
///     （引擎实据：<c>Hook.AfterCardPlayed</c>（<c>Hook.cs:422-434</c>）在出牌结束后对监听模型
///     依次调 <c>AfterCardPlayed</c> / <c>AfterCardPlayedLate</c>；引擎自己的注释也写"通常用前者"）。
///     </para>
/// </summary>
public sealed class OrcaLifestealPower : PowerModel
{
    /// <summary>
    ///     转化比例（25%）—— **一般攻击牌**用这一档，**向下取整、最低 1 点**。
    ///     文案里的数字由它派生，改一处两处都变。
    /// </summary>
    public const int Percent = 25;

    /// <summary>
    ///     ★★ **龙剑类型**打出时的倍率（50%）—— **向上取整**，且**一口气结清全部层数**。
    ///     用户口径 2026-09-23：「在魔剑打出的吸血倍率是 50%（向上取整）」
    ///     + 「吸血是只有被**龙剑类型**的伤害卡牌打出才会**消耗**并获得**额外倍率**」。
    ///     ★ 2026-10-06 用户裁定「代码服从文案」：回复量再 **× 当前层数**，层数**一次性清空**
    ///     （原来是"50% 一次 + 减 1 层"，与游戏内文案「× 当前层数，一口气结清所有层数」不符 ✗）。
    ///     "龙剑类型" = 魔剑体系那两张攻击牌（嗜血龙剑 / 嗜血魔剑）。
    /// </summary>
    public const int MaxBladePercent = 50;

    /// <summary>★ **其他攻击牌**吸血的**最低回复量**（1 点）—— 用户口径：「最低为 1」。**不消耗层数**。</summary>
    public const int MinHeal = 1;

    /// <summary>增益类。</summary>
    public override PowerType Type => PowerType.Buff;

    /// <summary>可叠层（每层对应"下一次【魔剑】攻击"）。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    // ══════════════════ 一次出牌的累计（用户口径 2026-10-06）══════════════════
    //   用户原话：「回血是总伤害 × 倍率，总生命伤害已经包括了各个敌人结算」
    //   ⇒ 同一次**出牌**内的所有伤害实例（多个敌人 / 多次命中）先合计，出牌结束后**只结算一次**。
    //   ⚠️ 这几个是**运行时状态**、不是配置；每次结算后清零（见 AfterCardPlayed）。

    /// <summary>本次出牌累计的**实际打进生命**的伤害（Σ 未被格挡伤害）。</summary>
    private int _pendingUnblocked;

    /// <summary>本次出牌里是否出现过**龙剑类型**（出现过就走 50% 档 + 一口气清层）。</summary>
    private bool _pendingIsSword;

    /// <summary>本次出牌累计的伤害实例数（用来判断"这次出牌有没有东西可结算"）。</summary>
    private int _pendingHits;

    /// <summary>
    ///     ★ **只累计、不结算**：把本次出牌造成的**实际生命伤害**记到账上，
    ///     等出牌结束（<see cref="AfterCardPlayed" />）再一次性回血。
    /// </summary>
    public override Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        try
        {
            // ① 必须是"我们这位玩家"打出的伤害
            if (dealer == null || dealer != Owner) return Task.CompletedTask;

            // ② 必须来自**打出的攻击牌**
            //    ★★ 2026-09-23 用户口径改版：原来只认【嗜血龙剑】，现在**任何攻击牌**都能吸血
            //       （"用魔典打出魔剑后，其他攻击牌打出按 25% 比例回复生命"）。
            //       仍然要求 cardSource 非空 ⇒ "由牌打出的"才算，纯 power/环境伤害不吸。
            if (cardSource == null) return Task.CompletedTask;

            // ③ 必须是攻击、且真的造成了**实际生命伤害**（未被格挡的那部分）
            if (!props.IsPoweredAttack()) return Task.CompletedTask;
            if (result.UnblockedDamage <= 0) return Task.CompletedTask;
            if (Amount <= 0) return Task.CompletedTask;

            // ④ 累计（**不在这里回血** —— 倍率要对"总伤害"整体乘一次，逐实例算不出来）
            //    ★★ 2026-09-23 用户口径的两档倍率，在下方 AfterCardPlayed 里一次算清。
            _pendingUnblocked += result.UnblockedDamage;
            _pendingIsSword |= cardSource is OrcaBloodSword or OrcaBloodBlade;
            _pendingHits++;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 吸血累计出错（不影响伤害）：{ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     ★★ <b>一次出牌结束 ⇒ 结算一次回血</b>（用户口径 2026-10-06：
    ///     <i>"回血是总伤害 × 倍率，总生命伤害已经包括了各个敌人结算"</i>）。
    ///
    ///     <para>两档（用户口径 2026-09-23）：
    ///     <list type="bullet">
    ///       <item><b>龙剑类型</b>（嗜血龙剑 / 嗜血魔剑）⇒ <c>ceil(总生命伤害 × 50%) × 当前层数</c>，
    ///         并**一口气结清全部层数**（2026-10-06 用户裁定"代码服从文案"）；</item>
    ///       <item><b>其他攻击牌</b> ⇒ <c>max(1, floor(总生命伤害 × 25%))</c>，**不消耗层数**。</item>
    ///     </list></para>
    /// </summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (_pendingHits == 0) return;                   // 这次出牌没有可结算的伤害

        int total = _pendingUnblocked;
        bool isSword = _pendingIsSword;
        _pendingUnblocked = 0;                           // 先清账：万一下面又触发钩子也不会重复计入
        _pendingIsSword = false;
        _pendingHits = 0;

        try
        {
            int layers = isSword ? (int)Amount : 0;      // 层数要在清层之前取

            int healed;
            string which;
            if (isSword)
            {
                healed = (int)Math.Ceiling(total * MaxBladePercent / 100.0) * layers;
                which = $"龙剑类型 {MaxBladePercent}%（向上取整）× {layers} 层";
            }
            else
            {
                healed = Math.Max(MinHeal, (int)Math.Floor(total * Percent / 100.0));
                which = $"其他攻击牌 {Percent}%（向下取整，最低 {MinHeal}）";
            }

            if (healed <= 0) return;

            var creature = Owner;
            int before = creature.CurrentHp;

            // ★★ 归墟（OrcaVoidReturnPower / OrcaVoidReturnBlockPatch）会拦下**一切**回复，
            //    而权威 2026-10-07 明文「**不拦截吸血**」⇒ 这一笔必须用闸门标出来。
            //    try/finally 配对：即使引擎在治疗里抛异常，闸门也一定会落下。
            OrcaLifestealHeal.Begin();
            try
            {
                await CreatureCmd.Heal(creature, healed, true);
            }
            finally
            {
                OrcaLifestealHeal.End();
            }
            OrcaLog.Info($"[Orca] 吸血触发（本次出牌合计）：{which}，总生命伤害 {total} → 回复 {healed} 点生命"
                     + $"（{before} → {creature.CurrentHp}）"
                     + (isSword ? $"，一口气结清 {layers} 层" : "，**不消耗层数**"), 2);

            // ★ 只有**龙剑类型**才清层，且是**一口气结清全部层数**（用户 2026-10-06 裁定）
            if (isSword) await PowerCmd.Remove(this);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 吸血结算出错（不影响伤害）：{ex.Message}", 2);
        }
    }
}