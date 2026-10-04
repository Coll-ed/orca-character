using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★★「狂躁」标签**自带的两个回归效果**（用户口径 2026-10-05，逐字）：
///     <i>"关于被丢弃与被消耗，我们记得是写在狂躁那里的
///     而狂躁标签又自带了2端代码：奇巧，消耗联动（如战鼓）
///     奇巧触发后，这场牌回到抽牌堆第一位
///     （代码类似战鼓的消耗获得能量）消耗触发同理"</i>。
///
///     <para><b>两条路径，各自挂在原版对应的钩子上</b>：
///     <list type="bullet">
///       <item><b>被丢弃</b>（奇巧那端）⇒ <see cref="AfterCardDiscarded" />；</item>
///       <item><b>被消耗</b>（消耗联动那端，同战鼓）⇒ <see cref="AfterCardExhausted" />。</item>
///     </list></para>
///
///     <para>★★ <b>为什么这两个钩子天然不会在"正常打出"时触发</b>（反编译官方 <c>sts2.dll</c> 实据）：
///     <code>
///     // CardCmd.DiscardAndDraw —— 「被丢弃」的唯一漏斗
///     await CardPileCmd.Add(card, discardPile);                    // 先进弃牌堆
///     CombatManager.Instance.History.CardDiscarded(combatState, card);
///     await Hook.AfterCardDiscarded(combatState, choiceContext, card);   // ★ 才轮到本钩子
///
///     // CardCmd.Exhaust —— 「被消耗」的唯一漏斗
///     await CardPileCmd.Add(card, PileType.Exhaust, CardPilePosition.Bottom, null, skipVisuals);
///     CombatManager.Instance.History.CardExhausted(combatState, card);
///     await Hook.AfterCardExhausted(combatState, choiceContext, card, causedByEthereal);  // ★ 才轮到本钩子
///     </code>
///     ⇒ 两者都只在**被"丢弃/消耗"效果处理**时发出；而牌被**正常打出**后落进弃牌堆，
///     是出牌流程的收尾，**不经过**它们（<c>CardPileCmd.Add</c> 本身不发这两个 Hook）。
///     所以龙剑按正常流程进弃牌堆时，这里根本不会被调到 —— 用户口径
///     <i>"我们龙剑是一个正常打出后会按照流程进入弃牌堆的一张正常牌，狂躁保证了出手率"</i>
///     由此天然成立，**不需要任何"打没打过"的判据**。</para>
///
///     <para>★ <b>为什么改挂钩子、而不是继续用 <c>CardPileCmd.Add</c> 的 Harmony 补丁</b>：
///     旧实现在 <c>Add</c> 上做了「进弃牌堆/消耗堆就搬走」的**全路径**拦截，区分不出
///     "正常打出落进弃牌堆"与"被丢弃效果丢进弃牌堆"，于是把正常打出的牌也搬走了
///     （2026-10-05 实机反馈：「你正常打出会回抽牌堆，而不是回到弃牌堆」）。
///     原版对「被丢弃」本来就有专门信号（Sly / 奇巧 走的就是它：
///     <c>CardCmd.DiscardAndDraw</c> 收集 <c>IsSlyThisTurn</c> 的牌后
///     <c>AutoPlay(…, AutoPlayType.SlyDiscard)</c>）⇒ 用信号、不用拦截，才和原版同构。</para>
///
///     <para>⚠️ <b>没有 <c>Retain</c> 的份</b>：用户实测「不能有保留！狂躁无法自动打出」——
///     带保留会让牌回合结束仍在手牌，狂躁链条直接断掉。</para>
/// </summary>
public abstract class OrcaFrenzyCard : OrcaCard
{
    /// <summary>日志 / 返回原因里用的标签名（与用户口径的"奇巧 / 消耗联动"两端对应）。</summary>
    private const string DiscardReturnReason = "狂躁（奇巧：被丢弃）";

    /// <summary>同上，消耗那端的标签名（对齐战鼓的消耗联动）。</summary>
    private const string ExhaustReturnReason = "狂躁（消耗联动：被消耗）";

    /// <summary>日志里用的名字（子类可覆盖，如"嗜血龙剑" / "嗜血魔剑"）。</summary>
    protected virtual string FrenzyName => Id.Entry;

    /// <summary>★ 门槛：不 override 成 true，游戏不会调用 <see cref="OnTurnEndInHand" />。</summary>
    public override bool HasTurnEndInHandEffect => true;

    protected OrcaFrenzyCard(int cost, CardType type, CardRarity rarity, TargetType target)
        : base(cost, type, rarity, target)
    {
    }

