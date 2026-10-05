using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

// ═══════════════════════════════════════════════════════════════════════
//  卡牌扩充第二版 · Power 层（设计原文见 `奥卡卡包集\\卡牌包2\\新建 文本文档.txt`）
//
//  ★ 改值类钩子的**语义**（反编译 `VulnerablePower` / `IntangiblePower` 实据）：
//     · `ModifyDamageMultiplicative(...)` → 返回的是**倍率**（易伤返回 1.5m、不适用返回 1m）
//     · `ModifyDamageCap(...)`            → 返回的是**伤害上限**（无实体返回 1m、不适用返回 decimal.MaxValue）
//     · `ModifyCardPlayCount(...)`        → 返回的是**新的打出次数**（复制返回 playCount + 1）
//     · `ModifyHpLostAfterOsty(...)`      → 返回的是**实际掉血量**（纯函数，不能 await）
//       配对的 `AfterModifyingHpLostAfterOsty()` 是 Task 钩子 ⇒ 异步收尾写在那里（无实体就是这么拆的）
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
///     ★ 烬血之翼（卡牌）的**记号** Power —— 它自己**不提供任何减伤**。
///
///     <para><b>减伤与减层现在全在 <see cref="OrcaSoarPower" />（自写翱翔）里</b>，
///     因为原版翱翔是 <c>sealed</c> + <c>StackType.Single</c>，不能继承也不能减层。</para>
///
///     <para>本 Power 只干一件事：**在下个奥卡回合开始时把翱翔摘掉**（基础版专属；
///     敲后不挂本记号 ⇒ 翱翔不再因回合开始而消失）。</para>
///
///     <para>「挨未格挡伤害 ⇒ 减一层」**不在这里**：那由 <c>OrcaSoarPower.AfterDamageReceived</c>
///     自己 <c>PowerCmd.Decrement</c> 完成。若在此处也做一遍"整个移除"，
///     会抢在减层之前抹掉翱翔，多层时行为就错了。</para>
/// </summary>
public sealed class OrcaEmberWingPower : PowerModel
{
    // ★ 2026-10-04（用户口径）：「我们不是照抄，我们是玩家，因此逻辑很简单：怪物的所有伤害减少50%」
    //   ⇒ 减伤改为由自写 OrcaSoarPower 提供，且**去掉原版那句 `if (!props.IsPoweredAttack()) return 1m;`**
    //     —— 那行只挡"攻击牌造成的伤害"，我们要的是**一切伤害减半**。
    //   本 Power 由此彻底降级为纯记号（见类注释）。

    public override PowerType Type => PowerType.Buff;

    /// <summary>纯记号，不叠层、不显示层数。</summary>
    public override PowerStackType StackType => PowerStackType.None;

    /// <summary>
    ///     下回合奥卡回合开始时：移除【翱翔】+ 自己。
    ///     <para>时序说明：本 Power 是在**玩家回合中**打牌时施加的，所以本回合的
    ///     <c>AfterPlayerTurnStart</c> 早已过去 ⇒ 下一次触发正是"下回合奥卡回合开始"，与原文一致。</para>
    /// </summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (Owner == null || player != Owner.Player) return;

            var soar = Owner.GetPower<OrcaSoarPower>();
            if (soar != null) await PowerCmd.Remove(soar);
            await PowerCmd.Remove(this);

            OrcaLog.Info("[Orca] 烬血之翼：奥卡回合开始 ⇒ 【翱翔】已消失", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 烬血之翼·收尾出错（翱翔可能残留）：{ex.Message}", 2);
        }
    }

    // ★ 2026-10-04 删除：原先这里 override AfterDamageReceived，
    //   作用是"挨到未格挡伤害 ⇒ 把翱翔整个移除"（当时被 /*TOGGLE-OFF-A5*/ 关着）。
    //
    //   现改为由【自写的 OrcaSoarPower】自己负责"受到的伤害减半 + 每挨一次未格挡伤害减一层"
    //   （用户口径 2026-10-04：「我们不是照抄，我们是玩家，因此逻辑很简单：怪物的所有伤害减少50%」
    //     + 规格卡牌说明2.txt：「翱翔存在时会为奥卡提供50%的减伤，当奥卡受到敌人未被格挡的
    //       生命伤害时减少一层」）。
    //
    //   ⇒ 这一段必须【删除而不是打开】：若保留，它会在 OrcaSoarPower 减层之前
    //     把翱翔整个抹掉，违背"减少一层"的规格（1 层时两者结果相同，多层时行为就错了）。
    //   ⇒ 本记号 Power 今后只负责一件事：**在你的回合开始时移除翱翔**。
}

