using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     **栖途**（3 费 · 银龙 · 能力牌 · 先古 · 己方；敲后 3 → 2 费）
///     —— 立刻铺一堵格挡**并结束这一回合**，然后开始【栖途】记账。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L42-46，★ 2026-10-07 全面重做）：
///     <i>"栖途 / 3费，银龙，先古牌，能力牌，敲后（选取遗物-欧洛巴斯之触会获得这张卡牌）/
///     立刻获得你最大生命值50%点的格挡，结束这一回合并获得【栖途】buff /
///     【栖途】：在没有受到敌人伤害（烧血自残不算）的情况下触发【银龙血统】时，获得1层再生，
///     战斗结束后根据再生层数转化为生命上限"</i>。</para>
///
///     <para>⚠️ <b>改版前是</b>「战斗结束后，把你 25%（敲后 50%）的战斗临时生命上限转化为真实生命上限」
///     —— 那套口径与它的实现（含 E4 跨局泄漏的修法）已整体作废。本版<b>不引入任何 static 状态</b>：
///     "敲没敲"由卡牌自己的持久状态决定（费用），记账全在 <see cref="OrcaHomesteadPower" /> 的实例上。</para>
///
///     <para>★ <b>敲后 = 3 → 2 费</b>：权威 L43 的「敲后」后面是空的，2026-10-07 用户裁定按
///     「3 费 → 2 费（推荐）」实现。</para>
///
///     <para>★ <b>三个动作的次序</b>：权威的语序是「格挡 → 结束这一回合 → 获得 buff」，
///     实现把「获得 buff」提到「结束这一回合」**之前**：引擎的 <c>PlayerCmd.EndTurn</c> 会把该玩家
///     标记为"已准备结束"，本张牌结算返回后战斗循环就推进到敌方回合 —— 记账 Power 若晚挂上去，
///     它的图标与"之后的触发"就会落到下一个回合去。</para>
///
///     <para>★ 枚举已查证：<c>CardType.Power=3</c>、<c>CardRarity.Ancient=5</c>（先古 ✓）、
///     <c>TargetType.Self=1</c>。</para>
/// </summary>
public sealed class OrcaHomestead : OrcaCard
{
    /// <summary>
    ///     打出时施加的 Power 层数 —— <b>引用 Power 侧的常量</b>（那 1 层只用于把 Power 挂上去，
    ///     真正计入换算的是之后的触发次数，见 <see cref="OrcaHomesteadPower.Triggers" />）。
    /// </summary>
    private const int Stacks = OrcaHomesteadPower.InitialStacks;

    /// <summary>
    ///     立刻获得的格挡 = **最大生命值的这个百分点**
    ///     （权威 L45「立刻获得你最大生命值50%点的格挡」）。
    ///
    ///     <para>★ <b>单一来源</b>：卡面 <c>{BlockPercent}</c> / <c>{Block}</c> 与出牌结算
    ///     共用本常量与 <see cref="BlockValue" /> ⇒ 不可能出现"卡面写 50%、实际按 30% 给"。</para>
    /// </summary>
    private const int BlockPercent = 50;

    /// <summary>百分数 ↔ 比例的分母（100 这个"百分数"单位本身的定义；唯一换算点用）。</summary>
    private const int PercentBase = 100;

    /// <summary>敲后的费用减少量（3 → 2）。</summary>
    private const int UpgradeCostStep = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    public OrcaHomestead()
        : base(3, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    /// <summary>
    ///     此刻会拿到的格挡 = <c>floor(当前最大生命 × 50%)</c>。
    ///
    ///     <para>取不到生物时（百科 / 牌库浏览 / 尚未挂载 Owner）返回 0 —— 那时没有 Owner，
    ///     属正常情况而非错误（与龙剑/魔剑卡面数字同一套写法）。</para>
    /// </summary>
    private int BlockValue()
    {
        var me = Owner?.Creature;
        return me == null ? 0 : (int)Math.Floor(me.MaxHp * BlockPercent / (double)PercentBase);
    }

    /// <summary>
    ///     卡面数字：<c>{BlockPercent}</c>（比例）与 <c>{Block}</c>（此刻的具体数值）。
    ///
    ///     <para>⚠️ 必须注入 <c>DynamicVar</c>：带格式化器的占位符只要缺一个键，SmartFormat 就报
    ///     <c>No suitable Formatter</c> ⇒ **整条卡面**回退成未格式化的原文（本项目已踩过四次）。</para>
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("BlockPercent", (decimal)BlockPercent));
        description.Add(new DynamicVar("Block", (decimal)BlockValue()));
        // ★ 转化口径的两个数字也由**唯一来源**（Power 的常量）注入，别在文案里再写一遍字面量。
        description.Add(new DynamicVar("RegenPerTrigger", (decimal)OrcaHomesteadPower.RegenPerTrigger));
        description.Add(new DynamicVar("MaxHpPerRegen", (decimal)OrcaHomesteadPower.MaxHpPerRegen));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var me = Owner.Creature;

        // ① 立刻获得"最大生命值 50%"点格挡（权威 L45）。
        //    属性用 ValueProp.Move —— 与「防御」牌同一档（卡牌给的格挡，反编译实据：
        //    OrcaDefend 的 BlockVar(5m, (ValueProp)8) 即 Move）。
        int block = BlockValue();
        if (block > 0)
        {
            await CreatureCmd.GainBlock(me, (decimal)block, ValueProp.Move, play, false);
            OrcaLog.Info($"[Orca] 栖途：立刻获得 {block} 点格挡"
                       + $"（最大生命 {me.MaxHp} × {BlockPercent}%）", 2);
        }
        else
        {
            // 边界显式：算不出格挡（生命上限为 0 / 拿不到生物）时**不静默**跳过。
            OrcaLog.Warn($"[Orca] 栖途：算出的格挡为 0（最大生命 {me.MaxHp}）⇒ 本次没有格挡");
        }

        // ② 获得【栖途】buff（放在结束回合之前，理由见类摘要）
        var power = await PowerCmd.Apply<OrcaHomesteadPower>(ctx, me, Stacks, me, this, false);
        if (power == null)
        {
            // 引擎只在"战斗收尾 / 目标不可接受 Power"时返回 null ⇒ 显式记 Warn，不静默。
            OrcaLog.Warn("[Orca] 栖途：引擎未返回 Power 实例 ⇒ 【栖途】本场未生效（不会记账、不会转化）");
        }

        // ③ 结束这一回合（权威 L45「结束这一回合」）—— 用引擎自己的"结束回合"入口。
        //    实据（反编译 sts2.dll）：同款卡 VoidForm.cs:26
        //    `PlayerCmd.EndTurn(base.Owner, canBackOut: false)` —— canBackOut: false = 不能再反悔，
        //    与点"结束回合"按钮走同一个入口（PlayerCmd.cs:279 SetReadyToEndTurn）。
        PlayerCmd.EndTurn(Owner, canBackOut: false);
        OrcaLog.Info("[Orca] 栖途：本回合就此结束（权威：结束这一回合）", 2);
    }

    /// <summary>
    ///     敲后：3 费 → 2 费。
    ///
    ///     <para>★ 旧实现这里是"什么都不做"（旧口径的强化点在**比例** 25% → 50%，而那个比例概念
    ///     已随本轮删除）⇒ 现在敲它才有收益，与用户裁定的「3 费 → 2 费」一致。</para>
    /// </summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-UpgradeCostStep);
}