    /// <summary>
    ///     ★ **本次是不是狂躁那一趟**。子类在 <c>OnPlay</c> 开头这样用：
    ///     <code>
    ///     if (IsFrenzyPlay(play)) { await OnFrenzyPlay(ctx, play); return; }
    ///     // …否则就是主动打出 / 魔典打出 ⇒ 走各自的"强化"分支
    ///     </code>
    ///     ⚠️ 它会**消费**魔典标记（一次性）⇒ 每次 <c>OnPlay</c> 只能调用一次。
    /// </summary>
    protected bool IsFrenzyPlay(CardPlay play)
    {
        // 每次打出前清掉"0 伤害"标记，防止上一次的残留影响这一次。
        return play.IsAutoPlay && !ConsumeViaCodex();
    }

    /// <summary>★ **狂躁打出时做什么** —— 子类实现（例如"对所有敌人造成伤害附加"）。</summary>
    protected abstract Task OnFrenzyPlay(PlayerChoiceContext ctx, CardPlay play);

    /// <summary>
    ///     ★ 狂躁触发（回合结束）：**先检测自己在不在手牌**，再决定做什么。
    ///
    ///     <para><b>用户口径（goal 原文）</b>：
    ///     <i>"狂躁标签执行自己打出命令时**检测自己在不在手牌里面，不在就回到抽牌堆结束，否则执行打出**"</i>。</para>
    ///
    ///     <para>⚠️ 反编译实据（`CombatManager.DoTurnEndCards`，顺序是死的）：
    ///     <code>
    ///     AddTurnEndCardToPlayPileWithDelay(card)   // ① 先把牌挪到 **Play pile**
    ///     ResolveTurnEndCardEffects(card, …)        // ② 才轮到本方法
    ///     TweenTurnEndCardToResultPile(…)           // ③ 之后进弃牌堆
    ///     </code>
    ///     ⇒ 本方法里 `Pile.Type` 正常就是 **Play**，所以"在手牌这条链上"要**同时接受 Hand 与 Play**；
    ///     只有真被移走（弃牌堆 / 消耗堆 / 抽牌堆）才算"**不在手牌**" ⇒ 回到抽牌堆结束。</para>
    /// </summary>
    protected sealed override async Task OnTurnEndInHand(PlayerChoiceContext ctx)
    {
        var pile = Pile?.Type;

        // ① 不在手牌（已被别的效果移走）⇒ 回到抽牌堆结束
        if (pile != null && pile != PileType.Hand && pile != PileType.Play)
        {
            OrcaLog.Info($"[Orca] {FrenzyName}·狂躁：已不在手牌（当前 {pile}）⇒ 回到抽牌堆，结束", 2);
            await ReturnToDrawPileTop("狂躁（回合结束时不在手牌）");
            return;
        }

        // ② 在手牌（本方法被调用时原版已挪到 Play pile，仍属这条链）⇒ 执行打出
        OrcaLog.Info($"[Orca] {FrenzyName}·狂躁触发：回合结束仍在手牌 → 自动打出", 2);
        await CardCmd.AutoPlay(ctx, this, null);
    }

    /// <summary>
    ///     ★ **被丢弃 ⇒ 回到抽牌堆第一位**（狂躁自带的「奇巧」那端）。
    ///     <para>触发点在 <c>CardCmd.DiscardAndDraw</c>：牌先进弃牌堆，再发本信号。</para>
    /// </summary>
    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card != this) return;                       // 只认自己（钩子是发给全场所有人的）

        try
        {
            OrcaLog.Info($"[Orca] {FrenzyName}·狂躁：被**丢弃** ⇒ {DiscardReturnReason}，回到抽牌堆第一位", 2);
            await ReturnToDrawPileTop(DiscardReturnReason);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] {FrenzyName}·狂躁：被丢弃后回抽牌堆失败：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★ **被消耗 ⇒ 回到抽牌堆第一位**（狂躁自带的「消耗联动」那端，与战鼓
    ///     <c>DrumOfBattle.AfterCardExhausted</c> 同一挂点）。
    ///     <para>触发点在 <c>CardCmd.Exhaust</c>：牌先进消耗堆，再发本信号。
    ///     <c>causedByEthereal</c> 不影响回归 —— 虚无导致的消耗同样是"被消耗"。</para>
    /// </summary>
    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if (card != this) return;                       // 只认自己

        try
        {
            var how = causedByEthereal ? "（虚无）" : string.Empty;
            OrcaLog.Info($"[Orca] {FrenzyName}·狂躁：被**消耗**{how} ⇒ {ExhaustReturnReason}，回到抽牌堆第一位", 2);
            await ReturnToDrawPileTop(ExhaustReturnReason);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] {FrenzyName}·狂躁：被消耗后回抽牌堆失败：{ex.Message}", 2);
        }
    }
}
