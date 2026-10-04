using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

/// <summary>
///     ★★「焚烧」—— 逐焰挂给敌人的 debuff（用户口径 2026-09-16）。
///
///     <para>原文：<i>"焚烧：敌方回合结束时，消耗所有层数造成（总层数）点伤害，敌人全体会受到伤害"</i>，
///     追问后明确为：<i>"带焚烧的敌人炸开时，会**波及到其他带焚烧的敌人**"</i>
///     ⇒ 不是"全体敌人"，而是**只在带焚烧的敌人之间扩散**。</para>
///
///     <para>触发时机照抄原版同类 debuff（反编译 <c>DoomPower</c> / <c>PoisonPower</c>）：
///     覆写 <c>AfterSideTurnEnd</c>，判定 <c>side == CombatSide.Enemy</c> 且自己参与了这一侧。</para>
///
///     <para>⚠️ <b>去重</b>：每个带焚烧的敌人都会各自收到一次钩子 ⇒ 必须只让"第一个带焚烧的敌人"
///     统一结算（<c>DoomPower.ShouldDoomTrigger</c> 用的就是这个模式），否则会炸 N 遍。</para>
/// </summary>
public sealed class OrcaBurnPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Enemy) return;
            if (CombatManager.Instance.IsOverOrEnding) return;
            if (Owner == null || Owner.IsDead) return;
            if (!participants.Contains(Owner)) return;

            var combat = Owner.CombatState;
            if (combat == null) return;

            // ★ 去重：只让"第一个带焚烧的敌人"执行一次
            List<Creature> burning = combat.GetCreaturesOnSide(CombatSide.Enemy)
                .Where(c => !c.IsDead && (c.GetPower<OrcaBurnPower>()?.Amount ?? 0) > 0)
                .ToList();
            if (burning.Count == 0) return;
            if (burning[0] != Owner) return;

            // 回合结束的结算 = 触发一次并**消耗层数**
            await Burst(choiceContext, consumeStacks: true, why: "回合结束");
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 焚烧结算出错（不影响其它流程）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★★ <b>红莲淬用</b>：立刻触发一次场上所有【焚烧】，**不消耗层数**。
    ///
    ///     <para>权威口径（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c>）：
    ///     <i>"将当前场上所有存在的【焚烧】立刻无消耗触发一次"</i>。</para>
    ///
    ///     <para>与 <see cref="AfterSideTurnEnd" /> 共用 <see cref="Burst" />（**单一来源**：
    ///     伤害公式、波及范围、A2/A3 开关语义都只有一份），只差 <c>consumeStacks</c> 一个参数。</para>
    /// </summary>
    /// <param name="ctx">选择上下文。</param>
    /// <returns>本次触发的"炸开"个数（0 = 场上没有焚烧）。</returns>
    internal static async Task<int> TriggerAllNow(PlayerChoiceContext ctx)
    {
        var combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat == null)
        {
            Log.Warn("[Orca] 红莲淬：不在战斗中，本次不触发", 2);
            return 0;
        }

        // 先数一下有没有可触发的（没触发时红莲淬要显式告诉玩家"什么都没发生"）
        int n = CountBurning(combat);
        if (n == 0)
        {
            Log.Info("[Orca] 红莲淬：场上没有任何【焚烧】⇒ 本次无效果", 2);
            return 0;
        }

        await Burst(ctx, consumeStacks: false, why: "红莲淬");
        Log.Info($"[Orca] 红莲淬：立刻触发 {n} 个【焚烧】（不消耗层数）", 2);
        return n;
    }

    /// <summary>数场上还有几个带【焚烧】的存活单位。</summary>
    private static int CountBurning(ICombatState combat)
        => combat.GetCreaturesOnSide(CombatSide.Enemy)
                 .Count(c => !c.IsDead && (c.GetPower<OrcaBurnPower>()?.Amount ?? 0) > 0);

    /// <summary>
    ///     焚烧的**唯一**结算实现：取快照 → （可选）扣层 → 每个炸开者波及所有带焚烧者。
    ///
    ///     <para><paramref name="consumeStacks" /> = <c>true</c> 用于回合结束（消耗层数），
    ///     <c>false</c> 用于红莲淬（无消耗触发）。</para>
    /// </summary>
    private static async Task Burst(PlayerChoiceContext choiceContext, bool consumeStacks, string why)
    {
        var combat = BurnAnchor;
        if (combat == null) return;

        List<Creature> burning = combat.GetCreaturesOnSide(CombatSide.Enemy)
            .Where(c => !c.IsDead && (c.GetPower<OrcaBurnPower>()?.Amount ?? 0) > 0)
            .ToList();
        if (burning.Count == 0) return;

        // ① 先取快照（结算过程中层数会被清空）
        var snapshot = burning
            .Select(c => (Target: c, Stacks: (int)(c.GetPower<OrcaBurnPower>()?.Amount ?? 0)))
            .Where(t => t.Stacks > 0)
            .ToList();
        if (snapshot.Count == 0) return;

        // ★ A3（2026-10-01）：【生死一线】（熔渊枯骨）—— 口径原文见 OrcaPowers2.cs L134-135：
        //   "你造成的【焚烧】每次触发只消耗 50% 层数，【焚烧】现在变成对场上所有人（包括奥卡）造成伤害"
        //   谓词用现成的 OrcaMoltenBonePower.IsActive(Creature?) ✓（它收到的是 Creature ✓）
        bool molten = false; /*TOGGLE-OFF-A3：判据暂时短路，验完恢复*/
        if (molten) Log.Info("[Orca] 焚烧结算：【生死一线】生效 ⇒ 只消耗 50% 层数 + 波及场上所有人（含奥卡）", 2);

        // ② 消耗层数（红莲淬走"无消耗"⇒ 整段跳过）
        if (consumeStacks)
        {
            foreach (var (target, _) in snapshot)
            {
                var p = target.GetPower<OrcaBurnPower>();
                if (p == null) continue;
                if (molten)
                {
                    // 口径："只消耗 50% 层数" ⇒ 移除一半（向上取整，剩余层数保留 ✓）
                    // API 实据（ilspy 反编译 sts2.dll）：PowerCmd.ModifyAmount(ctx, power, offset) : Task<int> ✓
                    int half = (int)Math.Ceiling(p.Amount / 2m);
                    // 完整签名实据（ilspy）：ModifyAmount(ctx, power, offset, Creature? applier, CardModel? cardSource, bool silent=false) ✓
                    if (half > 0) await PowerCmd.ModifyAmount(choiceContext, p, -half, null, null);
                }
                else
                {
                    await PowerCmd.Remove(p);
                }
            }
        }

        // ③ 每个"炸开"的敌人，伤害波及**所有带焚烧的敌人**（含自己）—— 用户口径
        foreach (var (src, stacks) in snapshot)
        {
            List<Creature> alive;
            if (molten)
            {
                // 口径："对场上所有人（包括奥卡）造成伤害" ⇒ 双方所有存活单位 ✓
                alive = combat.GetCreaturesOnSide(CombatSide.Enemy)
                    .Concat(combat.GetCreaturesOnSide(CombatSide.Player))
                    .Where(c => !c.IsDead).ToList();
            }
            else
            {
                alive = snapshot.Select(t => t.Target).Where(c => !c.IsDead).ToList();
            }
            if (alive.Count == 0)
            {
                // ★ A2 修复（2026-10-01）：原来这里是 break ✗ —— 一旦某个 src 炸完后场上
                //   已无存活目标，**整个循环就被中断**，后面的 src 连日志都不打 ⇒
                //   实机上表现为"残血敌人看似没触发" ✓ 改为 continue 让每个 src 都单独走完 ✓
                /*TOGGLE-OFF-A2*/ break;
            }

            await CreatureCmd.Damage(
                choiceContext,
                alive,
                (decimal)stacks,
                ValueProp.Unblockable | ValueProp.Unpowered,
                src);

            Log.Info($"[Orca] 焚烧结算（{why}）：{src.Name} 的 {stacks} 层炸开 → 波及 {alive.Count} 个带焚烧的敌人", 2);
        }
    }

    /// <summary>
    ///     触发器锚点：<see cref="Burst" /> 是静态方法，但需要 <c>CombatState</c>。</summary>
    /// <remarks>
    ///     ⚠️ 用静态入口时的取法：红莲淬在**玩家回合内**打出，此时
    ///     <c>CombatManager.Instance.DebugOnlyGetState()</c> 就是当前战斗状态 ✓
    ///     （回合结束那条路不走这里，它用 <c>Owner.CombatState</c> —— 已内联在调用处。）
    /// </remarks>
    private static ICombatState? BurnAnchor => CombatManager.Instance.DebugOnlyGetState();
}

/// <summary>
///     ★「回响」—— 焚卷入典给的"**下次【龙族魔典】额外打出次数**"。
///     层数 = 额外打出次数；由 <see cref="OrcaDragonCodex" /> 在打出时**消费掉**（见其 OnPlay）。
/// </summary>
public sealed class OrcaCodexEchoPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;
}