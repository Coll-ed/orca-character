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
using MegaCrit.Sts2.Core.Localization;        // ★ A7：覆写 PowerModel.Description 需要 LocString
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
    ///     未敲的回复比例（百分点，= 卡面 50%）。
    ///     权威：<c>卡牌包2\卡牌说明2.txt</c> L3「当你受到致命伤害时，消耗所有临时生命上限，
    ///     回复50%消耗上限（具体数值向下取整）的生命」。
    /// </summary>
    internal const int BasePercent = 50;

    /// <summary>
    ///     敲后的回复比例（百分点，= 卡面 100%）。
    ///     ★ <b>2026-10-07 权威改版</b>（<c>卡牌说明2.txt</c> L2「3费，银龙，虚无，金卡，能力牌，
    ///     <b>敲后100%比例恢复</b>」）⇒ 敲后**不再减费**（旧口径「敲后 3→2 费」已作废），
    ///     费用恒为 3，升级只抬这个比例。
    /// </summary>
    internal const int UpgradedPercent = 100;

    /// <summary>百分数 ↔ 比例的分母（100 = "百分数"这个单位的定义；两个换算方向共用）。</summary>
    private const int PercentBase = 100;

    /// <summary>
    ///     回复比例（消耗上限的比例）—— <b>本实例</b>的字段，由卡牌在出牌时按该卡本局的
    ///     <c>IsUpgraded</c> 写入（与 <see cref="OrcaVoidReturnPower.Ratio" /> 同一套写法：
    ///     不引入任何跨局共享的 static 状态）。
    /// </summary>
    private decimal _healRatio = RatioFromPercent(BasePercent);

    /// <summary>当前回复比例（0.5 / 1.0）—— 结算与日志共用。</summary>
    internal decimal HealRatio => _healRatio;

    /// <summary>百分数 → 比例的**唯一**换算点。</summary>
    internal static decimal RatioFromPercent(int percent) => (decimal)percent / PercentBase;

    /// <summary>比例 → 百分数的**唯一**换算点（卡面/日志显示用；不用 <c>P0</c> 格式化以避开本地化百分号）。</summary>
    internal static int PercentFromRatio(decimal ratio) => (int)Math.Round(ratio * PercentBase);

    /// <summary>
    ///     由卡牌在出牌时写入**本实例**的回复比例（敲后 100%，否则 50%）。
    ///
    ///     <para>★ 边界显式：百分点非法（不是那两个合法值之一）⇒ 记 Warn 并**拒绝写入**（保持默认 50%），
    ///     绝不静默接受。</para>
    /// </summary>
    /// <param name="percent">未敲 / 敲后的百分点（只接受 <see cref="BasePercent" /> 或 <see cref="UpgradedPercent" />）。</param>
    internal void SetHealPercent(int percent)
    {
        if (percent != BasePercent && percent != UpgradedPercent)
        {
            OrcaLog.Warn($"[Orca] 浴血涅槃：收到非法的回复百分点 {percent}"
                       + $"（合法值只有 {BasePercent} / {UpgradedPercent}）"
                       + $" ⇒ 本实例仍按 {PercentFromRatio(_healRatio)}% 结算");
            return;
        }

        _healRatio = RatioFromPercent(percent);
        OrcaLog.Info($"[Orca] 浴血涅槃：本实例回复比例已按本局卡牌状态写入 = {PercentFromRatio(_healRatio)}%"
                   + $"（本局{(percent == UpgradedPercent ? "**已敲**" : "**未敲**")}）");
    }

    /// <summary>本帧是否刚拦下一次致命伤（由纯函数打标、由 Task 钩子消费）。</summary>
    private bool _pending;

    public override PowerType Type => PowerType.Buff;

    /// <summary>
    ///     ★★ <b>浮窗里的百分比跟随本实例的比例</b>（A7 同一套路、同一处反编译实据）：
    ///     <c>PowerModel.HoverTips</c> 组装 buff 浮窗时**不会**调 <c>AddExtraArgsToDescription</c>
    ///     （那是 <c>CardModel</c> 独有的钩子，<c>PowerModel</c> 上没有这个方法）⇒
    ///     带格式化器的占位符只能在 <c>Description</c> 的取值处注入，
    ///     否则 <c>powers.json</c> 里那个 <c>{HealPercent}</c> 会**裸露**在玩家眼前（不是报错，是显示字面量）。
    ///     ★ 与卡面同源：两者都读本实例的比例（<see cref="HealRatio" />）⇒ 改常量两处一起变。
    /// </summary>
    public override LocString Description
    {
        get
        {
            var loc = base.Description;
            try
            {
                if (loc.GetRawText().Contains("{HealPercent}"))
                {
                    loc.Add("HealPercent", (decimal)PercentFromRatio(HealRatio));
                }
            }
            catch (Exception ex)
            {
                // 拿不到原文（表未加载等）⇒ 记日志后放行（与归墟同一处理：宁可退化成"可能裸露"，也不让浮窗构造失败）
                OrcaLog.Warn($"[Orca] 浴血涅槃：读取浮窗文案原文失败，HealPercent 未注入：{ex.Message}");
            }
            return loc;
        }
    }

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

            // ①′ ★★ 2026-10-05 修「临时上限泄漏」：Consume 只减**账本**（共享池），生物身上的
            //     `MaxHp` 一点没动 ⇒ 银龙血统收尾那次**绝对写回**（target = Max(basis, MaxHp − pool_rem)）
            //     在 pool_rem == 0 时会把 target 算成 `basis + 本笔` ⇒ 被"消耗"的临时上限**永久坐实**。
            //     ⇒ 必须**紧随其后、经遗物内部带闸门的路径**把它从上限上真正摘掉
            //     （Power 里直接 CreatureCmd.SetMaxHp 会绕过重入闸门 _applying ⇒ 降上限截断当前生命
            //       ⇒ 二次触发 `AfterCurrentHpChanged` ⇒ 钩子互相打架；此刻生命正被钳在 1 点，几乎必然触发）。
            //     ⚠️ 取遗物必须经 `Creature.Player`：`PowerModel.Owner` 是 **Creature** 不是 Player
            //       ⇒ `Owner.GetRelic<...>()` 会是 CS1061（只有真编译能抓到），
            //       与同文件上方「栖途」处写法一致（同为 Owner?.Player?.GetRelic<OrcaBloodline>()）。
            //     拿不到遗物 ⇒ **记 Error（不静默吞）**，且消耗账本 / 回血的既有逻辑照旧执行。
            if (Owner != null)
            {
                var bloodline = Owner.Player?.GetRelic<OrcaBloodline>();
                if (bloodline != null)
                {
                    // 递减量由遗物按「实际挂着的临时上限 = Max(0, MaxHp − 基准)」自行裁剪
                    // （引擎可能已在回合边界把上限同步回基准 ⇒ 此时实际摘掉 0，绝不压到基准以下）。
                    decimal removed = await bloodline.ConsumeAttachedTempMaxHp(used);
                    OrcaLog.Info($"[Orca] 浴血涅槃：临时上限消耗 {used} ⇒ 实际上限摘掉 {removed}"
                             + $"（当前上限 {Owner.MaxHp}）", 2);
                }
                else
                {
                    OrcaLog.Error($"[Orca] 浴血涅槃：账本已消耗 {used} 点临时上限，"
                                + "但**身上没有银龙血统** ⇒ 无法把上限从生物身上摘掉"
                                + "（本场战斗收尾可能把这笔坐实成真实上限）");
                }
            }

            // ② 回复其中的 50%（向下取整，与 HealRatio 同源；旧注释写 40% 是过期口径）
            int heal = (int)Math.Floor(used * HealRatio);
            if (heal > 0 && Owner != null)
            {
                await CreatureCmd.Heal(Owner, heal, true);
            }

            OrcaLog.Info($"[Orca] 浴血涅槃：拦下致命伤 ⇒ 消耗临时生命上限 {used}，"
                     + $"回复 {heal} 点生命（{PercentFromRatio(HealRatio)}%）", 2);
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
///     本该回复的生命按 50% 比例转入【嗜血魔剑】的附加伤害（敲后 75%）；**不拦截吸血**。
///
///     <para>权威口径（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L52-54，2026-10-07 改版）：
///     <i>"归墟 / 2费，魔剑，蓝卡，能力牌，敲后75%比例 / 场上所有角色无法回复生命，
///     回复的生命按50%比列（向下取整）转入<b>魔剑</b>的附加伤害，<b>不拦截吸血</b>"</i>。</para>
///
///     <para>实现走 <see cref="AfterCurrentHpChanged" /> 的**正值**分支（同龙之威仪、同银龙血统）：
///     原版没有"战斗内治疗"的拦截钩子（`ModifyRestSiteHealAmount` 只管篝火），
///     所以只能在**回血已经发生后**把血扣回去，并把差值转成魔剑附加 —— 效果等价，见下方注释。</para>
/// </summary>
public sealed class OrcaVoidReturnPower : PowerModel
{
    /// <summary>未敲的转入比例（百分点，用户口径 50%）—— <b>本功能数值的唯一来源</b>（卡面/浮窗/结算都由它推导）。</summary>
    internal const int BasePercent = 50;

