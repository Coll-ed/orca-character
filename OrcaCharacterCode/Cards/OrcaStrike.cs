using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     **打击**（1 费 · 攻击 · 基础 · 任一敌人）—— 银龙奥卡的起始牌（4 张）。
///
///     <para><b>本文件是「重建源码树」的第 4 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(IEnumerable&lt;DynamicVar&gt;)(object)</c> 数组转换，
///     以及被反编译成数字的枚举与伤害类型。</para>
///
///     <para>★ 枚举与伤害类型已逐条查证：
///     <c>CardTag.Strike=1</c>（打击带 Strike 标签）、<c>CardType.Attack=1</c>、
///     <c>CardRarity.Basic=1</c>、<c>TargetType.AnyEnemy=2</c>、
///     以及 <c>ValueProp.Move=8</c> ——
///     枚举自身的文档写明「Move = Attack damage from Attack cards」，正是打击该用的伤害类型。</para>
/// </summary>
public sealed class OrcaStrike : OrcaCard
{
    /// <summary>基础伤害。</summary>
    private const decimal BaseDamage = 6m;

    /// <summary>敲后伤害增量（6 → 9）。</summary>
    private const decimal UpgradeDamageStep = 3m;

    /// <summary>打击类标签（影响"打击"相关的联动）。</summary>
    protected override HashSet<CardTag> CanonicalTags => new() { CardTag.Strike };

    /// <summary>6 点攻击伤害（<see cref="ValueProp.Move" /> = 攻击牌造成的攻击伤害）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DamageVar(BaseDamage, ValueProp.Move) };

    public OrcaStrike()
        : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // 攻击牌必须有目标；没有就是调用方出错，显式抛出而不是静默空打
        ArgumentNullException.ThrowIfNull(play.Target, "cardPlay.Target");

        OrcaLog.Info($"[Orca] 打击效果：对 {play.Target.Name} 造成 {DynamicVars.Damage.BaseValue} 点伤害");

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, play)
            .Targeting(play.Target)
            .WithHitFx("vfx/vfx_attack_slash", null, null)
            .Execute(ctx);
    }

    /// <summary>敲后：伤害 6 → 9。</summary>
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(UpgradeDamageStep);
}
