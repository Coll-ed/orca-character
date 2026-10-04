using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **熔渊枯骨**（3 费 · 能力 · 稀有 · 己方）—— 获得【生死一线】。
///
///     <para>【生死一线】（<see cref="OrcaMoltenBonePower" />）：你造成的【焚烧】每次触发只消耗一半层数，
///     且改为对场上**所有人**（含自己）造成伤害。属"卡包 2"内容。</para>
///
///     <para><b>本文件是「重建源码树」的第 5 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(CardKeyword[])(object)</c> 数组转换与被数字化的枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Rare=4</c>、<c>TargetType.Self=1</c>、
///     <c>CardKeyword.Innate=3</c>（敲后**固有** ✓ 与规格「敲后固有」一致）。</para>
/// </summary>
public sealed class OrcaMoltenBone : OrcaCard
{
    /// <summary>【生死一线】每次施加的层数。</summary>
    private const int MoltenBoneStacks = 1;

    /// <summary>无色：不改变能量球形态。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    public OrcaMoltenBone()
        : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner.Creature;
        await PowerCmd.Apply<OrcaMoltenBonePower>(ctx, creature, MoltenBoneStacks, creature, this, false);
        OrcaLog.Info("[Orca] 熔渊枯骨：获得【生死一线】—— 焚烧只消耗一半层数，且波及场上所有人（含自己）");
    }

    /// <summary>敲后带**固有**（用户口径与规格一致：敲后固有）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, new[] { CardKeyword.Innate });
}