    /// <summary>敲后的转入比例（百分点，用户口径 75%）。</summary>
    internal const int UpgradedPercent = 75;

    /// <summary>百分数 ↔ 比例的分母（100 = "百分数"这个单位的定义；两个换算方向共用）。</summary>
    private const int PercentBase = 100;

    /// <summary>
    ///     转入附加的比例（0.5 / 0.75）—— 由卡牌的 <c>OnPlay</c> 按本局 <c>IsUpgraded</c> 写入，默认未敲。
    ///
    ///     <para>⚠️ <b>这里刻意不写第二份数值</b>：只由 <see cref="BasePercent" /> /
    ///     <see cref="UpgradedPercent" /> 经 <see cref="RatioFromPercent" /> 换算得到
    ///     （纪律：同一个常量只定义一次；卡牌侧只 <b>引用</b> 这两个百分点常量）。</para>
    /// </summary>
    internal decimal Ratio = RatioFromPercent(BasePercent);

    /// <summary>百分数 → 比例的**唯一**换算点（卡牌写值、卡面与浮窗显示都走它）。</summary>
    internal static decimal RatioFromPercent(int percent) => (decimal)percent / PercentBase;

    /// <summary>比例 → 百分数的**唯一**换算点（浮窗注入用）。</summary>
    internal static int PercentFromRatio(decimal ratio) => (int)Math.Round(ratio * PercentBase);

