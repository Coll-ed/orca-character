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
    /// <summary>每次消耗卡牌追加的临时加成（%）。★ 2026-09-23：5 → **10**。</summary>
    public const int PerExhaustPercent = 10;

    /// <summary>★ 本场战斗内因消耗卡牌累加的加成（2026-09-23 起**不再每回合清零**）。</summary>
    private int _tempPercent;

    public override PowerType Type => PowerType.Buff;

    /// <summary>可叠层 —— 但语义是"常驻加成百分比"，不是层数。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>实际生效的总加成（%）= 常驻 + 本回合临时。</summary>
    public int TotalPercent => (int)Amount + _tempPercent;

    /// <summary>每次**消耗卡牌** → 本回合 +5%（用户口径）。</summary>
    public override Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        try
        {
            _tempPercent += PerExhaustPercent;
            OrcaLog.Info($"[Orca] 焚文：消耗 {card.Id.Entry} → 本回合治疗加成 +{PerExhaustPercent}%"
                     + $"（当前总加成 {TotalPercent}%）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 焚文·消耗钩子出错：{ex.Message}", 2);
        }
        return Task.CompletedTask;
    }

    // ★★ 2026-09-23 用户口径：「焚文烧牌不是本回合，而是**本场战斗**了，不然太弱了」
    //   ⇒ 原 `AfterPlayerTurnStart` 里"新回合清零 _tempPercent"的逻辑**整段删除**：
    //     烧牌加成现在与常驻加成一样，累计到**本场战斗结束**（随 Power 一起消失）。
    //     字段名保留 `_tempPercent` 以免动其它引用，语义已变成"本场累计的烧牌加成"。
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

            int pct = power.TotalPercent;
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