/// <summary>
///     ★★★ 睥睨的 Power —— **打出的牌额外打出一次（不分牌型），每多打出一次消耗 1 层**。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L40）：
///     <i>"并获得获得等额层buff-【睥睨】——**消耗一层，使打出的牌额外打出一次**"</i>
///     ⇒ 层数由卡牌那侧按"消耗手牌数"等额施加（<see cref="OrcaOverlook" /> 的 <c>OnPlay</c>），
///     本类只负责"打出一张牌 ⇒ 多打一次 + 减 1 层"。</para>
///
///     <para>措辞变化（旧 → 新）：旧权威写"使**这张牌**额外打出一次"，新权威改成"使**打出的牌**"
///     ⇒ 与本类的"不分牌型"实现同口径（2026-09-23 用户口径也纠正过："只能额外打出一张牌"，
///     一度改成原版三件套的"每类各 1 次"是错的，已改回单次）。</para>
///
///     <para>挂点照抄原版 <see cref="DuplicationPower" />：
///     <c>ModifyCardPlayCount</c> 返回 <c>playCount + 1</c>，
///     再用配对的 <c>AfterModifyingCardPlayCount</c> 把层数减掉（一层换一次）。</para>
/// </summary>
public sealed class OrcaOverlookPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    ///     ★ 本层 Power 管不管这张牌 —— <c>ModifyCardPlayCount</c> 与
    ///     <c>AfterModifyingCardPlayCount</c> **共用这一个判据**（单一来源）。
    ///
    ///     <para>为什么配对钩子也要判：层数现在是"等额"（可能 N 层），若减层不核对
    ///     "这一次到底有没有真的多打一次"，别的生物打出的牌（<c>Owner</c> 不是我们）
    ///     也会白白扣掉一层 —— 与本工程既有做法一致（见 <c>OrcaOverlookBuffs.cs</c> 的
    ///     "配对钩子必须再加一道判定，否则别的牌型打出来也会误扣层数"，那两个类已随本轮删除，
    ///     但这条纪律保留）。</para>
    /// </summary>
    private bool Boosts(CardModel card) => card.Owner?.Creature == Owner;

    /// <summary>下 1 张打出的牌多打出一次（只认自己打出的牌）。</summary>
    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
        => Boosts(card) ? playCount + 1 : playCount;

    /// <summary>配对钩子：**确实多打了一次**才减 1 层（用完即清）。</summary>
    public override async Task AfterModifyingCardPlayCount(CardModel card)
    {
        if (!Boosts(card)) return;

        try
        {
            await PowerCmd.Decrement(this);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 睥睨·减层出错：{ex.Message}", 2);
        }
    }
}


/// <summary>
///     ★ 熔渊枯骨（卡牌）的 Power —— 【生死一线】标记。
///
///     <para>用户口径：<i>"你造成的【焚烧】每次触发只消耗 50% 层数，
///     【焚烧】现在变成对场上所有人（包括奥卡）造成伤害"</i>。</para>
///
///     <para>本类**只是个标记**（<c>Amount</c> 无意义）—— 真正的规则改写发生在
///     <see cref="OrcaBurnPower" /> 里：它每次结算前查一下玩家身上有没有这个 Power。</para>
/// </summary>
public sealed class OrcaMoltenBonePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    /// <summary>玩家身上是否挂着【生死一线】。</summary>
    internal static bool IsActive(Creature? player) =>
        player != null && player.GetPower<OrcaMoltenBonePower>() != null;
}

