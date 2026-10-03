using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     把奥卡的三个池子挂进 <c>ModelDb</c> 的全局列表（**这是卡牌/遗物/药水能不能被游戏"看见"的关键**）。
///
///     症状（用户实测）：
///       · 百科（card library）里看不到打击 / 防御 / 龙族魔典 / 嗜血龙剑；
///       · 打小怪时牌不进牌堆；
///       · 日志里别的模组都有 `[Content] Registered card: … -> XxxCardPool`，我们一条都没有。
///
///     原因：光在 <c>OrcaCardPool.GenerateAllCards()</c> 里声明卡片**不够** —— 池子本身要出现在
///     <c>ModelDb.AllCardPools</c>（以及角色池/遗物池/药水池的对应列表）里，游戏才会去枚举它。
///     这和我们当初必须给 <c>ModelDb.get_AllCharacters</c> 打补丁才能让角色出现在选人界面是同一类问题。
///
///     做法：给这几个 getter 挂 postfix，把我们的池子补进去（用 Distinct 语义防止重复）。
///
///     <para>
///     ★★ 2026-09-16 补：还有一处**独立**的入口 —— <c>ModelDb.AllCards</c>（扁平的全部卡列表）。
///     百科的卡片网格读的就是它（<c>NCardLibraryGrid._Ready()</c>），光补 <c>AllCardPools</c> 不够，
///     见 <see cref="OrcaAllCardsPatch" />。
///     </para>
/// </summary>
internal static class OrcaPoolRegistry
{
    private static bool _loggedCards;
    private static bool _loggedRelics;
    private static bool _loggedAllCards;

