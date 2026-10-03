using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;

namespace OrcaCharacter;

/// <summary>
///     ★★「**狂躁**」—— 独立出来的机制基类（用户口径 2026-09-16：
///     <i>"狂躁单独成一个类！后面方便我扩展卡牌"</i>）。
///
///     <para>
///     狂躁 = 三件事，全部集中在这里，**新卡继承本类即可复用**：
///     <list type="number">
///       <item><b>门槛</b>：<c>HasTurnEndInHandEffect =&gt; true</c>
///         （不 override 它，游戏根本不会调用 <c>OnTurnEndInHand</c>）；</item>
///       <item><b>触发</b>：回合结束仍在手牌 ⇒ <c>CardCmd.AutoPlay</c> 打出 —— 这就是"狂躁那一趟"；</item>
///     </list>
///     </para>
///
///     <para>
///     子类**只需要实现 <see cref="OnFrenzyPlay" />**（狂躁打出的那一下做什么）。
///     "主动打出"的分支照旧写在子类自己的 <c>OnPlay</c> 里，用 <see cref="IsFrenzyPlay" /> 分流。
///     </para>
///
///     <para>
///     ⚠️ <b>为什么不能只看 <c>play.IsAutoPlay</c></b>：**魔典打出的牌底层也走 AutoPlay**
///     （<c>IsAutoPlay = true</c>），但那是"正常使用"、不是狂躁 ⇒ 必须配合
///     <see cref="OrcaCard.ConsumeViaCodex" /> 那个一次性标记一起判断（见 <see cref="IsFrenzyPlay" />）。
///     </para>
///
///     <para>
///     「回抽牌堆第一位」是给"**没被打出**就进了弃牌堆 / 消耗堆"那些牌（被烧、被丢）的**补偿**，
///     （历史教训：早先"给 <c>CardPileCmd.Add</c> 挂全路径 postfix"那种写法从未生效 ——
///     <c>Add</c> 是 async，postfix 拿到的 Task 还没跑完，<c>!IsCompletedSuccessfully</c> 恒为真。）
///     </para>
/// </summary>
public abstract class OrcaFrenzyCard : OrcaCard
{
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
            Log.Info($"[Orca] {FrenzyName}·狂躁：已不在手牌（当前 {pile}）⇒ 回到抽牌堆，结束", 2);
            await ReturnToDrawPileTop("狂躁");
            return;
        }

        // ② 在手牌（本方法被调用时原版已挪到 Play pile，仍属这条链）⇒ 执行打出
        Log.Info($"[Orca] {FrenzyName}·狂躁触发：回合结束仍在手牌 → 自动打出", 2);
        await CardCmd.AutoPlay(ctx, this, null);
    }
}