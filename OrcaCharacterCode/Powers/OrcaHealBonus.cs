using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★ 焚文的「额外治疗加成」（用户口径演进）。
///
///     <para>2026-09-16 原版：<i>"获得 50% 额外治疗加成，若消耗卡牌还会获得额外 5% 的治疗加成于本回合"</i>。</para>
///     <para>★ 2026-09-23 改版：<i>"1 费，打出时获得 **20%** 的生命回复效率，每烧一张牌获得 **10%** 的生命回复效率，敲后固有"</i>
///     —— 常驻降到 20%，但每烧一张从 5% 提到 **10%**（烧牌越多越猛）。</para>
///
///     <para>
///     <c>Amount</c> = 常驻加成（20）；<c>_tempPercent</c> = 本回合因消耗卡牌累加的临时加成。
///     实际生效值走 <see cref="TotalPercent" />。
///     </para>
///
///     <para>
///     ⚠️ 原版**没有**"修改治疗量"的 Hook（<c>Hook</c> 里只有 <c>AfterRestSiteHeal</c>，那是休息点专用）
///     ⇒ 只能 patch <see cref="CreatureCmd.Heal" /> 的 Prefix，按 <c>ref amount</c> 放大。
///     治疗入口唯一（实机与 <c>OrcaRelic</c> 的回复都走它），所以这里能覆盖全部治疗。
///     </para>
/// </summary>
public sealed class OrcaHealBonusPower : PowerModel
{
    /// <summary>每次消耗卡牌追加的加成（%）。★ 2026-09-23：5 → **10**。</summary>
    public const int PerExhaustPercent = 10;

    public override PowerType Type => PowerType.Buff;

    /// <summary>可叠层 —— 语义是"治疗加成百分比"，不是层数。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    ///     每次**消耗卡牌** → 本场战斗再 +<see cref="PerExhaustPercent" />%。
    ///
    ///     <para>★★ <b>2026-10-05 修「图标的数字没跟着变」</b>（用户实测：
    ///     「焚文是不是代码实际运行有效果？但是图标的数字没跟着变?」—— **就是这个问题**）。
    ///     原先烧牌加成存在**另一个私有字段</b> <c>_tempPercent</c> 里，靠 <c>TotalPercent = Amount + _tempPercent</c>
    ///     参与结算 ⇒ **效果真的生效，但图标上的数字永远只显示 <c>Amount</c>（常驻 20）**，
    ///     玩家看不到它涨。</para>
    ///
    ///     <para><b>为什么改成直接涨 <c>Amount</c></b>（反编译 <c>PowerModel</c> 实据）：
    ///     <list type="bullet">
    ///       <item>浮窗文案用它：<c>locString.Add("Amount", Amount)</c>；</item>
    ///       <item>引擎确实另留了一个 <c>public virtual int DisplayAmount =&gt; Amount</c> 可以只改"显示值"——
    ///         但那样**图标变了、浮窗还是旧的**（两处不一致）⇒ 不用它；
    ///         让 <c>Amount</c> 本身成为真值，**图标 / 浮窗 / 结算三处同源**；</item>
    ///       <item>⚠️ <c>Amount</c> 的 setter 是 <c>private set</c>（编译器实测 <c>CS0200</c>）⇒
    ///         改写要用**引擎自己的公开入口** <c>PowerCmd.ModifyAmount(...)</c>
    ///         （它是 <c>Task&lt;int&gt;</c>，内部会走 <c>SetAmount</c> 并通知 UI）。</item>
    ///     </list></para>
    ///
    ///     <para>★ 累计范围＝**本场战斗**（用户口径 2026-09-23：「焚文烧牌不是本回合，而是本场战斗了」），
    ///     随 Power 一起在战斗结束时消失，所以直接写进 <c>Amount</c> 不会跨战斗泄漏。</para>
    /// </summary>
    public override async Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        try
        {
            // 用引擎的公开入口涨量（applier＝自己；这不是卡牌效果，cardSource 传 null）
            await PowerCmd.ModifyAmount(choiceContext, this, PerExhaustPercent, Owner, null);

            // 日志读的是改完之后的 Amount（不依赖 ModifyAmount 的返回值语义）
            OrcaLog.Info($"[Orca] 焚文：消耗 {card.Id.Entry} → 治疗加成 +{PerExhaustPercent}%"
                     + $"（当前共 {Amount}%）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 焚文·消耗钩子出错：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     ★ 把治疗加成接到**所有治疗**上：patch <c>CreatureCmd.Heal(Creature, decimal, bool)</c> 的 Prefix，
///     用 <c>ref amount</c> 放大。只对"身上带 <see cref="OrcaHealBonusPower" /> 的那个生物"生效。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), "Heal")]
internal static class OrcaHealBonusPatch
{
    private static void Prefix(Creature creature, ref decimal amount)
    {
        try
        {
            if (creature == null || amount <= 0) return;
            var power = creature.GetPower<OrcaHealBonusPower>();
            if (power == null) return;

            // ★★ 2026-10-07 权威（<c>work/奥卡卡包集/备注.txt</c> L12）：<i>"吸血…**不享受治疗加成**"</i>
            //    ⇒ 吸血那一路的治疗原样放行，不放大（闸门由 OrcaLifestealHeal 标注，
            //      两个生产端见其类摘要：吸血结算 / 魔剑单敌自愈）。
            if (OrcaLifestealHeal.InFlight)
            {
                OrcaLog.Info($"[Orca] 治疗加成：{creature.Name} 的 {amount} 点治疗来自【吸血】"
                           + " ⇒ 按权威**不加成**（吸血不享受治疗加成）", 2);
                return;
            }

            // ★ 2026-10-05：加成现在就是 Amount 本身（烧牌直接涨 Amount ⇒ 图标/浮窗/结算同源）
            int pct = power.Amount;
            if (pct <= 0) return;

            decimal boosted = Math.Ceiling(amount * (100 + pct) / 100.0m);
            OrcaLog.Info($"[Orca] 治疗加成 {pct}%：{amount} → {boosted}", 2);
            amount = boosted;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 治疗加成 patch 出错（按原值治疗）：{ex.Message}", 2);
        }
    }
}