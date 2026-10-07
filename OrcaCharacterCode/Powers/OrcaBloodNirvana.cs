using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     **浴血涅槃**（3 费 · 能力 · 稀有 · 己方，敲后**仍是 3 费**、回复比例 50% → 100%）——
///     受到致命伤害时用战斗临时生命上限换命。
///
///     <para>【浴血涅槃】（<see cref="OrcaBloodNirvanaPower" />）：当你受到致命伤害时，
///     消耗所有**战斗临时生命上限**，回复其中 50% 的生命；触发一次后消失。</para>
///
///     <para><b>本文件是「重建源码树」的第 6 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(IEnumerable&lt;CardKeyword&gt;)(object)</c> 数组转换与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Rare=4</c>、<c>TargetType.Self=1</c>、
///     <c>CardKeyword.Ethereal=2</c> —— 与规格「3费，银龙，**虚无**，金卡，能力牌，敲后100%比例恢复」逐条吻合 ✓
///     （★ 2026-10-07 权威改版：敲后从「3→2 费」改成「50% → 100% 比例」，**费用恒为 3**。）</para>
/// </summary>
public sealed class OrcaBloodNirvana : OrcaCard
{
    /// <summary>【浴血涅槃】每次施加的层数。</summary>
    private const int NirvanaStacks = 1;

    /// <summary>
    ///     本局生效的回复百分点（敲后 100%，否则 50%）—— 卡面数字、日志与结算共用此口径。
    ///
    ///     <para>★ 两个百分点数值**定义在 <see cref="OrcaBloodNirvanaPower" /> 里**，这里只**引用**
    ///     （纪律：同一个常量只定义一次）。</para>
    /// </summary>
    private int CurrentPercent => IsUpgraded
        ? OrcaBloodNirvanaPower.UpgradedPercent
        : OrcaBloodNirvanaPower.BasePercent;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    /// <summary>虚无：回合结束时若还在手里则消耗。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };

    public OrcaBloodNirvana()
        : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    /// <summary>
    ///     ★ 把回复比例与**具体数值**注进卡面（<c>ORCA_BLOOD_NIRVANA.description</c> 里的
    ///     <c>{HealPercent:diff()}%（{Heal:diff()}血）</c>）。
    ///
    ///     <para><b>为什么必须有这一段</b>（审计 §2.1.1）：卡面文案只有一个键
    ///     <c>{卡id}.description</c>，写死的数字**升级前后一模一样**；且带格式化器的占位符
    ///     只要缺一个键，SmartFormat 就报 <c>No suitable Formatter</c> ⇒ **整条卡面**回退成原文。</para>
    ///
    ///     <para><b>单一来源</b>：两个百分点（50 / 100）只定义在
    ///     <see cref="OrcaBloodNirvanaPower.BasePercent" /> / <see cref="OrcaBloodNirvanaPower.UpgradedPercent" />，
    ///     卡面读本类的 <c>CurrentPercent</c>、结算读 Power 实例自己的比例 ⇒ 两边不可能脱钩。
    ///     具体数值用同一比例乘当前临时上限并**同样向下取整**。</para>
    ///
    ///     <para>★ 具体数值按**当前**临时生命上限预览（用户卡面口径「XX%（具体数值）」，
    ///     同 <see cref="OrcaBloodForge" /> 的 <c>{MaxHpLoss}</c> 写法）——
    ///     真实触发时的数值取决于那一刻的池子，这里是"此刻会发生多少"的预告。</para>
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("HealPercent", (decimal)CurrentPercent));
        description.Add(new DynamicVar("Heal", (decimal)HealPreview()));
    }

    /// <summary>
    ///     此刻按临时上限预告的回血量 = <c>floor(当前临时上限 × 本局比例)</c>（与结算同口径）。
    ///     ★ 比例走 <see cref="CurrentPercent" /> ⇒ 卡面数字跟着敲没敲走（改前这里恒按 50% 算 ✗）。
    /// </summary>
    private int HealPreview()
    {
        var pool = OrcaTempHp.Current;
        return pool <= 0
            ? 0
            : (int)Math.Floor(pool * OrcaBloodNirvanaPower.RatioFromPercent(CurrentPercent));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var creature = Owner.Creature;
        var power = await PowerCmd.Apply<OrcaBloodNirvanaPower>(ctx, creature, NirvanaStacks, creature, this, false);

        // ★ 2026-10-07：比例按**本局这张卡**的敲没敲现算，写进该 Power 实例
        //   （同 OrcaVoidReturn.OnPlay / OrcaHomestead.OnPlay 的写法 —— 不引入任何跨局 static 状态）。
        //   Apply 返回 null（理论上：战斗收尾 / 层数为 0）⇒ 记 Warn，不静默。
        if (power != null)
        {
            power.SetHealPercent(CurrentPercent);
        }
        else
        {
            OrcaLog.Warn("[Orca] 浴血涅槃：出牌但引擎未返回 Power 实例 ⇒ 本场比例未写入（将按默认 50% 结算）");
        }

        OrcaLog.Info($"[Orca] 浴血涅槃：受到致命伤害时，消耗所有临时生命上限并回复其中 {CurrentPercent}% 的生命"
                   + "（触发一次后消失）");
    }

    /// <summary>
    ///     敲后：回复比例 50% → **100%**（权威 L2「敲后100%比例恢复」）。
    ///
    ///     <para>⚠️ 这里**刻意什么都不写**（与 <c>OrcaVoidReturn.OnUpgrade</c> / <c>OrcaHomestead.OnUpgrade</c>
    ///     逐字同构）：升级态是卡牌自己的持久状态，比例在**每次出牌**时由 <see cref="CurrentPercent" /> 现算
    ///     ⇒ 不需要、也不可以把"敲过了"记到任何 static 上。</para>
    ///
    ///     <para>★ 旧实现这里是 <c>EnergyCost.UpgradeBy(-1)</c>（敲后 2 费）—— 权威 2026-10-07 改版后
    ///     **费用不再变**，该改动已随本轮删除。</para>
    /// </summary>
    protected override void OnUpgrade() { }
}
