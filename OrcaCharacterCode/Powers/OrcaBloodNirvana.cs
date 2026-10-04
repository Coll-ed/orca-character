using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **浴血涅槃**（3 费 · 能力 · 稀有 · 己方，敲后 2 费）—— 受到致命伤害时用战斗临时生命上限换命。
///
///     <para>【浴血涅槃】（<see cref="OrcaBloodNirvanaPower" />）：当你受到致命伤害时，
///     消耗所有**战斗临时生命上限**，回复其中 50% 的生命；触发一次后消失。</para>
///
///     <para><b>本文件是「重建源码树」的第 6 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(IEnumerable&lt;CardKeyword&gt;)(object)</c> 数组转换与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Rare=4</c>、<c>TargetType.Self=1</c>、
///     <c>CardKeyword.Ethereal=2</c> —— 与规格「3费，银龙，**虚无**，金卡，能力牌，敲后2费」逐条吻合 ✓</para>
/// </summary>
public sealed class OrcaBloodNirvana : OrcaCard
{
    /// <summary>【浴血涅槃】每次施加的层数。</summary>
    private const int NirvanaStacks = 1;

    /// <summary>敲后费用减少量（3 → 2）。</summary>
    private const int UpgradeCostStep = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    /// <summary>虚无：回合结束时若还在手里则消耗。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };

    public OrcaBloodNirvana()
        : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner.Creature;
        await PowerCmd.Apply<OrcaBloodNirvanaPower>(ctx, creature, NirvanaStacks, creature, this, false);
        OrcaLog.Info("[Orca] 浴血涅槃：本回合受到致命伤害时，将消耗所有临时生命上限换命");
    }

    /// <summary>敲后：3 费 → 2 费。</summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-UpgradeCostStep);
}
