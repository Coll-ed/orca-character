using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;

namespace OrcaCharacter;

/// <summary>
///     卡牌扩充第二版 · 两张**特殊来源**卡的获取途径（用户口径 2026-09-17）：
///     <list type="bullet">
///       <item><b>栖途</b> —— 选取先古遗物「**欧洛巴斯之触**」时获得
///         （设计原文：<i>"（选取遗物-欧洛巴斯之触会获得这张卡牌）"</i>）；</item>
///       <item><b>逆鳞</b> —— <i>"击败 BOSS 后有概率掉落"</i>（一改是必给，二改成概率）。</item>
///     </list>
///
///     <para>两张都**不在随机池**里（逆鳞按设计"不可被获得"被 <c>FilterThroughEpochs</c> 排除），
///     所以只能由这里显式塞进牌组。</para>
/// </summary>
internal static class OrcaPack2Acquisition
{
    /// <summary>
    ///     把一张卡加进玩家牌组（**已有的同类卡就不再给**，避免重复）。
    ///     全部包 try/catch：给卡失败绝不影响别的流程。
    /// </summary>
    internal static async Task GrantToDeck(Player? player, CardModel? card, string reason)
    {
        try
        {
            if (player == null || card == null) return;
            if (player.Character is not Orca) return;                 // 只对奥卡生效

            var deck = player.Deck;
            if (deck != null && deck.Cards.Any(c => c.GetType() == card.GetType()))
            {
                OrcaLog.Info($"[Orca] {reason}：牌组里已有【{card.Title}】⇒ 不重复给", 2);
                return;
            }

            // ★ A4 修复（2026-10-01）：原来直接用 AddCard(原型) ✗ ⇒ 引擎抛
            //   「Canonical model … used in incorrect place」✗ ⇒ 卡永远掉不出来 ✓
            //   API 实据（ilspy 反编译 sts2.dll，RunState）：CreateCard(canonicalCard, owner)
            //   的内部实现就是 canonicalCard.ToMutable() → AddCard → AfterCreated ✓
            //   ⇒ 用 CreateCard 才会把原型变成【可变副本】再进牌组 ✓
            // ★ A4-b 修复（2026-10-01）：CreateCard 只是"造出一张可变副本并登记进 RunState"✗
            //   —— RunState.AddCard 的正文只做 card.Owner = owner + 进私有 _allCards，**不碰牌组** ✗
            //   ⇒ 必须紧接着把它放进牌堆，否则卡到不了 player.Deck（去重也永不成立 ⇒ 每次 BOSS 战都重发）✗
            //   API 实据（ilspy 反编译 sts2.dll）：CardPileCmd.AddCursesToDeck 的标准写法就是
            //       CardModel card = owner.RunState.CreateCard(curse, owner);
            //       results.Add(await Add(card, PileType.Deck));          ← ★ 这一句才真正入牌组 ✓
            var added = player.RunState.CreateCard(card, player);
            // ★ 2026-10-04 恢复（原被 /*TOGGLE-OFF-A4*/ 注释成 await Task.CompletedTask）：
            //   这一句才真正把卡放进牌组。关掉它的后果是栖途**永远进不了牌组**
            //   （权威「选取遗物-欧洛巴斯之触会获得这张卡牌」不兑现），且下面的去重判断永远为假。
            var result = await CardPileCmd.Add(added, PileType.Deck);

            // ★ 边界显式：只在**真的入牌组**时才报成功，不再无条件输出"（已入牌堆…）"。
            //   CardPileAddResult.success 实据（ilspy 反编译 sts2.dll，struct CardPileAddResult）：
            //     public bool success;   —— "Whether we were successful in adding the card to a pile."
            int deckCount = player.Deck?.Cards.Count() ?? -1;
            if (result.success)
            {
                OrcaLog.Info($"[Orca] {reason} ⇒ 获得卡牌【{card.Title}】（已入牌堆，牌组现有 {deckCount} 张）", 2);
            }
            else
            {
                // 失败原因也记下来（旧牌堆 / 目标堆），不静默吞掉
                OrcaLog.Warn($"[Orca] {reason} ⇒ 卡牌【{card.Title}】**未能**入牌堆"
                             + $"（success=false，目标堆={result.targetPile}，原堆={result.oldPile?.Type.ToString() ?? "（无）"}，"
                             + $"牌组现有 {deckCount} 张）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 给卡失败（{reason}）：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     ★★★ **把先古事件里那个「欧洛巴斯之触」遗物格，换成给【栖途】的卡牌选项**
///     （用户口径 2026-10-05：<i>"我确定是战后奖励这种"</i> + <i>"一个遗物格 → 换成卡牌格"</i>）。
///
///     <para><b>链条（逐环有反编译实据；全量导出 3538 个 .cs 后搜 <c>TouchOfOrobas</c> 定位到）</b>：
///     先古事件发生在**打完 Boss 之后** ⇒ 它的选项页就是用户说的"战后奖励界面"：
///     <code>
///     // MegaCrit.Sts2.Core.Models.Events\Orobas.cs:59-94
///     private IEnumerable&lt;EventOption&gt; OptionPool3 {
///         TouchOfOrobas touchOfOrobas = (TouchOfOrobas)ModelDb.Relic&lt;TouchOfOrobas&gt;().ToMutable();
///         if (touchOfOrobas.SetupForPlayer(base.Owner))
///             list.Add(RelicOption(touchOfOrobas));      // ← ★ 那个"遗物格"
///         ArchaicTooth archaicTooth = ...;               // 古老牙齿在同一池（必须保留不动）
///     }
///     </code></para>
///
///     <para><b>为什么这样换可行</b>（<c>EventOption</c> 实据）：
///     <list type="bullet">
///       <item><c>public RelicModel? Relic { get; private set; }</c> —— 有遗物才渲染成"遗物格"；
///         用**不带遗物**的构造函数建的选项是普通文字选项（标题 + 描述）；</item>
///       <item><c>EventOption(EventModel, Func&lt;Task&gt;? onChosen, LocString title, LocString description, string textKey, …)</c>
///         —— 标题/描述可自带、回调随便给 ⇒ 在 <c>onChosen</c> 里把栖途加进卡组即可。</item>
///     </list></para>
///
///     <para>⚠️ <b>取舍要说清楚</b>：事件选项**没有"卡牌格"这种渲染**（<c>EventOption</c> 只有
///     <c>Relic</c> 一个图形位，没有 <c>Card</c>）⇒ 换出来的是**文字选项**（标题「栖途」+ 说明），
///     不是卡面缩略图。与"卡牌奖励"一致的实质是：**玩家点它才拿到牌**（而非上一版那样默默塞进卡组）。</para>
///
///     <para>⚠️ 只动**欧洛巴斯之触**那一项：<c>ArchaicTooth</c>（古老牙齿，龙剑→魔剑的升级来源）
///     与其它选项一律原样保留 —— 用 <c>Relic is TouchOfOrobas</c> 精确匹配，不做任何"按位置删"。</para>
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Events.Orobas), "get_OptionPool3")]
internal static class OrcaOrobasCardOptionPatch
{
    /// <summary>卡牌选项的标题键（在本模组 <c>cards.json</c> 里，单一来源）。</summary>
    private const string OptionTitleKey = "ORCA_HOMESTEAD.event.title";

    /// <summary>卡牌选项的描述键（同上）。</summary>
    private const string OptionDescriptionKey = "ORCA_HOMESTEAD.event.description";

    /// <summary>本地化文件名（<c>new LocString(文件, 键)</c> 的第一个参数）。</summary>
    private const string LocFile = "cards";

    private static void Postfix(MegaCrit.Sts2.Core.Models.Events.Orobas __instance,
                                ref IEnumerable<MegaCrit.Sts2.Core.Events.EventOption> __result)
    {
        try
        {
            if (__result == null) return;

            var list = new List<MegaCrit.Sts2.Core.Events.EventOption>(__result);
            var removed = list.RemoveAll(o => o?.Relic is TouchOfOrobas);
            if (removed == 0) return;         // 本次没提供那个遗物（例如玩家没有起始遗物）⇒ 一律不动

            var card = ModelDb.Card<OrcaHomestead>();
            list.Add(new MegaCrit.Sts2.Core.Events.EventOption(
                __instance,
                async () =>
                {
                    var owner = __instance.Owner;
                    if (owner == null)
                    {
                        OrcaLog.Warn("[Orca] 欧洛巴斯·栖途选项：拿不到 Owner ⇒ 本次没给牌", 2);
                        return;
                    }
                    await OrcaPack2Acquisition.GrantToDeck(owner, card, "欧洛巴斯之触（卡牌选项）");
                },
                new MegaCrit.Sts2.Core.Localization.LocString(LocFile, OptionTitleKey),
                new MegaCrit.Sts2.Core.Localization.LocString(LocFile, OptionDescriptionKey),
                OptionTitleKey,
                Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>()));

            __result = list;
            OrcaLog.Info($"[Orca] 欧洛巴斯之触：遗物格已换成卡牌选项（现有 {list.Count} 个选项，古老牙齿未动）", 2);
        }
        catch (Exception ex)
        {
            // ★ 出错就不改（__result 保持原样）：宁可让人拿到头环，也不能把事件页弄坏
            OrcaLog.Warn($"[Orca] 欧洛巴斯之触·换卡牌选项出错（保持原样）：{ex.Message}", 2);
        }
    }
}

// ★ 2026-10-04 移除整段：原 OrcaReverseScaleSource（BOSS 战胜利按 0.5 概率给逆鳞、再独立掷点给睥睨）。
//
//   用户口径：「BOSS 专属奖励卡牌还是没掉落，去掉专属奖励吧，回归肉鸽随机属性」
//   ⇒ 该"BOSS 专属奖励"机制整体删除：
//       · OrcaRelic 里的 AfterCombatVictory override 已移除（回归基类行为）
//       · 逆鳞(OrcaReverseScale) 与 睥睨(OrcaOverlook) 已移出随机池排除名单（见 Pools.cs）
//         ⇒ 改为与其它卡一样，通过常规战斗奖励 / 商店随机获得
//   删除原因不只是"没生效"：这两张卡被排除出随机池后，掉落是它们的**唯一**来源，
//   来源失效即等于永远拿不到 —— 与其继续维护一条隐蔽的专属通道，不如回归统一随机。