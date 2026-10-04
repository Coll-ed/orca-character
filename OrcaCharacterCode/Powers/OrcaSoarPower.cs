using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     自写的「翱翔」—— 烬血之翼专用。
///
///     <para><b>为什么不直接用原版翱翔</b>（反编译 <c>SoarPower</c> 实据）：
///     <c>public sealed class SoarPower</c> ⇒ 不能继承；
///     <c>StackType => PowerStackType.Single</c> ⇒ **不支持层数减少**。
///     而本卡规格要求「当奥卡受到敌人未被格挡的生命伤害时**减少一层**」⇒ 只能自写。</para>
///
///     <para><b>机制骨架参照原版「振翅」<c>FlutterPower</c></b>（ThievingHopper 的招式，可叠 5 层、
///     每挨一次伤害减一层）：<c>Counter</c> 层数 + <c>ModifyDamageMultiplicative</c> 给倍率 +
///     <c>AfterDamageReceived</c> 里 <c>PowerCmd.Decrement</c>。</para>
///
///     <para>★★ <b>但刻意不抄它的这一行</b>：
///     <code>if (!props.IsPoweredAttack()) return 1m;</code>
///     那是"**怪物**只挡玩家攻击牌"的口径。我们是**玩家**，要的是<b>一切伤害减半</b>
///     （用户口径：「怪物的所有伤害减少50%」= 照抄骨架、去掉这条守门）。
///     同理 <c>AfterDamageReceived</c> 里也不做 <c>IsPoweredAttack</c> 判断。</para>
///
///     <para>图标走 <see cref="OrcaPowerIconSkinPatch" />：本 Power 的 Id 被映射到原版翱翔图标。</para>
/// </summary>
public sealed class OrcaSoarPower : PowerModel
{
    /// <summary>减伤百分比（单卡数值，就地声明：50 = 受到的伤害减半）。</summary>
    private const decimal DamageDecreasePercent = 50m;

    public override PowerType Type => PowerType.Buff;

    /// <summary>
    ///     <c>Counter</c>：支持层数显示与减少。
    ///     （原版翱翔是 <c>Single</c>，减不了层 ⇒ 这是自写的第一个理由。）
    /// </summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("DamageDecrease", DamageDecreasePercent) };

    /// <summary>
    ///     ★ 一切伤害减半。
    ///
    ///     <para><b>刻意不写</b> <c>if (!props.IsPoweredAttack()) return 1m;</c>
    ///     —— 那一行只挡"攻击牌造成的伤害"，与本卡规格「为奥卡提供 50% 的减伤」不符。</para>
    /// </summary>
    public override decimal ModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner) return 1m;                 // 只管自己挨的伤害
        return DamageDecreasePercent / 100m;            // 50% ⇒ 0.5 倍
    }

    /// <summary>挨一次未格挡伤害 ⇒ 减少一层（归零则自动消失）。</summary>
    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner) return;
        if (result.UnblockedDamage == 0) return;        // 被完全格挡 = 没受伤，不减层

        await PowerCmd.Decrement(this);
    }
}
