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
            await Burst(choiceContext, combat, consumeStacks: true, why: "回合结束");
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
        // ⚠️ 取战斗状态用 DebugOnlyGetState —— 红莲淬在**玩家回合内**打出，它此时就是当前战斗状态 ✓
        //    （实据：ilspy 反编译 sts2.dll，CombatManager 里**没有**公开的 State 属性，
        //      只有 StateTracker（其内部状态不公开）与这个方法。）
        //    ⚠️ 边界显式：拿不到就如实报"不在战斗中"，不静默 return。
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

        await Burst(ctx, combat, consumeStacks: false, why: "红莲淬");
        Log.Info($"[Orca] 红莲淬：立刻触发 {n} 个【焚烧】（不消耗层数）", 2);
        return n;
    }

    /// <summary>数场上还有几个带【焚烧】的存活单位。
    ///
    ///     <para>★★ 2026-10-05 <b>用户裁定 A4-b</b>：权威「将当前**场上所有**存在的【焚烧】立刻触发一次」
    ///     ⇒ "场上所有"＝**双方都算**（原先写死 <c>CombatSide.Enemy</c>，比我方/召唤物身上的焚烧漏掉了）。</para>
    ///
    ///     <para>API 实据（<c>ICombatState</c>）：<c>IReadOnlyList&lt;Creature&gt; Creatures</c>
    ///     —— <i>"Get all creatures in the combat on all sides."</i></para>
    ///
    ///     <para>⚠️ 只改**来源集合**（谁身上的焚烧会炸）；**伤害目标**仍按权威「敌人全体会受到伤害」
    ///     取敌方 —— 两件事不要混。</para>
    /// </summary>
    private static int CountBurning(ICombatState combat)
        => combat.Creatures
                 .Count(c => !c.IsDead && (c.GetPower<OrcaBurnPower>()?.Amount ?? 0) > 0);

    /// <summary>
    ///     焚烧的**唯一**结算实现：取快照 → （可选）扣层 → 每个炸开者波及所有带焚烧者。
    ///
    ///     <para><paramref name="consumeStacks" /> = <c>true</c> 用于回合结束（消耗层数），
    ///     <c>false</c> 用于红莲淬（无消耗触发）。</para>
    /// </summary>
    /// <param name="choiceContext">选择上下文。</param>
    /// <param name="combat">战斗状态（由调用方给 —— 回合结束那条路用 <c>Owner.CombatState</c>）。</param>
    /// <param name="consumeStacks"><c>true</c> 用于回合结束（消耗层数），<c>false</c> 用于红莲淬（无消耗触发）。</param>
    /// <param name="why">日志里的触发来源。</param>
    private static async Task Burst(
        PlayerChoiceContext choiceContext,
        ICombatState combat,
        bool consumeStacks,
        string why)
    {
        // ★★ 2026-10-05 用户裁定 A4-b：来源集合＝**场上所有**（双方都算），照权威
        //    「将当前场上所有存在的【焚烧】立刻触发一次」。
        //    API 实据：ICombatState.Creatures = "Get all creatures in the combat on all sides."
        //    ⚠️ 别把这里和下面"伤害波及谁"混起来 —— 这里管**谁炸开**，下面管**炸到谁**，两者规则不同：
        //       带【生死一线】(molten) ⇒ 场上所有人（**含没带焚烧标记的敌人**）；否则 ⇒ 只有带焚烧者。
        //       （2026-10-05 用户订正原话：「熔渊枯骨打出后，就是对没有标记的敌人也可以造成伤害」）
        List<Creature> burning = combat.Creatures
            .Where(c => !c.IsDead && (c.GetPower<OrcaBurnPower>()?.Amount ?? 0) > 0)
            .ToList();
        if (burning.Count == 0) return;

        // ① 先取快照（结算过程中层数会被清空）
        var snapshot = burning
            .Select(c => (Target: c, Stacks: (int)(c.GetPower<OrcaBurnPower>()?.Amount ?? 0)))
            .Where(t => t.Stacks > 0)
            .ToList();
        if (snapshot.Count == 0) return;

        // ★ A3（2026-10-01）：【生死一线】（熔渊枯骨）—— 口径原文见 OrcaPowers2.cs L143-151：
        //   "你造成的【焚烧】每次触发只消耗 50% 层数，【焚烧】现在变成对场上所有人（包括奥卡）造成伤害"
        //   ★ 2026-10-04 恢复真判据（原来是 /*TOGGLE-OFF-A3*/ bool molten = false; 硬编码短路
        //     ⇒ 熔渊枯骨永远不生效，而 OrcaMoltenBonePower.IsActive 全工程只有定义、无调用点）。
        //   判据来源 = 现成的 OrcaMoltenBonePower.IsActive(Creature?)（单一来源，不另写一份判断）。
        //   ⚠️ 只认**玩家自己**身上有没有这个 Power：权威写的是"**你造成的**【焚烧】"，
        //      若按"场上任意生物"取，联机里任一玩家带【生死一线】就会改写所有人的焚烧结算。
        bool molten = OrcaMoltenBonePower.IsActive(combat.PlayerCreatures.FirstOrDefault());
        if (molten) Log.Info("[Orca] 焚烧结算：【生死一线】生效 ⇒ 只消耗 50% 层数 + 波及场上所有人（含奥卡）", 2);

        // ⚠️ 日志必须**如实**说明这一批"炸到谁"：molten 时打的是场上所有人（含没带焚烧的敌人），
        //    若照旧写成"带焚烧的敌人"，实机排查会把范围误判成"只打带焚烧的"✗（本次订正的原因）
        string scopeText = molten ? "场上所有人（含未带焚烧者）" : "带焚烧者";

        // ② 消耗层数（红莲淬走"无消耗"⇒ 整段跳过）
        if (consumeStacks)
        {
            foreach (var (target, _) in snapshot)
            {
                var p = target.GetPower<OrcaBurnPower>();
                if (p == null) continue;
                if (molten)
                {
                    // 口径："只消耗 50% 层数" ⇒ 移除一半（**向下取整**，剩余层数保留 ✓）
                    // 依据：权威 卡牌说明2.txt:28「每次触发只消耗50%（具体数值向下取整）层数」
                    //   例：5 层 ⇒ 扣 2 留 3（此前写成 Ceiling ⇒ 扣 3 留 2，与权威相反 ✗）
                    // 来源：2026-10-05 用户裁定「按 work 权威改」（清单条目 A1）
                    // API 实据（ilspy 反编译 sts2.dll）：PowerCmd.ModifyAmount(ctx, power, offset) : Task<int> ✓
                    int half = p.Amount / 2; // 整数除法＝对非负层数向下取整（原为 Math.Ceiling，已按权威订正）
                    // 完整签名实据（ilspy）：ModifyAmount(ctx, power, offset, Creature? applier, CardModel? cardSource, bool silent=false) ✓
                    if (half > 0) await PowerCmd.ModifyAmount(choiceContext, p, -half, null, null);
                }
                else
                {
                    await PowerCmd.Remove(p);
                }
            }
        }

        // ③ 每个"炸开"的源，按【生死一线】决定**波及谁**（两个分支见下）—— 用户口径
        foreach (var (src, stacks) in snapshot)
        {
            List<Creature> alive;
            if (molten)
            {
                // ★★ 2026-10-05 用户订正：「熔渊枯骨打出后，就是对**没有标记的敌人**也可以造成伤害」
                //    ⇒ 这里取**敌方全体**（**不看**有没有焚烧标记）+ 玩家侧全体 —— 不是"只打带焚烧的"。
                //    ⚠️ 这条与上面的 A4-b 是**两件事**：A4-b 管"谁炸开"，这里管"炸到谁"。
                // 口径："对场上所有人（包括奥卡）造成伤害" ⇒ 敌方全部存活单位 + 玩家侧全部存活单位。
                // ★ 用 ICombatState.PlayerCreatures（实据：ilspy 反编译 sts2.dll，
                //   ICombatState 属性 "Get all the player creatures in the combat."）
                //   而不是 GetCreaturesOnSide(CombatSide.Player) —— 后者是**按侧**取，
                //   多玩家时会把队友算进去；权威说的是"场上所有人（包括奥卡）"。
                alive = combat.GetCreaturesOnSide(CombatSide.Enemy)
                    .Concat(combat.PlayerCreatures)
                    .Where(c => !c.IsDead).ToList();
            }
            else
            {
                // 没打熔渊枯骨 ⇒ 照卡面文案（powers.json 的 ORCA_BURN_POWER.description：
                // 「对每个带焚烧的敌人造成（层数）点伤害」）⇒ **只打带焚烧的**。
                alive = snapshot.Select(t => t.Target).Where(c => !c.IsDead).ToList();
            }
            if (alive.Count == 0)
            {
                // ★ A2 修复（2026-10-01）：原来这里是 break ✗ —— 一旦某个 src 炸完后场上
                //   已无存活目标，**整个循环就被中断**，后面的 src 连日志都不打 ⇒
                //   实机上表现为"残血敌人看似没触发" ✓ 改为 continue 让每个 src 都单独走完 ✓
                                // ★★★ 2026-10-05 用户裁定 A3b：**由 break 改为 continue**。
                //   权威（卡牌说明1.txt:47 备注）明确要求"防止残血先结算死亡导致满血结算少算一次"；
                //   而原来的 `/*TOGGLE-OFF-A2*/ break;` 会在**某次炸开后目标全死时直接跳出整个循环**
                //   ⇒ 后面的焚烧**不再结算**＝正是那句"少算一轮" ✗
                //   （同一行原本的注释就已写着"改为 continue"，代码与注释不符 ⇒ 现按注释与权威改回）
                //   continue：只跳过**本次已死**的目标，其余照常结算 ✓
                //   ⚠️ 与上面"波及谁"的范围口径**不冲突**：那管"打谁"（molten 时是场上所有人，
                //      含没带焚烧的敌人），这里管"不因死亡漏算"。
                continue;
            }

            // ★★ 2026-10-05：把本批编号发给"特殊死亡台词"的快照（**必须在造成伤害之前**）——
            //    挂点理由与批次语义见 OrcaDeathQuotes 的类注释。
            var batchId = OrcaDeathQuotes.NoteBurstSnapshot(combat);

            // ★★ 2026-10-05：把"这次出伤属于焚烧"这件事**夹在 Damage 前后**（try/finally 保证闸门一定落下）——
            //    死亡台词侧靠它才能证明"玩家是被这次焚烧烧死的"，而不是被敌人打死的 ✓
            //    ⚠️ 判定必须在**出伤期间**（Kill 的前置）完成：引擎在 Kill 体内就建结束画面并写文案，
            //       等 Damage 返回之后再判"玩家 IsDead"已经太晚（文案早就写成引擎原文了）✗
            OrcaDeathQuotes.BeginBurnDamage(batchId);
            try
            {
                await CreatureCmd.Damage(
                    choiceContext,
                    alive,
                    (decimal)stacks,
                    ValueProp.Unblockable | ValueProp.Unpowered,
                    src);
            }
            finally
            {
                OrcaDeathQuotes.EndBurnDamage();
            }

            Log.Info($"[Orca] 焚烧结算（{why}）：{src.Name} 的 {stacks} 层炸开 → 波及 {alive.Count} 个{scopeText}", 2);
        }
    }
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