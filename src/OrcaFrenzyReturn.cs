using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★★「狂躁系卡牌」的搬运 —— 牌**真的进了**弃牌堆 / 消耗堆之后，送回抽牌堆第一位。
///
///     <para>
///     <b>★ 挂的是哪个重载（2026-09-17 第四次修，用户："<i>用猎人杂技这种弃牌还是无法触发狂躁</i>"）</b>：
///     反编译 <c>CardPileCmd</c> 发现 <c>Add</c> 有三个重载，**前两个都只是转调**：
///     <code>
///     Add(CardModel, PileType, …)  →  await Add(card, pile, …)                       // 转调
///     Add(CardModel, CardPile, …)  →  (await Add(new[]{card}, pile, …))[0]           // 转调**批量版**
///     Add(IEnumerable&lt;CardModel&gt;, CardPile, …)  ← ★ **真正的实现**（做事的地方）
///     </code>
///     而 <c>CardCmd.Discard</c> → <c>DiscardAndDraw</c> 走的是**后面那条路**（猎人的杂技就是它），
///     于是**绕过了**一开始只挂在 <c>PileType</c> 版上的补丁 ⇒ 杂技弃牌不触发；
///     而狂躁（回合结束那条链）恰好经过 <c>PileType</c> 版 ⇒ 能触发。
///     ⇒ 现在**只挂真正的实现（批量版）**，所有路径（主动打出/弃牌/消耗/杂技/魔典）全覆盖。
///     </para>
///
///     <para>
///     <b>为什么不在狂躁的 <c>OnTurnEndInHand</c> 里直接搬</b>（第二次修的原因）：
///     <c>CombatManager</c> 在 <c>OnTurnEndInHandWrapper</c> **之后**还会无条件
///     <c>CardPileCmd.Add(card, PileType.Discard…)</c> ⇒ 在那里搬会被**立刻覆盖**
///     （实机："有进抽牌堆的动画，但牌没真的进去"）。
///     </para>
///
///     <para>
///     <b>为什么要等一帧</b>（第三次修）：搬运确实执行了，但打完牌那条链上还有清理步骤，
///     会把牌**挪回弃牌堆**（烧牌链短 ⇒ 成功；弃牌链长 ⇒ 失败）。所以 <c>await</c> 一个
///     <c>ProcessFrame</c> 让整条链先跑完，并在搬完**校验** <c>Pile?.Type == Draw</c>，失败重试一次。
///     </para>
///
///     <para>
///     ⚠️ 另有一个更早的坑：postfix 里的 <c>Task</c> 是 <b>async</b> 的，
///     不 await 就判 <c>IsCompletedSuccessfully</c> 永远为假 ⇒ 补丁从未生效过。
///     </para>
///     </summary>
[HarmonyPatch(typeof(CardPileCmd), "Add", new[]
{
    // ★ 真正的实现：IEnumerable<CardModel> 版（其余重载都转调到它）
    typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition),
    typeof(AbstractModel), typeof(bool), typeof(bool)
})]
internal static class OrcaFrenzyReturnPatch
{
    /// <summary>正在搬运中的牌（防重入：搬运本身就是一次 <c>Add</c>，不拦住会无限循环）。</summary>
    private static readonly HashSet<CardModel> _pending = new();

    private static void Postfix(Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        if (__result == null) return;
        TaskHelper.RunSafely(HandleAsync(__result));
    }

    private static async Task HandleAsync(Task<IReadOnlyList<CardPileAddResult>> task)
    {
        IReadOnlyList<CardPileAddResult> results;
        try
        {
            results = await task;                      // ★ 等它真的完成
        }
        catch
        {
            return;                                    // Add 自己失败了
        }

        if (results == null) return;

        foreach (var result in results)
        {
            try
            {
                if (!result.success) continue;         // ⚠️ CardPileAddResult 是 struct

                CardModel card = result.cardAdded;
                if (card is not OrcaFrenzyCard) continue;      // ★ 所有狂躁系卡

                PileType dest = result.targetPile;
                if (dest != PileType.Discard && dest != PileType.Exhaust) continue;

                if (!_pending.Add(card)) continue;             // 已在搬运中

                Player? owner = card.Owner;
                if (owner == null || card.CombatState == null)
                {
                    _pending.Remove(card);
                    continue;
                }

                Log.Info($"[Orca] 狂躁搬运：{card.Id.Entry} 进了 {dest} → 准备送回抽牌堆第一位", 2);
                _ = TaskHelper.RunSafely(ReturnAsync((OrcaCard)card, dest));
            }
            catch (Exception ex)
            {
                Log.Warn($"[Orca] 狂躁搬运（单张处理）出错：{ex.Message}", 2);
            }
        }
    }

    /// <summary>等一帧 → 搬 → 校验 → 不行再搬一次。</summary>
    private static async Task ReturnAsync(OrcaCard card, PileType from)
    {
        try
        {
            await NextFrame();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                await card.ReturnToDrawPileTop($"狂躁搬运(第{attempt}次)");

                await NextFrame();
                var now = card.Pile?.Type;
                if (now == PileType.Draw)
                {
                    Log.Info($"[Orca] 狂躁搬运成功：{card.Id.Entry} 现在在抽牌堆（原 {from}）", 2);
                    return;
                }

                Log.Warn($"[Orca] 狂躁搬运**没生效**（第{attempt}次后仍在 {now}）—— "
                         + (attempt == 1 ? "重试一次" : "放弃"), 2);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 狂躁搬运：送回抽牌堆失败（{card.Id.Entry}）：{ex.Message}", 2);
        }
        finally
        {
            _pending.Remove(card);
        }
    }

    /// <summary>等一个渲染帧（Godot）。拿不到 SceneTree 就直接返回，不阻塞。</summary>
    private static async Task NextFrame()
    {
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree && tree != null)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch
        {
            // 拿不到就不等
        }
    }
}