    /// <summary>
    ///     ★★ <b>A7 修复点：buff 浮窗的百分比跟随实际比例</b>（原来 <c>powers.json</c> 里硬写 50%）。
    ///
    ///     <para><b>为什么必须走这条覆盖</b>（反编译实据，<c>sts2.dll</c>）：
    ///     <list type="number">
    ///       <item>Power 的浮窗文案在 <c>PowerModel.HoverTips</c>（<c>PowerModel.cs:350-403</c>）里组装：
    ///         取 <c>Description</c> → <c>AddDumbVariablesToDescription</c>（<c>:535-540</c>，只注入
    ///         <c>Amount</c> / <c>singleStarIcon</c> / <c>energyPrefix</c>）→ <c>GetFormattedText()</c>。
    ///         <b>它不会调用 <c>AddExtraArgsToDescription</c></b> —— 那个钩子**只存在于 <c>CardModel</c>**
    ///         （<c>CardModel.cs:1545</c>，由 <c>GetDescriptionForPile</c> 在 <c>:1376</c> 调），
    ///         <c>PowerModel</c> 上**没有**这个方法（全工程 <c>PowerModel</c> 无此成员）。</item>
    ///       <item>所以"把 <c>{Ratio}</c> 写进 <c>powers.json</c> 再靠 <c>AddExtraArgsToDescription</c> 注入"
    ///         这条**在 buff 浮窗上根本走不通**：没有注入点。</item>
    ///       <item>而缺值时的后果是**裸露占位符**而非报错：<c>LocManager.SmartFormat</c>
    ///         （<c>LocManager.cs:261-285</c>）在 <c>FormattingException</c> 下
    ///         <c>Log.Error</c> 后 <c>return rawText</c> ⇒ 玩家看到的字面量就是 <c>按 {Ratio}% 转入</c> ✗。
    ///         （这正是本项目已踩过四次的"整条文案回退成原文"。）</item>
    ///     </list></para>
    ///
    ///     <para><b>因此选的是"在取值处注入"</b>：<c>Description</c> 是 <c>virtual</c>
    ///     （<c>PowerModel.cs:51</c>），我们把它覆写成"同一个 <c>LocString</c>，但已带上 <c>Ratio</c>"。
    ///     这样：
    ///     <list type="bullet">
    ///       <item>与卡面（<c>Cards-Pack2-B.cs</c> 的 <c>{Ratio}</c>）**同一个数值来源** = 本 Power 的
    ///         <c>Ratio</c> 字段 ⇒ 不可能脱钩；</item>
    ///       <item>不新造第二份定义、不改 JSON 结构 ⇒ 不存在"另一处也写了一份 75"的漂移；</item>
    ///       <item>占位符只在**本类自己注入过之后**才可能被格式化到 ⇒ 结构上不可能裸露（见下方守卫）。</item>
    ///     </list></para>
    ///
    ///     <para>⚠️ <b>为什么可以先读原文再决定注不注</b>：<c>GetRawText()</c> 只读 <c>LocTable</c>，
    ///     不抛、不格式化；只有原文**确实含** <c>{Ratio}</c> 时才注入 ⇒ 万一将来文案改回写死数字，
    ///     这里连 <c>Ratio</c> 变量都不会塞进字典，不会与文案里可能存在的同名字段打架。</para>
    /// </summary>
    public override LocString Description
    {
        get
        {
            var loc = base.Description;
            try
            {
                if (loc.GetRawText().Contains("{Ratio}"))
                {
                    loc.Add("Ratio", (decimal)PercentFromRatio(Ratio));
                }
            }
            catch (Exception ex)
            {
                // 拿不到原文（表未加载等）⇒ 记日志后放行：退化成"文案里若写了 {Ratio} 就会裸露"，
                // 但这比整个浮窗构造失败要好，且这条 Warn 让根因可查、不静默。
                OrcaLog.Warn($"[Orca] 归墟：读取浮窗文案原文失败，Ratio 未注入：{ex.Message}");
            }
            return loc;
        }
    }

