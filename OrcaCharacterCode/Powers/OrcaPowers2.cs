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
///     ★ 栖途（卡牌）的 Power —— 战斗结束时，把**一部分战斗临时生命上限**
///     转化为**真实（永久）生命上限**（未敲 25%，敲后 50%）。
///
///     <para>用户口径：<i>"（选取遗物-欧洛巴斯之触会获得这张卡牌）战斗结束后，
///     将你 25% 的临时生命上限转化为真实生命上限"</i>；卡包2 补充"敲后 50%"。</para>
///
///     <para>⚠️ <b>比例存在 Power **实例**上（<c>_ratio</c>），不是 static</b> —— 它在卡牌出牌时
///     按该卡本局的 <c>IsUpgraded</c> 现算并写入。跨局泄漏（E4）的来龙去脉见 <c>_ratio</c> 的注释。</para>
///
///     <para>⚠️ <b>与银龙血统的配合</b>：遗物 <c>AfterCombatEnd</c> 会把**剩余**临时上限还原掉。
///     我们从共享池 <see cref="OrcaTempHp" /> 里**先 Consume 掉这个比例**，再加到真实上限上
///     ⇒ 遗物随后只会还原剩下的 75%（敲后 50%），不会把转化过的部分又扣回去。</para>
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
///     ⇒ 顺序是**对我们有利**的那一种：Consume 掉的那部分遗物根本不会去还原（池子已经小了）。
///     ⚠️ 但正因为遗物**后跑**，"GainMaxHp 这笔真实上限"必须**紧随其后抬基准**
///     （见 <c>OrcaBloodline.AdvanceCombatBase</c>，与龙剑/魔剑击杀奖励同一根因），
///     否则收尾那次绝对写回会把它**再多算一倍**。</para>
/// </summary>
public sealed class OrcaHomesteadPower : PowerModel
{
    /// <summary>
    ///     ★ 卡包2 原文："敲后50%" ⇒ 升级后比例 25% → 50%。
    ///
    ///     <para>⚠️ <b>E4 已修（跨局泄漏）</b>：这里**原来**是 <c>internal static bool UpgradedRatio</c>
    ///     + <c>static Ratio =&gt; UpgradedRatio ? 0.5m : 0.25m</c>，而它只由卡牌的
    ///     <c>OrcaHomestead.OnUpgrade()</c> 单向置 <c>true</c>、**全工程无任何一处复位**
    ///     ⇒ static 跨局共享 ⇒ <b>上一局敲过栖途，下一局开局就按敲后的 50% 结算</b> ✗。
    ///     现改为**本 Power 实例自己的字段**，由卡牌在**每次出牌时**按
    ///     <c>IsUpgraded</c> 重新写入（同 <c>OrcaVoidReturn.OnPlay</c> 的既有写法）
    ///     ⇒ "这局敲没敲"完全由卡牌自身状态推导，**没有跨局共享状态可泄漏** ✓。</para>
    ///
    ///     <para>⚠️ 这里<b>刻意</b>不再提供任何 static 的"全局比例"—— 比例是<b>本实例</b>的字段；
    ///     而这个功能的**数值定义**（25 / 50 两个百分点）就在本类里，卡牌的
    ///     <c>CurrentPercent</c> 只**引用**它（单一来源：卡面 / 日志 / 结算同源）✓</para>
    /// </summary>
    private decimal _ratio = RatioFromPercent(BasePercent);

    /// <summary>未敲的转化比例（百分点，= 卡面 25%）—— <b>本功能数值的唯一来源</b>。</summary>
    internal const int BasePercent = 25;

    /// <summary>敲后的转化比例（百分点，= 卡面 50%）。</summary>
    internal const int UpgradedPercent = 50;

    /// <summary>百分数 ↔ 比例的分母（100 = "百分数"这个单位的定义；两个换算方向共用）。</summary>
    private const int PercentBase = 100;

    /// <summary>当前转化比例（0.25 或 0.5）—— <b>本实例</b>的，随每次出牌按卡牌是否敲过重新写入。</summary>
    internal decimal Ratio => _ratio;

    /// <summary>
    ///     ★ <b>百分数 → 比例的**唯一**换算点</b>：把 <see cref="BasePercent" /> /
    ///     <see cref="UpgradedPercent" /> 一次性除成 <c>Ratio</c> 要用的 0.25 / 0.5，
    ///     免得 100 这个换算因子散落在多处（与 <c>OrcaBloodNirvanaPower.HealRatioDisplay</c>
    ///     的"显示 ↔ 结算同源"是同一套路，只是方向相反）。
    /// </summary>
    internal static decimal RatioFromPercent(int percent) => (decimal)percent / PercentBase;