/// <summary>
///     ★ 龙之威仪（卡牌）的 Power —— 本回合内，**每消耗一张牌 / 每回复一次生命**，
///     就给所有敌人各 1 层<b>易伤</b>与<b>虚弱</b>。
///
///     <para>用户口径：<i>"在本回合内每消耗一张牌，回复一次生命，就给与敌人一层易伤与虚弱"</i>。</para>
///
///     <para>两个触发点都复用**已验证过**的挂点：
///     <list type="bullet">
///       <item>消耗 → <c>AfterCardExhausted</c>（同战鼓 / 狂躁的兜底钩子）；</item>
///       <item>回血 → <c>AfterCurrentHpChanged</c> 的**正值**分支（同银龙血统，它用的是负值分支）。</item>
///     </list>
///     易伤 / 虚弱直接用原版 <see cref="VulnerablePower" /> / <see cref="WeakPower" />。</para>
/// </summary>
public sealed class OrcaDragonDignityPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    /// <summary>每触发一次给几层（用户口径：1 层）。</summary>
    private const decimal StacksPerTrigger = 1m;

    /// <summary>重入闸门：我们自己在钩子里改 HP 会再次触发钩子，必须屏蔽。</summary>
    private bool _applying;

    public override async Task AfterCardExhausted(
        PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if (_applying) return;
        if (card.Owner?.Creature != Owner) return;      // 只认自己消耗的牌
        await PunishEnemies(choiceContext, $"消耗 {card.Id.Entry}");
    }

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_applying) return;
        if (creature != Owner) return;
        if (delta <= 0) return;                          // 掉血不算"回复一次生命"
        await PunishEnemies(null, $"回复 {delta} 点生命");
    }

    /// <summary>给所有存活敌人各挂 1 层易伤 + 1 层虚弱。</summary>
    private async Task PunishEnemies(PlayerChoiceContext? choiceContext, string reason)
    {
        try
        {
            var me = Owner;
            var combat = me?.CombatState;
            if (me == null || combat == null) return;

            // ★ 回血那条触发路径（AfterCurrentHpChanged）**不带上下文**，
            //   而 PowerCmd.Apply 必须要一个非空的 PlayerChoiceContext
            //   ⇒ 照引擎自己的做法造一个（反编译 Hook.BeforeSideTurnEnd 实据）：
            //     new HookPlayerChoiceContext(source, owner, localPlayerId, GameActionType.Combat)
            var ctx = choiceContext;
            if (ctx == null)
            {
                var netId = LocalContext.NetId;
                if (!netId.HasValue) return;
                ctx = new HookPlayerChoiceContext(this, Owner!.Player!, netId.Value, GameActionType.Combat);
            }

            _applying = true;
            int hit = 0;
            foreach (var enemy in combat.Enemies.Where(e => !e.IsDead).ToList())
            {
                await PowerCmd.Apply<VulnerablePower>(ctx, enemy, StacksPerTrigger, me, null);
                await PowerCmd.Apply<WeakPower>(ctx, enemy, StacksPerTrigger, me, null);
                hit++;
            }

            OrcaLog.Info($"[Orca] 龙之威仪：{reason} ⇒ 给 {hit} 个敌人各 {StacksPerTrigger} 层易伤 + 虚弱", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 龙之威仪触发失败：{ex.Message}", 2);
        }
        finally
        {
            _applying = false;
        }
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner)) await PowerCmd.Remove(this);   // 只到本回合结束
    }
}

/// <summary>
///     ★ 浴血涅槃（卡牌）的 Power —— **受到致命伤害时**：
///     消耗所有临时生命上限，回复 50%（消耗上限，向下取整）的生命，**触发一次后消失**。
///
///     <para>台词 <i>"杀不死我的，我会更强大！"</i></para>
///
///     <para>★★ <b>2026-10-05：删掉了「回合结束到期作废」</b>（用户实测「有的时候会出现浴血打出后，
///     buff 图标消失」）。原先有一个 <c>AfterSideTurnEnd</c> 里 <c>PowerCmd.Remove(this)</c> 的
///     "本回合没触发 ⇒ 到期作废" —— 那是**旧口径**的遗留（下引那句用户旧话里的"本回合"），
///     而现行三处依据都指向"整场有效、触发一次才消失"：
///     <list type="number">
///       <item>权威 <c>卡牌包2\卡牌说明2.txt</c>：「3费，银龙，虚无，金卡，**能力牌**，敲后2费 /
///         当你受到致命伤害时，消耗所有临时生命上限，回复50%消耗上限…的生命」——**通篇没有"本回合"**；</item>
///       <item>卡面 <c>ORCA_BLOOD_NIRVANA.description</c>：「…触发一次后消失。」；</item>
///       <item>用户实测：打完一结束回合图标就没了 ⇒ 与上面两条矛盾。</item>
///     </list>
///     ⇒ 现在只保留 <see cref="SettleFromLethal" /> 里的「用掉即摘」，与卡面逐字一致。</para>
///
///     <para><b>怎么拦下致命伤</b>：照抄原版 <see cref="IntangiblePower" /> 的**两段式拆分**——
///     <list type="number">
///       <item><see cref="ModifyHpLostAfterOsty" />（纯函数，不能 await）：算出"这一下会不会打死我"，
///         会的话把掉血量**压到只剩 1 点生命**，并打上 <c>_pending</c> 标记；</item>
///       <item><see cref="AfterModifyingHpLostAfterOsty" />（Task 钩子）：拿标记去做异步收尾
///         —— 消耗临时上限、回血、摘掉自己。</item>
///     </list></para>
/// </summary>
public sealed class OrcaBloodNirvanaPower : PowerModel
{
    /// <summary>
    ///     回复比例（消耗上限的 50%）。
    ///     权威：50%（卡牌包2\卡牌说明2.txt L3「回复50%消耗上限（具体数值向下取整）的生命」）。
    ///     ★ 卡面（<c>ORCA_BLOOD_NIRVANA.description</c> 的 <c>{HealPercent:diff()}</c>）经
    ///     <see cref="HealRatioDisplay" /> 读这一个常量 ⇒ 改这里卡面跟着变，不再是两处各写一份。
    /// </summary>
    internal const decimal HealRatio = 0.5m;

