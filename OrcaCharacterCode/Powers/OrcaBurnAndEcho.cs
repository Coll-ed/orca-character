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
///     <para>★★ <b>2026-10-07 权威改版</b>（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c> L45-48）——
///     本轮把【焚烧】的三条规则钉死，逐条对应下面的实现：
///     <list type="number">
///       <item><b>先打护盾、再打生命</b>（权威「【焚烧】先攻击护盾再生命结算」）⇒ 伤害属性
///         <b>不带</b> <c>Unblockable</c>（带它＝无视格挡，与权威相反 ✗），见 <see cref="BurnDamageProps" />；</item>
///       <item><b>附加到已有【焚烧】的敌人 ⇒ 它自己立刻无消耗炸一次，且不波及他人</b>
///         ⇒ 见 <see cref="AfterPowerAmountChanged" />（只在"这次是加层"且"加之前它身上已经有层"时触发）；</item>
///       <item><b>【生死一线】（熔渊枯骨）改写结算</b>：附加即时触发<b>失效</b>、结算打<b>场上所有人</b>、
///         层数<b>完全不消耗</b>（权威「不再消失」）⇒ 见 <see cref="OrcaMoltenBonePower" /> 与 <see cref="Burst" />。</item>
///     </list></para>
/// </summary>
public sealed class OrcaBurnPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    ///     【焚烧】出伤用的伤害属性 —— <b>单一来源</b>：回合结束结算、附加即时触发两条路共用它。
    ///
    ///     <para>权威（<c>卡牌说明1.txt</c> L47）：「触发特效为灼伤的触发特效，<b>【焚烧】先攻击护盾再生命结算</b>」
    ///     ⇒ <b>不带</b> <c>Unblockable</c>（<c>ValueProp.Unblockable</c> 的官方注释就是
    ///     <i>"HP loss like Poison"</i> ＝ 无视格挡，与权威相反 ✗）。
    ///     保留 <c>Unpowered</c>（官方注释 <i>"Damage from relics, potions, and powers"</i>）：
    ///     焚烧伤害恒等于层数，**不该**吃力量之类的攻击加成。</para>
    /// </summary>
    private const ValueProp BurnDamageProps = ValueProp.Unpowered;

    /// <summary>
    ///     ★★ <b>「附加【焚烧】到已有【焚烧】的敌人」⇒ 它自己立刻无消耗炸一次</b>
    ///     （权威 L46：「当你附加【焚烧】到带有【焚烧】的敌人时，这名敌人会无消耗的立刻触发一次【焚烧】，
    ///     此效果触发的伤害<b>不会波及它人</b>」）。
    ///
    ///     <para><b>为什么挂在这个钩子</b>：<c>PowerCmd.Apply</c> 分两条路 ——
    ///     目标身上**没有**该 Power 时走"新建实例"（<c>PowerModel.AfterApplied</c>），
    ///     **已有**时走 <c>ModifyAmount</c>（<b>只加层，不调 <c>AfterApplied</c></b>，实据
    ///     <c>PowerCmd.cs:83-92</c>）。而<b>两条路都会</b>调 <c>Hook.AfterPowerAmountChanged</c>
    ///     （实据 <c>PowerCmd.cs:160</c> 与 <c>:250</c>）⇒ 只有这个钩子能同时覆盖，
    ///     且它能区分"本次加了多少层"。</para>
    ///
    ///     <para>★ <b>首次挂上不触发</b>：那一刻 <c>Amount == amount</c> ⇒ 加层前是 0 ⇒ 不是"附加到**带有**焚烧的敌人"。</para>
    ///
    ///     <para>★ <b>伤害口径</b>＝<b>该敌人自己当前的层数</b>（含本次刚加上去的），只打它自己。
    ///     用户 2026-10-07 裁定：「现在就做：附加到已有焚烧的敌人 → 它自己立刻无消耗炸一次」，
    ///     并明确伤害<b>不按</b>场上总层数（那会在多敌时爆炸式增长）。</para>
    /// </summary>
    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        try
        {
            if (!ReferenceEquals(power, this)) return;          // 只认我们自己这一次【焚烧】的变化
            if (amount <= 0) return;                            // 只在"加层"时；扣层/清层不触发
            if (applier == null || !applier.IsPlayer) return;   // 权威写的是「**你**附加」⇒ 只认玩家方施加的
            if (Owner == null || Owner.IsDead || Owner.IsPlayer) return;   // 只对敌人（我方身上的焚烧不触发）

            int added = (int)amount;
            int before = (int)Amount - added;                    // ★ 钩子触发时 Amount 已含本次增量
            if (before <= 0) return;                             // 首次挂上 ⇒ 不触发

            // ★★ 【生死一线】「你触发的附加【焚烧】将不再有效果」（权威 卡牌说明2.txt L28）
            if (OrcaMoltenBonePower.IsActive(applier))
            {
                Log.Info($"[Orca] 焚烧·附加即时触发：{Owner.Name} 已有 {before} 层，但施加者带【生死一线】"
                       + " ⇒ 本次附加不触发（权威：你触发的附加焚烧不再有效果）", 2);
                return;
            }

            if (CombatManager.Instance.IsOverOrEnding) return;

            int stacks = (int)Amount;                            // 无消耗 ⇒ 用当前层数
            int hpBefore = Owner.CurrentHp;

            // ★ 只打它自己（权威「此效果触发的伤害不会波及它人」）⇒ 不走 Burst 的波及逻辑。
            //   ⚠️ 这里**不需要** OrcaDeathQuotes 的批次闸门：目标只可能是敌人，玩家不可能被这次伤害打死
            //      （那条闸门服务的是"你和敌人被同一批焚烧一起烧死"的死亡台词）。
            await CreatureCmd.Damage(choiceContext, new[] { Owner }, (decimal)stacks, BurnDamageProps, applier);

            int lost = Math.Max(0, hpBefore - Owner.CurrentHp);
            Log.Info($"[Orca] 焚烧·附加即时触发：{Owner.Name} 原本已有 {before} 层 + 本次 {added} 层"
                   + $" ⇒ 立刻无消耗炸 {stacks} 点（先扣格挡），实际损失生命 {lost}"
                   + $"（不消耗层数、不波及他人）", 2);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 焚烧·附加即时触发出错（不影响本次附加）：{ex.Message}", 2);
        }
    }

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

            // 回合结束的结算 = 触发一次；层数是否消耗由【生死一线】决定（见 Burst）
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
    /// <returns>本次触发的“炸开”个数（0 = 场上没有焚烧），以及本批**敌人实际损失的生命**（红莲淬据此给剑加伤害附加）。</returns>
    internal static async Task<(int BurstCount, int EnemyHpLost)> TriggerAllNow(PlayerChoiceContext ctx)
    {
        // ⚠️ 取战斗状态用 DebugOnlyGetState —— 红莲淬在**玩家回合内**打出，它此时就是当前战斗状态 ✓
        //    （实据：ilspy 反编译 sts2.dll，CombatManager 里**没有**公开的 State 属性，
        //      只有 StateTracker（其内部状态不公开）与这个方法。）
        //    ⚠️ 边界显式：拿不到就如实报"不在战斗中"，不静默 return。
        var combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat == null)
        {
            Log.Warn("[Orca] 红莲淬：不在战斗中，本次不触发", 2);
            return (0, 0);
        }

        // 先数一下有没有可触发的（没触发时红莲淬要显式告诉玩家"什么都没发生"）
        int n = CountBurning(combat);
        if (n == 0)
        {
            Log.Info("[Orca] 红莲淬：场上没有任何【焚烧】⇒ 本次无效果", 2);
            return (0, 0);
        }

        var (bursts, enemyHpLost) = await Burst(ctx, combat, consumeStacks: false, why: "红莲淬");
        Log.Info($"[Orca] 红莲淬：立刻触发 {n} 个【焚烧】（不消耗层数）⇒ 敌人实际损失生命合计 {enemyHpLost}", 2);
        return (bursts, enemyHpLost);
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
    ///     <c>false</c> 用于红莲淬（无消耗触发）；★【生死一线】生效时**恒不消耗**（权威「不再消失」）。</para>
    ///
    ///     <para><b>返回值</b>：本批"炸开"的批次数，以及<b>敌人实际损失的生命合计</b> ——
    ///     红莲淬按权威 L21「将敌人损失的生命附加到龙剑/魔剑伤害中」消费后者。</para>
    /// </summary>
    /// <param name="choiceContext">选择上下文。</param>
    /// <param name="combat">战斗状态（由调用方给 —— 回合结束那条路用 <c>Owner.CombatState</c>）。</param>
    /// <param name="consumeStacks"><c>true</c> 用于回合结束（消耗层数），<c>false</c> 用于红莲淬（无消耗触发）；
    /// ★【生死一线】生效时即使传 <c>true</c> 也不消耗（权威「不再消失」）。</param>
    /// <param name="why">日志里的触发来源。</param>
    private static async Task<(int Bursts, int EnemyHpLost)> Burst(
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
        if (burning.Count == 0) return (0, 0);

        // ① 先取快照（结算过程中层数会被清空）
        var snapshot = burning
            .Select(c => (Target: c, Stacks: (int)(c.GetPower<OrcaBurnPower>()?.Amount ?? 0)))
            .Where(t => t.Stacks > 0)
            .ToList();
        if (snapshot.Count == 0) return (0, 0);

        // ★ 本批统计：bursts 只用于日志，enemyHpLost 是红莲淬的联动输入（敌人**实际**损失的生命）
        int bursts = 0;
        int enemyHpLost = 0;

        // ★ A3（2026-10-01；★ 2026-10-07 按权威改版整体重订）：【生死一线】（熔渊枯骨）—— 现行权威原文
        //   （work/奥卡卡包集/卡牌包2/卡牌说明2.txt L28）：
        //   "你触发的附加【焚烧】将不再有效果，每次【焚烧】结算时将对场上所有人（包括奥卡）造成伤害，不再消失"
        //   ⇒ 三条：① 附加即时触发失效（见 AfterPowerAmountChanged）；② 波及场上所有人（含奥卡）；
        //     ③ 层数**完全不消耗**（= 下方消费段的 consumeStacks && !molten）。
        //     旧口径「每次触发只消耗 50% 层数（向下取整）」已随本轮作废。
        //   ★ 2026-10-04 恢复真判据（原来是 /*TOGGLE-OFF-A3*/ bool molten = false; 硬编码短路
        //     ⇒ 熔渊枯骨永远不生效，而 OrcaMoltenBonePower.IsActive 全工程只有定义、无调用点）。
        //   判据来源 = 现成的 OrcaMoltenBonePower.IsActive(Creature?)（单一来源，不另写一份判断）。
        //   ⚠️ 只认**玩家自己**身上有没有这个 Power：权威写的是"**你造成的**【焚烧】"，
        //      若按"场上任意生物"取，联机里任一玩家带【生死一线】就会改写所有人的焚烧结算。
        bool molten = OrcaMoltenBonePower.IsActive(combat.PlayerCreatures.FirstOrDefault());
        if (molten) Log.Info("[Orca] 焚烧结算：【生死一线】生效 ⇒ 层数**完全不消耗**（权威「不再消失」）+ 波及场上所有人（含奥卡）", 2);

        // ⚠️ 日志必须**如实**说明这一批"炸到谁"：molten 时打的是场上所有人（含没带焚烧的敌人），
        //    若照旧写成"带焚烧的敌人"，实机排查会把范围误判成"只打带焚烧的"✗（本次订正的原因）
        string scopeText = molten ? "场上所有人（含未带焚烧者）" : "带焚烧者";

        // ② 消耗层数（红莲淬走"无消耗"⇒ 整段跳过）
        // ★ 2026-10-07 权威改版：① 红莲淬 ⇒ consumeStacks=false，整段跳过；
        //   ②【生死一线】⇒ 层数**完全不消耗**（权威「不再消失」）⇒ 这里也整段跳过
        //   （原来那句「只消耗 50% 层数」的半扣分支已随本轮删除，见下面 foreach 内的注释）。
        if (consumeStacks && !molten)
        {
            foreach (var (target, _) in snapshot)
            {
                var p = target.GetPower<OrcaBurnPower>();
                if (p == null) continue;

                // ★ 2026-10-07 权威改版：走到这里就是**全部消耗**，不再有"扣一半"的分支 ——
                //   需要"不消耗"的两种情况（红莲淬 / 【生死一线】）都在外层条件里被挡掉了。
                await PowerCmd.Remove(p);
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
            // ★★ 红莲淬的联动（2026-10-07 权威 卡牌说明1.txt L21）要的是「**敌人损失的生命**」
            //    ⇒ 出伤前后各取一次当前生命、取差值累加。不用 DamageResult 的理由与魔剑狂躁的单敌回血同款
            //      （伤害指令的 Execute 不返回它）。
            //    ⚠️ 只统计**敌人**：molten 时 alive 还含玩家侧生物，那些不该算进"敌人损失的生命"。
            var enemyHpBefore = alive.Where(c => !c.IsPlayer)
                                     .ToDictionary(c => c, c => c.CurrentHp);

            OrcaDeathQuotes.BeginBurnDamage(batchId);
            try
            {
                await CreatureCmd.Damage(
                    choiceContext,
                    alive,
                    (decimal)stacks,
                    BurnDamageProps,
                    src);
            }
            finally
            {
                OrcaDeathQuotes.EndBurnDamage();
            }

            bursts++;
            foreach (var (creature, hpBefore) in enemyHpBefore)
            {
                enemyHpLost += Math.Max(0, hpBefore - creature.CurrentHp);
            }

            Log.Info($"[Orca] 焚烧结算（{why}）：{src.Name} 的 {stacks} 层炸开 → 波及 {alive.Count} 个{scopeText}", 2);
        }

        // ★ 本批统计交回调用方（红莲淬用它把"敌人实际损失的生命"全额加到剑上）
        return (bursts, enemyHpLost);
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