    /// <summary>比例 → 百分数的**唯一**换算点（日志与卡面显示同源）。</summary>
    internal static int PercentFromRatio(decimal ratio) => (int)Math.Round(ratio * PercentBase);

    /// <summary>
    ///     ★ 由卡牌在出牌时写入**本实例**的转化比例（敲后 50%，否则 25%）。
    ///
    ///     <para>⚠️ 复用"同一个生物身上只有一份"的简化：与 <c>OrcaVoidReturnPower</c> 同理，
    ///     我们假设同一时刻身上至多只有一张栖途生效（用户口径：该牌由欧洛巴斯之触给一张）。
    ///     若将来真能同场叠两张（一敲一未敲），这里**后者覆盖前者** —— 记 Warn，不静默。</para>
    ///
    ///     <para>★ 边界显式：百分点非法 ⇒ 记 Warn 并拒绝写入（保持默认），绝不静默接受。
    ///     这里**不再**做"比例 ↔ 百分点互校" —— 数值只有本类这一份定义，
    ///     结构上已不存在"两处各写一份导致脱钩"的可能，那道自校没有对象了。</para>
    /// </summary>
    /// <param name="percent">未敲 / 敲后的百分点（只接受 <see cref="BasePercent" /> 或 <see cref="UpgradedPercent" />）。</param>
    internal void SetRatio(int percent)
    {
        if (percent != BasePercent && percent != UpgradedPercent)
        {
            OrcaLog.Warn($"[Orca] 栖途：收到非法的转化百分点 {percent}（合法值只有 {BasePercent} / {UpgradedPercent}）"
                       + $" ⇒ 本实例仍按 {PercentFromRatio(_ratio)}% 结算");
            return;
        }

        _ratio = RatioFromPercent(percent);
        OrcaLog.Info($"[Orca] 栖途：本实例转化比例已按本局卡牌状态写入 = {PercentFromRatio(_ratio)}%"
                   + $"（本局{(percent == UpgradedPercent ? "**已敲**" : "**未敲**")}；仅作用于本实例，无跨局静态状态）");
    }

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

                // ★★ 与击杀奖励（龙剑/魔剑）同理：谁赚到**真实**上限，谁就把本场基准一起推进。
                //    本 Power 的 AfterCombatEnd 由引擎排在银龙血统**之前**（实据见类摘要），
                //    所以这里是"先转化、后收尾"：若不抬基准，遗物收尾那次绝对写回会按
                //    「基准 + 池子余量」重算 ⇒ 转化量被**多算一倍** ⇒ 战斗结束跳出 2×used。
                //    抬基准之后：上限 = 新基准 + 池子余量，两边同时含这笔，幂等。
                //    拿不到遗物（理论上不该发生：银龙血统是奥卡的起始遗物）⇒ 记 Error + 上限照旧，不静默吞。
                //    ⚠️ 必须经 `Creature.Player` 才能拿到遗物：`PowerModel.Owner` 是 **Creature**（不是 Player），
                //      Creature 上的公开属性是 `Player`（Creature.cs:104，可空）⇒ 用 ?. 链式取，
                //      与同文件既有写法一致（见上方 Owner?.Player?.PlayerCombatState?...）。
                //      （首次提交曾误写成 Owner.GetRelic<...> ⇒ CS1061；**只有真编译能抓到** ✗）
                var bloodline = Owner?.Player?.GetRelic<OrcaBloodline>();
                if (bloodline != null)
                {
                    bloodline.AdvanceCombatBase(used);
                }
                else
                {
                    OrcaLog.Error($"[Orca] 栖途：{used} 点已转为真实生命上限，"
                                + "但**身上没有银龙血统** ⇒ 无法并进本场基准（收尾可能把这笔再加一次）");
                }

                // ⚠️ 这里刻意**不用** `{Ratio:P0}` 那种"格式化成百分比"的写法：中文/日文 locale 下
                //    `P` 格式串会带上本地化的百分号与不换行空格，日志里对不上卡面的"25 / 50"。
                //    改走与卡面同一个整数口径（PercentFromRatio），日志与卡面必然同源。
                OrcaLog.Info($"[Orca] 栖途：战斗结束 ⇒ 临时生命上限 {pool} 的 {PercentFromRatio(Ratio)}%"
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