    /// <summary>
    ///     同一比例转成的**百分点数**（0.5m ⇒ 50），仅供卡面显示 ——
    ///     卡面文案里带格式化器的占位符必须由 <c>DynamicVar</c> 注入（裸值会 <c>No suitable Formatter</c>）。
    /// </summary>
    internal const decimal HealRatioDisplay = HealRatio * 100m;

    /// <summary>本帧是否刚拦下一次致命伤（由纯函数打标、由 Task 钩子消费）。</summary>
    private bool _pending;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    public override decimal ModifyHpLostAfterOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        try
        {
            if (target != Owner) return amount;
            if (amount < Owner.CurrentHp) return amount;      // 打不死 ⇒ 原样放行

            _pending = true;                                   // 会打死 ⇒ 拦！
            return Math.Max(0m, Owner.CurrentHp - 1m);         // 只掉到剩 1 点生命
        }
        catch
        {
            return amount;
        }
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        if (!_pending) return;
        _pending = false;
        // ★ 补齐（按官方反编译版 ✓）：结算抽成 SettleFromLethal，与 AfterCurrentHpChanged / Patch 共用
        await SettleFromLethal();
    }

    /// <summary>
    ///     ★ 补齐（官方反编译实证 ✓）：另一条拦截路径 —— 有人**直接改 CurrentHp**（而非走掉血管线）时，
    ///     拦下致命伤后也会走到这里；与 <see cref="AfterModifyingHpLostAfterOsty" /> 共用一个结算。
    /// </summary>
    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_pending && creature == Owner)
        {
            _pending = false;
            await SettleFromLethal();
        }
    }

    /// <summary>★ 补齐（官方反编译实证 ✓）：由 <c>OrcaNirvanaSelfHarmPatch</c> 在纯函数里打标，异步钩子消费。</summary>
    internal void MarkPending() => _pending = true;

    private async Task SettleFromLethal()
    {

        try
        {
            Flash();

            // ① 消耗**所有**临时生命上限
            decimal pool = OrcaTempHp.Current;
            decimal used = OrcaTempHp.Consume(pool);

            // ② 回复其中的 50%（向下取整，与 HealRatio 同源；旧注释写 40% 是过期口径）
            int heal = (int)Math.Floor(used * HealRatio);
            if (heal > 0 && Owner != null)
            {
                await CreatureCmd.Heal(Owner, heal, true);
            }

            OrcaLog.Info($"[Orca] 浴血涅槃：拦下致命伤 ⇒ 消耗临时生命上限 {used}，"
                     + $"回复 {heal} 点生命（{HealRatio:P0}）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 浴血涅槃结算失败：{ex.Message}", 2);
        }
        finally
        {
            await PowerCmd.Remove(this);                       // 用掉即摘（本回合内只保一次）
        }
    }

    // 过期策略：本 Power **不在回合结束时作废**（2026-10-05 删除，理由见类摘要）。
    // 唯一的下场是 SettleFromLethal 里的「用掉即摘」—— 与卡面「触发一次后消失」逐字一致。
}

