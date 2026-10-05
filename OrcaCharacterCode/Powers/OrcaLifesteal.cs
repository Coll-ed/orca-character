using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
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

    public override async Task AfterDamageGiven(
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
            if (dealer == null || dealer != Owner) return;

            // ② 必须来自**打出的攻击牌**
            //    ★★ 2026-09-23 用户口径改版：原来只认【嗜血龙剑】，现在**任何攻击牌**都能吸血
            //       （"用魔典打出魔剑后，其他攻击牌打出按 25% 比例回复生命"）。
            //       仍然要求 cardSource 非空 ⇒ "由牌打出的"才算，纯 power/环境伤害不吸。
            if (cardSource == null) return;

            // ③ 必须是攻击、且真的造成了**未被格挡**的伤害
            if (!props.IsPoweredAttack()) return;
            if (result.UnblockedDamage <= 0) return;
            if (Amount <= 0) return;

            // ★★ 2026-09-23 用户口径（最终版）：
            //    「吸血是**只有被龙剑类型的伤害卡牌**打出才会**消耗**并获得**额外倍率**，
            //      其他卡牌打出**不消耗**」
            //    ⇒ 分两档：
            //      · **龙剑类型**（嗜血龙剑 / 嗜血魔剑 —— 魔剑体系那两张）⇒ 额外倍率 50%（向上取整），**并消耗 1 层**；
            //      · 其他攻击牌 ⇒ 基础 25%（向下取整、最低 1），**不消耗层数**。
            //    ⚠️ 旧的"单敌 ⇒ 50%"覆盖机制（PercentOverride）已被取代 ⇒ 已删除。
            bool isSword = cardSource is OrcaBloodSword or OrcaBloodBlade;

            // ★★ 2026-10-06 用户裁定：**代码服从文案** —— 游戏内文案写的是
            //    「[gold]龙剑类型[/gold]（嗜血龙剑 / 嗜血魔剑）→ 伤害的 50%（向上取整）× 当前层数，
            //      **一口气结清所有层数**」
            //    而原来是"50% 只算一次 + 减 1 层"（旧口径，与上面的文案不一致 ✗ —— 用户实测
            //    "叠了 3 层龙剑打出后只消耗了一层"就是这个不一致暴露出来的）。
            //    现按文案改为：
            //      · 回复量 = ceil(未被格挡伤害 × 50%) × **当前层数**
            //      · **一次性清空全部层数**（Remove ⇒ 层数归零、Power 消失）
            //    ⚠️ 已知代价（如实记录）：同一张牌产生**多次伤害实例**时（例如狂躁的嗜血魔剑打全体），
            //      只有**第一次**实例能吃到层数 —— 第一次就把层数结清了。这是"一口气结清"口径的
            //      必然结果，不是缺陷；若日后要改成"每个目标各结清一次"，得改口径而不是改这里。
            int layers = (int)Amount;                        // 先取层数（下面结算过程中会被清空）

            int healed;
            string which;
            if (isSword)
            {
                healed = (int)Math.Ceiling(result.UnblockedDamage * MaxBladePercent / 100.0) * layers;
                which = $"龙剑类型 {MaxBladePercent}%（向上取整）× {layers} 层";
            }
            else
            {
                healed = Math.Max(MinHeal, (int)Math.Floor(result.UnblockedDamage * Percent / 100.0));
                which = $"其他攻击牌 {Percent}%（向下取整，最低 {MinHeal}）";
            }

            if (healed <= 0) return;

            var creature = Owner;
            int before = creature.CurrentHp;
            await CreatureCmd.Heal(creature, healed, true);
            OrcaLog.Info($"[Orca] 吸血触发：{which}，未被格挡伤害 {result.UnblockedDamage} → 回复 {healed} 点生命"
                     + $"（{before} → {creature.CurrentHp}）"
                     + (isSword ? $"，一口气结清 {layers} 层" : "，**不消耗层数**"), 2);

            // ④ ★ 只有**龙剑类型**才消耗层数，且是**一口气结清全部层数**（用户 2026-10-06 裁定）
            if (isSword) await PowerCmd.Remove(this);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 吸血结算出错（不影响伤害）：{ex.Message}", 2);
        }
    }
}