    /// <summary>重入闸门：我们自己在钩子里 SetCurrentHp 会再次触发钩子。</summary>
    private bool _applying;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.None;

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_applying) return;
        if (delta <= 0) return;                                  // 只处理"回复"

        // ★★ 权威 2026-10-07（卡牌说明2.txt L54）：「**不拦截吸血**」
        //    ⇒ 吸血那一路（闸门见 OrcaLifestealHeal）连"事后撤销"都不做 ——
        //      否则会把走 SetCurrentHp 的魔剑单敌自愈又扣回去。
        if (OrcaLifestealHeal.InFlight) return;

        try
        {
            _applying = true;

            // ① 把这次回复**撤销**（"无法回复生命"）
            await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - delta);

            // ② 其中一部分转成【嗜血魔剑】的附加伤害
            //    ★ 2026-10-07 权威改版：权威逐字写「转入**魔剑**的附加伤害」—— 改前这里加的是
            //      【嗜血龙剑】（与权威不符）。加附加的唯一入口 = OrcaSwordBonus.Add。
            int converted = (int)Math.Floor(delta * Ratio);
            if (converted > 0)
            {
                var pick = OrcaSwordBonus.OrcaSwordPick.BloodBlade;
                var hits = OrcaSwordBonus.Add(Owner?.Player, converted, pick);

                OrcaLog.Info($"[Orca] 归墟：{creature.Name} 的 {delta} 点回复被阻止 ⇒ "
                         + $"{converted} 点（{PercentFromRatio(Ratio)}%）转入{OrcaSwordBonus.Describe(hits, pick)}", 2);
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
///     ★★ 栖途（卡牌）的 Power ——【栖途】**记账**：自残触发【银龙血统】⇒ +1 层【再生】；
///     战斗结束后按**本场累计发放的【再生】层数**转化为**真实（永久）生命上限**。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L42-46，★ 2026-10-07 全面重做）：
///     <i>"栖途 / 3费，银龙，先古牌，能力牌，敲后（选取遗物-欧洛巴斯之触会获得这张卡牌）/
///     立刻获得你最大生命值50%点的格挡，结束这一回合并获得【栖途】buff /
///     【栖途】：在没有受到敌人伤害（烧血自残不算）的情况下触发【银龙血统】时，获得1层再生，
///     战斗结束后根据再生层数转化为生命上限"</i>。</para>
///
///     <para>⚠️ <b>旧口径整体作废</b>：改版前是"战斗结束后把 25%（敲后 50%）的战斗临时生命上限
///     转化为真实上限"，那套比例概念已随本轮删除（见下面那行删除说明）。
///     ★ 历史教训仍然有效、且本版结构性免疫：E4 那次"跨局泄漏"的成因是用**static 标志**表达
///     "这局敲没敲"（上一局敲过 ⇒ 下一局开局就按敲后结算 ✗）—— 新口径改的是**费用**
///     （卡牌自己的持久状态，由 <c>EnergyCost.UpgradeBy</c> 承担），本 Power
///     **没有任何 static 状态**可泄漏。</para>
///
///     <para>★ <b>不再动临时生命上限池</b>：改版前是"从共享池 <see cref="OrcaTempHp" /> 里先 Consume
///     掉一笔再转成真实上限"，新口径的来源是【再生】层数、与临时上限池**无关**
///     ⇒ 遗物的收尾逻辑（把本场临时上限还原掉）与这笔转化互不干扰，两边各管各的账。</para>
///
///     <para>★★ <b>先后顺序已查实（不再是"由引擎决定"的待验证点）</b> —— 本 Power **先跑**，遗物**后跑**。
///     两条反编译实据（<c>sts2.dll</c>，2026-10-05 复核）：
///     <list type="number">
///       <item><c>CombatManager.EndCombatInternal</c>（<c>CombatManager.cs:1320</c>）调
///         <c>Hook.AfterCombatEnd(runState, combatState, room)</c>，<c>combatState</c> **非 null**；</item>
///       <item><c>Hook.AfterCombatEnd</c>（<c>Hook.cs:472-479</c>）逐个 <c>await model.AfterCombatEnd(room)</c>，
///         遍历源 = <c>RunState.IterateHookListeners(combatState)</c>（<c>RunState.cs:812</c>）。
///         因为 <c>childCombatState != null</c>，那段的"遗物/药水"块被**跳过**
///         （<c>RunState.cs:142-155</c>），遗物改由**末尾的** <c>combatState.IterateHookListeners()</c> 给出
///         （<c>RunState.cs:197-213</c>）；而 <c>CombatState.IterateHookListeners()</c> 对每个生物
///         **先 <c>AddRange(creature.Powers)</c>、再放该玩家的遗物**（<c>CombatState.cs:119-138</c>），
///         盟友又排在敌人之前（<c>CombatState.cs:116-118</c>）
///         ⇒ 玩家生物上的本 Power 排在银龙血统**之前**。</item>
///     </list>
///     ⇒ 顺序是**对我们有利**的那一种：本 Power 的转化**先落地**、遗物的收尾**后跑**。
///     ⚠️ 正因为遗物**后跑**，"GainMaxHp 这笔真实上限"必须**紧随其后抬基准**
///     （见 <c>OrcaBloodline.AdvanceCombatBase</c>，与龙剑/魔剑击杀奖励同一根因）：
///     引擎若在收尾前就把临时上限同步回了基准，那么遗物那句
///     <c>target = max(basis, MaxHp − pool)</c> 会算出 <c>basis + gain − pool</c>，
///     一旦 gain &lt; pool 就被那个 max 兜底**吞掉**（这场转化白做）✗；
///     抬过基准之后它在"引擎清过 / 没清过"两种世界下都成立。</para>
/// </summary>
public sealed class OrcaHomesteadPower : PowerModel
{
    /// <summary>【栖途】每触发一次发放的【再生】层数（权威 L46「获得1层再生」）。</summary>
    internal const int RegenPerTrigger = 1;

    /// <summary>
    ///     战斗结束时**每层【再生】换到的生命上限**。
    ///     ★ 2026-10-07 用户裁定：「1 层再生 = +1 生命上限」。
    ///     ⚠️ 换算用的"层数"是**本场【栖途】累计发放的层数**（= 本 Power 的 <c>Amount</c>），
    ///     不是"战斗结束那一刻身上还剩的【再生】层数" —— 口径理由见 <see cref="AfterCombatEnd" />。
    /// </summary>
    internal const int MaxHpPerRegen = 1;

    /// <summary>
    ///     打出【栖途】时施加的层数 —— <b>只用于把那枚 Power 挂到玩家身上</b>（引擎不接受 0 层的施加：
    ///     <c>PowerCmd.Apply</c> 在 <c>amount == 0</c> 时直接 return，实据 <c>PowerCmd.cs:107</c>）
    ///     ⇒ <c>Amount</c> 的起点是 1，真正的"触发次数"要把它减掉（见 <see cref="Triggers" />）。
    ///     ★ 卡牌侧**引用**本常量（不另写一份），两边不可能漂移。
    /// </summary>
    internal const int InitialStacks = 1;

    // ★ 2026-10-07：`_ratio` / `Ratio` / `RatioFromPercent` / `PercentFromRatio` / `SetRatio`
    //   全部随旧口径（"战后把 25% / 50% 的**临时生命上限**转成真实上限"）一并删除 ——
    //   新口径里根本没有"比例"这个量：敲后改的是**费用**（3 → 2 费），
    //   换算只剩"累计发放的【再生】层数 × 每层价值（MaxHpPerRegen）"一条乘法（见 AfterCombatEnd）。

    public override PowerType Type => PowerType.Buff;

    /// <summary>
    ///     ★ 可叠层：<c>Amount</c> = <see cref="InitialStacks" />（打出时那 1 层）+ 本场触发次数
    ///     ⇒ 每次触发发 <see cref="RegenPerTrigger" /> 层【再生】。
    ///     图标上的数字走 <see cref="DisplayAmount" />（= <see cref="Triggers" />，扣掉起点那 1 层），
    ///     玩家看到的就是"战后能换到多少点生命上限"。
    /// </summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    ///     本场【栖途】的**真实触发次数**（= 累计发放的【再生】层数 / <see cref="RegenPerTrigger" />）。
    ///     <para>★ 单一来源：战斗结束的换算与图标数字都读它，别处不要再各减一次。</para>
    /// </summary>
    internal int Triggers => Math.Max(0, (int)Amount - InitialStacks);

    /// <summary>图标上显示的数字 = 触发次数（而不是含起点 1 层的 <c>Amount</c>）。</summary>
    public override int DisplayAmount => Triggers;

    /// <summary>
    ///     ★★ <b>【银龙血统】刚刚为"掉血"触发了一次</b> —— 由遗物 <see cref="OrcaBloodline" />
    ///     在"当前生命下降"钩子里调用（只有那里知道这一下掉血是不是敌人打的）。
    ///
    ///     <para>权威 L46：<i>"在没有受到敌人伤害（烧血自残不算）的情况下触发【银龙血统】时，
    ///     获得1层再生"</i> ⇒ <paramref name="fromEnemy" /> 为真（这次掉血来自敌人）时**不发**；
    ///     自残（卖血）触发时发 <see cref="RegenPerTrigger" /> 层。</para>
    ///
    ///     <para>★ <b>为什么用 <c>ThrowingPlayerChoiceContext</c></b>：遗物的
    ///     <c>AfterCurrentHpChanged(Creature, decimal)</c> 签名里**没有** choice context，
    ///     而施加 Power 必须走 <c>PowerCmd.Apply</c>。引擎自己的同类场景就是这么写的
    ///     （实据：<c>PowerCmd.Decrement</c> 内部就是 <c>new ThrowingPlayerChoiceContext()</c>，
    ///     <c>PowerCmd.cs:185-188</c>）—— 这条路径上不可能发生玩家选择，所以"抛异常的上下文"是安全的。</para>
    /// </summary>
    /// <param name="me">奥卡的生物（= 本 Power 的 Owner）。</param>
    /// <param name="fromEnemy">这一下掉血是否由敌人造成（自残 = <c>false</c>）。</param>
    /// <param name="lost">这一下掉的血量（仅用于日志）。</param>
    internal async Task NoteBloodlineTrigger(Creature me, bool fromEnemy, int lost)
    {
        try
        {
            if (fromEnemy)
            {
                OrcaLog.Info($"[Orca] 栖途：这次【银龙血统】是**敌人打的**（掉血 {lost}）"
                           + " ⇒ 不发【再生】（权威：在没有受到敌人伤害、烧血自残不算，的情况下才发）", 2);
                return;
            }

            var ctx = new ThrowingPlayerChoiceContext();
            await PowerCmd.Apply<OrcaHomesteadPower>(ctx, me, RegenPerTrigger, me, null);
            await PowerCmd.Apply<RegenPower>(ctx, me, RegenPerTrigger, me, null);

            OrcaLog.Info($"[Orca] 栖途：自残触发【银龙血统】（掉血 {lost}）⇒ +{RegenPerTrigger} 层【再生】"
                       + $"（本场累计触发 {Triggers} 次 ⇒ 战后可转化 {Triggers * MaxHpPerRegen} 点生命上限）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 栖途·发放【再生】出错（本次不发）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★★ <b>浮窗里的两个数字由本类的常量注入</b>（A7 同一套路）：
    ///     <c>PowerModel.HoverTips</c> 不调 <c>AddExtraArgsToDescription</c> ⇒ 占位符只能在
    ///     <c>Description</c> 取值处补，否则 <c>powers.json</c> 里的 <c>{RegenPerTrigger}</c> /
    ///     <c>{MaxHpPerRegen}</c> 会裸露成字面量。
    ///     ★ 与卡面同源：两处都读本类的这两个常量（单一来源，改常量即两处同步）。
    /// </summary>
    public override LocString Description
    {
        get
        {
            var loc = base.Description;
            try
            {
                var raw = loc.GetRawText();
                if (raw.Contains("{RegenPerTrigger}"))
                {
                    loc.Add("RegenPerTrigger", (decimal)RegenPerTrigger);
                }
                if (raw.Contains("{MaxHpPerRegen}"))
                {
                    loc.Add("MaxHpPerRegen", (decimal)MaxHpPerRegen);
                }
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 栖途：读取浮窗文案原文失败，占位符未注入：{ex.Message}");
            }
            return loc;
        }
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        try
        {
            // ★★ 转化量 = 本场【栖途】**累计发放的【再生】层数**（= 本 Power 的 Amount）× 每层价值。
            //    ⚠️ 口径说明（为什么不用"战斗结束那一刻身上还剩的【再生】层数"）：
            //      引擎的 RegenPower 每个我方回合结束都会 Heal(Amount) 然后 Decrement 一层
            //      （反编译实据 RegenPower.cs:20-27）⇒ 战斗结束时的"剩余层数"通常只剩 0~2，
            //      按它转化会让这张先古牌的收益形同没有。权威原话是"根据再生层数转化为生命上限"，
            //      用户 2026-10-07 的裁定是"1 层 = +1 生命上限"—— 取**累计发放层数**才与这句话、
            //      以及"自残流的成长回报"这个定位一致。日志把两个数都打出来，便于实机核对口径。
            int granted = Triggers * RegenPerTrigger;         // 累计发放层数 = 触发次数 × 每次层数
            int regenStacksNow = (int)(Owner?.GetPower<RegenPower>()?.Amount ?? 0);
            int convert = granted * MaxHpPerRegen;
            if (convert <= 0)
            {
                OrcaLog.Info("[Orca] 栖途：本场一次都没触发过（累计发放 0 层【再生】）⇒ 不转化", 2);
                return;
            }

            var me = Owner;
            if (me != null)
            {
                await CreatureCmd.GainMaxHp(me, convert);     // 真实上限（会顺带回血，符合"转化"语义）

                // ★★ 与击杀奖励（龙剑/魔剑）同理：谁赚到**真实**上限，谁就把本场基准一起推进。
                //    本 Power 的 AfterCombatEnd 由引擎排在银龙血统**之前**（实据见类摘要），
                //    所以这里是"先转化、后收尾"：若不抬基准，遗物收尾那次绝对写回会按
                //    「基准 + 池子余量」重算 ⇒ 转化量被**多算一倍** ⇒ 战斗结束跳出 2×convert。
                //    抬基准之后：上限 = 新基准 + 池子余量，两边同时含这笔，幂等。
                //    拿不到遗物（理论上不该发生：银龙血统是奥卡的起始遗物）⇒ 记 Error + 上限照旧，不静默吞。
                //    ⚠️ 必须经 `Creature.Player` 才能拿到遗物：`PowerModel.Owner` 是 **Creature**（不是 Player），
                //      Creature 上的公开属性是 `Player`（Creature.cs:104，可空）⇒ 用 ?. 链式取，
                //      与同文件既有写法一致（见上方 Owner?.Player?.PlayerCombatState?...）。
                //      （首次提交曾误写成 Owner.GetRelic<...> ⇒ CS1061；**只有真编译能抓到** ✗）
                var bloodline = Owner?.Player?.GetRelic<OrcaBloodline>();
                if (bloodline != null)
                {
                    bloodline.AdvanceCombatBase(convert);
                }
                else
                {
                    OrcaLog.Error($"[Orca] 栖途：{convert} 点已转为真实生命上限，"
                                + "但**身上没有银龙血统** ⇒ 无法并进本场基准（收尾可能把这笔再加一次）");
                }

                OrcaLog.Info($"[Orca] 栖途：战斗结束 ⇒ 本场累计发放【再生】{granted} 层 × 每层 {MaxHpPerRegen} 点"
                         + $" = 共 {convert} 点转化为**真实生命上限**（当前上限 {me.MaxHp}）"
                         + $"；身上此刻还剩【再生】{regenStacksNow} 层（引擎每回合会衰减 1 层，故不作为换算依据）", 2);
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