/// <summary>
///     ★ 归墟（卡牌）的 Power —— 场上所有角色**无法回复生命**，
///     本该回复的生命按 50% 比例转入【嗜血魔剑】的附加伤害。
///
///     <para>用户口径：<i>"场上所有角色无法回复生命，回复的生命按 50% 比列（向下取整）转入魔剑的附加伤害"</i>；
///     敲后比例 75%。</para>
///
///     <para>实现走 <see cref="AfterCurrentHpChanged" /> 的**正值**分支（同龙之威仪、同银龙血统）：
///     原版没有"战斗内治疗"的拦截钩子（`ModifyRestSiteHealAmount` 只管篝火），
///     所以只能在**回血已经发生后**把血扣回去，并把差值转成魔剑附加 —— 效果等价，见下方注释。</para>
/// </summary>
public sealed class OrcaVoidReturnPower : PowerModel
{
    /// <summary>转入附加的百分比（用户口径：50% → 敲后 75%）。</summary>
    internal decimal Ratio = 0.5m;

    /// <summary>重入闸门：我们自己在钩子里 SetCurrentHp 会再次触发钩子。</summary>
    private bool _applying;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_applying) return;
        if (delta <= 0) return;                                  // 只处理"回复"

        try
        {
            _applying = true;

            // ① 把这次回复**撤销**（"无法回复生命"）
            await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - delta);

            // ② 其中一部分转成【嗜血魔剑】的附加伤害
            int converted = (int)Math.Floor(delta * Ratio);
            if (converted > 0)
            {
                var sword = Owner?.Player?.PlayerCombatState?.AllCards
                    .OfType<OrcaBloodSword>().FirstOrDefault();
                sword?.AddBonus(converted);

                OrcaLog.Info($"[Orca] 归墟：{creature.Name} 的 {delta} 点回复被阻止 ⇒ "
                         + $"{converted} 点（{Ratio:P0}）转入【嗜血龙剑】附加伤害"
                         + $"（当前 {sword?.Bonus ?? 0}）", 2);
            }
            else
            {
                OrcaLog.Info($"[Orca] 归墟：{creature.Name} 的 {delta} 点回复被阻止（不足 1 点可转化）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 归墟结算失败：{ex.Message}", 2);
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>
///     ★ 栖途（卡牌）的 Power —— 战斗结束时，把 **25% 的战斗临时生命上限**
///     转化为**真实（永久）生命上限**。
///
///     <para>用户口径：<i>"（选取遗物-欧洛巴斯之触会获得这张卡牌）战斗结束后，
///     将你 25% 的临时生命上限转化为真实生命上限"</i>。</para>
///
///     <para>⚠️ <b>与银龙血统的配合</b>：遗物 <c>AfterCombatEnd</c> 会把**剩余**临时上限还原掉。
///     我们从共享池 <see cref="OrcaTempHp" /> 里**先 Consume 掉 25%**，再加到真实上限上
///     ⇒ 遗物随后只会还原剩下的 75%，不会把转化过的部分又扣回去。
///     （两个 <c>AfterCombatEnd</c> 的**先后顺序**由引擎决定；若实测发现遗物先跑导致转化量为 0，
///     就把这里改成在 <c>AfterCombatVictory</c> 更早的钩子里做 —— 已记为实机验证点。）</para>
/// </summary>
public sealed class OrcaHomesteadPower : PowerModel
{
    /// <summary>转化比例（用户口径：25%）。</summary>
    /// <summary>★ 卡包2 原文："敲后50%" ⇒ 升级后比例 25% → 50%（由卡牌在 OnUpgrade 里置位）。</summary>
    internal static bool UpgradedRatio;

    private static decimal Ratio => UpgradedRatio ? 0.5m : 0.25m;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        try
        {
            decimal pool = OrcaTempHp.Current;
            int convert = (int)Math.Floor(pool * Ratio);
            if (convert <= 0)
            {
                OrcaLog.Info($"[Orca] 栖途：本场临时生命上限 {pool}，不足 1 点可转化", 2);
                return;
            }

            decimal used = OrcaTempHp.Consume(convert);      // ★ 先出池，免得被遗物一起还原掉
            var me = Owner;
            if (me != null && used > 0)
            {
                await CreatureCmd.GainMaxHp(me, used);        // 真实上限（会顺带回血，符合"转化"语义）
                OrcaLog.Info($"[Orca] 栖途：战斗结束 ⇒ 临时生命上限 {pool} 的 {Ratio:P0}"
                         + $"，共 {used} 点转化为**真实生命上限**（当前上限 {me.MaxHp}）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 栖途结算失败：{ex.Message}", 2);
        }
        finally
        {
            await PowerCmd.Remove(this);                      // 一次性：转化过就摘掉
        }
    }
}