    internal static void AddCardPool(ref IEnumerable<CardPoolModel> result, string where)
    {
        try
        {
            var mine = ModelDb.CardPool<OrcaCardPool>();
            if (mine == null) { OrcaLog.Warn($"[Orca] 卡池注入失败：ModelDb.CardPool<OrcaCardPool>() 返回 null（{where}）", 2); return; }

            var list = result?.ToList() ?? new List<CardPoolModel>();
            if (!list.Contains(mine)) list.Add(mine);
            result = list;

            if (!_loggedCards)
            {
                _loggedCards = true;
                var ids = mine.AllCards?.Select(c => c.Id.Entry).ToArray() ?? Array.Empty<string>();
                OrcaLog.Info($"[Orca] 卡池已挂进 {where}（池内 {ids.Length} 张：{string.Join(", ", ids)}）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 卡池注入 {where} 出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★★ 把奥卡的卡补进**扁平的卡牌总列表**（<c>ModelDb.AllCards</c>）。
    ///
    ///     <para>
    ///     百科的卡片网格（<c>NCardLibraryGrid._Ready()</c>）就是遍历这个列表来建格的：
    ///     <code>
    ///     foreach (CardModel allCard in ModelDb.AllCards)
    ///         if (allCard.ShouldShowInCardLibrary) _allCards.Add(allCard);
    ///     </code>
    ///     它**不经过** <c>AllCardPools</c> ⇒ 只补池子的话，新卡在百科里一张都看不到。
    ///     </para>
    /// </summary>
    internal static void AddCards(ref IEnumerable<CardModel> result, string where)
    {
        try
        {
            var mine = ModelDb.CardPool<OrcaCardPool>()?.AllCards?.ToList();
            if (mine == null || mine.Count == 0)
            {
                OrcaLog.Warn($"[Orca] 卡牌总列表注入失败：取不到 OrcaCardPool 的卡（{where}）", 2);
                return;
            }

            var list = result?.ToList() ?? new List<CardModel>();
            int added = 0;
            foreach (var card in mine)
            {
                if (card == null || list.Contains(card)) continue;
                list.Add(card);
                added++;
            }
            result = list;

            if (!_loggedAllCards)
            {
                _loggedAllCards = true;
                OrcaLog.Info($"[Orca] 卡牌总列表（{where}）已补入 {added} 张奥卡卡牌"
                         + $"（列表原有 {list.Count - added} 张）：{string.Join(", ", mine.Select(c => c.Id.Entry))}", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 卡牌总列表 {where} 注入出错：{ex.Message}", 2);
        }
    }

    internal static void AddRelicPool(ref IEnumerable<RelicPoolModel> result, string where)
    {
        try
        {
            var mine = ModelDb.RelicPool<OrcaRelicPool>();
            if (mine == null) { OrcaLog.Warn($"[Orca] 遗物池注入失败：ModelDb.RelicPool<OrcaRelicPool>() 返回 null（{where}）", 2); return; }

            var list = result?.ToList() ?? new List<RelicPoolModel>();
            if (!list.Contains(mine)) list.Add(mine);
            result = list;

            if (!_loggedRelics)
            {
                _loggedRelics = true;
                var ids = mine.AllRelics?.Select(r => r.Id.Entry).ToArray() ?? Array.Empty<string>();
                OrcaLog.Info($"[Orca] 遗物池已挂进 {where}（池内 {ids.Length} 件：{string.Join(", ", ids)}）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 遗物池注入 {where} 出错：{ex.Message}", 2);
        }
    }

    internal static void AddPotionPool(ref IEnumerable<PotionPoolModel> result, string where)
    {
        try
        {
            var mine = ModelDb.PotionPool<OrcaPotionPool>();
            if (mine == null) return;

            var list = result?.ToList() ?? new List<PotionPoolModel>();
            if (!list.Contains(mine)) list.Add(mine);
            result = list;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 药水池注入 {where} 出错：{ex.Message}", 2);
        }
    }
}

/// <summary>全部卡池（百科 / 卡池枚举都读它）。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllCardPools")]
internal static class OrcaAllCardPoolsPatch
{
    private static void Postfix(ref IEnumerable<CardPoolModel> __result)
        => OrcaPoolRegistry.AddCardPool(ref __result, "AllCardPools");
}

/// <summary>角色卡池（按角色筛卡时读它）。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllCharacterCardPools")]
internal static class OrcaAllCharacterCardPoolsPatch
{
    private static void Postfix(ref IEnumerable<CardPoolModel> __result)
        => OrcaPoolRegistry.AddCardPool(ref __result, "AllCharacterCardPools");
}

/// <summary>
///     ★★ 卡牌**总列表** —— 百科网格读的就是它（`NCardLibraryGrid._Ready()`）。
///     不补这一条，新卡在百科里一张都看不到。
///
///     <para>
///     ⚠️ 用 <c>TargetMethods</c> 而不是直接写名字：<c>ModelDb</c> 上可能有多处 <c>AllCards</c> 声明
///     （`_api\\sts2.api.txt` 里就有 3 条 PROP 记录），一次性把它们**全部**挂上，哪个被 UI 走到都不漏。
///     这也是本项目踩过"Harmony 重载歧义 ⇒ 补丁被静默放弃"之后的标准做法。
///     </para>
/// </summary>
[HarmonyPatch]
internal static class OrcaAllCardsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var found = AccessTools.GetDeclaredProperties(typeof(ModelDb))
            .Where(p => p.Name == "AllCards")
            .Select(p => p.GetGetMethod(true))
            .OfType<MethodBase>()
            .ToList();

        OrcaLog.Info($"[Orca] AllCards 补丁目标：找到 {found.Count} 个 getter", 2);
        return found;
    }

    private static void Postfix(ref IEnumerable<CardModel> __result)
        => OrcaPoolRegistry.AddCards(ref __result, "AllCards");
}

/// <summary>全部遗物池。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllRelicPools")]
internal static class OrcaAllRelicPoolsPatch
{
    private static void Postfix(ref IEnumerable<RelicPoolModel> __result)
        => OrcaPoolRegistry.AddRelicPool(ref __result, "AllRelicPools");
}

/// <summary>角色遗物池（起始遗物就是从这里出的）。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllCharacterRelicPools")]
internal static class OrcaAllCharacterRelicPoolsPatch
{
    private static void Postfix(ref IEnumerable<RelicPoolModel> __result)
        => OrcaPoolRegistry.AddRelicPool(ref __result, "AllCharacterRelicPools");
}

/// <summary>药水池（目前是空池，挂上以免以后忘）。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllPotionPools")]
internal static class OrcaAllPotionPoolsPatch
{
    private static void Postfix(ref IEnumerable<PotionPoolModel> __result)
        => OrcaPoolRegistry.AddPotionPool(ref __result, "AllPotionPools");
}