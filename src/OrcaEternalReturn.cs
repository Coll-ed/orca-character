using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★「嗜血龙剑」的回抽牌堆（用户口径 2026-09-16）：
///     <i>"如果没有被打出的情况下就进入消耗/弃牌堆，将重新回到抽牌堆第一位"</i>。
///
///     <para>
///     挂在 <c>CardPileCmd.Add(CardModel, PileType, CardPilePosition, AbstractModel, Boolean)</c> 的 postfix。
///     </para>
///
///     <para>
///     ★★★ <b>必须写死参数类型数组</b>（`new[] { typeof(CardModel), typeof(PileType), ... }`）：
///     <c>CardPileCmd.Add</c> 有两个重载**返回类型完全相同**——
///       <c>Add(CardModel, PileType, CardPilePosition, AbstractModel, bool)</c> 与
///       <c>Add(IEnumerable&lt;CardModel&gt;, PileType, CardPilePosition, AbstractModel, bool)</c>
///     都返回 <c>Task&lt;CardPileAddResult&gt;</c>。只写方法名 "Add" 时 Harmony 报
///     <c>Ambiguous match found for '… Add(CardModel, PileType, …)'</c> 并**静默放弃这个补丁**
///     （2026-09-16 实机日志实锤：<c>[Orca] Harmony 补丁: 19 成功 / 1 失败</c>）⇒ 表现就是"回到抽牌堆不生效"。
///     </para>
///
///     <para>
///     ★ 原版回合结束链路（反编译 <c>CombatManager.ResolveTurnEndCardEffects</c>）确认会走这条路：
///     <code>
///     await card.OnTurnEndInHandWrapper(choiceContext);
///     return !card.Keywords.Contains(CardKeyword.Ethereal)
///         ? await CardPileCmd.Add(card, PileType.Discard.GetPile(card.Owner), CardPilePosition.Bottom, null, skipVisuals: true)
///         : await CardCmd.Exhaust(...);
///     </code>
///     </para>
///
///     <para>
///     ⚠️ <b>不要再给它加 <c>CardKeyword.Retain</c></b>：用户实测「不能有保留！狂躁无法自动打出」——
///     Retain 会让它回合结束仍留在手牌 ⇒ 永远不进弃牌堆 ⇒ ① 这里不触发；② 狂躁链条断掉。
///     它的回手路径是**正常循环**：弃牌堆 →（本补丁）→ 抽牌堆 → 下回合抽到手里 → 回合结束触发狂躁。
///     </para>
///
///     <para>
///     ★ 防重入：搬运本身也是一次 <c>Add</c>，不拦住就无限循环。用按**牌**记录的 <see cref="_pending"/>
///     而不是 bool 标志 —— <c>Add</c> 是异步的，postfix 一返回 bool 就被清掉了。
///     另外 <c>Postfix</c> 不能 <c>await</c>，直接 <c>.Wait()</c> 会死锁 ⇒ 用 <c>TaskHelper.RunSafely</c> 起异步。
///     </para>
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), "Add", new[]
{
    typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)
})]
internal static class OrcaEternalReturnPatch
{
    /// <summary>正在被搬回抽牌堆的牌（防自己触发自己）。</summary>
    private static readonly HashSet<CardModel> _pending = new();

    /// <summary>
    ///     postfix 的 <c>__result</c> 类型**必须是 <c>Task&lt;CardPileAddResult&gt;</c>**（不是解包后的结构体）。
    ///
    ///     <para>
    ///     ★★ 这里连踩两次，两种报错要分清：
    ///     <list type="number">
    ///       <item>只写方法名（没有参数类型数组）⇒
    ///         <c>Ambiguous match found for '… Add(CardModel, PileType, …)'</c>
    ///         （<c>Add</c> 两个重载返回类型完全相同，Harmony 选不出来）；</item>
    ///       <item>参数类型数组写对了，但 <c>__result</c> 声明成 <c>CardPileAddResult</c> ⇒
    ///         <c>Cannot assign method return type Task`1[CardPileAddResult] to __result type CardPileAddResult</c>。</item>
    ///     </list>
    ///     原因：目标方法是 <c>async Task&lt;CardPileAddResult&gt;</c>，
    ///     Harmony 的 <c>__result</c> 映射的是**方法签名上的返回类型**（也就是那个 <c>Task</c> 对象），
    ///     不会替我们解包 —— 要拿里面的值得自己 <c>await</c>/<c>.Result</c>。
    ///     这里因为 postfix 里只用来读"落点/成功与否"，改成 <c>Task</c> 版本后取 <c>Result</c> 即可。
    ///     </para>
    /// </summary>
    private static void Postfix(Task<CardPileAddResult> __result)
    {
        try
        {
            if (__result == null || !__result.IsCompletedSuccessfully) return;

            CardPileAddResult result = __result.Result;
            if (!result.success) return;

            CardModel card = result.cardAdded;
            if (card is not OrcaBloodSword) return;

            // 只在"进了弃牌堆 / 消耗堆"时动手（抽牌堆/手牌/出牌区一律放过）
            PileType dest = result.targetPile;
            if (dest != PileType.Discard && dest != PileType.Exhaust) return;

            if (!_pending.Add(card)) return;                  // 已经在搬运中 ⇒ 放过（也顺手挡住了自己）

            Player? owner = card.Owner;
            if (owner == null || card.CombatState == null)    // 战斗已结束/正在收尾：别再往抽牌堆塞
            {
                _pending.Remove(card);
                return;
            }

            Log.Info($"[Orca] 永恒：{card.Id.Entry} 进入 {dest} → 回到抽牌堆**第一位**", 2);
            TaskHelper.RunSafely(PutOnTopOfDrawPile(owner, card));
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 永恒搬运出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     塞到抽牌堆顶。<c>CardPilePosition.Top</c> = 第一位 ⇒ 下一次抽牌第一张就是它。
    /// </summary>
    private static async Task PutOnTopOfDrawPile(Player owner, CardModel card)
    {
        try
        {
            await CardPileCmd.Add(card, PileType.Draw.GetPile(owner), CardPilePosition.Top);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 永恒：回抽牌堆失败（{card.Id.Entry}）：{ex.Message}", 2);
        }
        finally
        {
            _pending.Remove(card);
        }
    }
}