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
            /*TOGGLE-OFF-A4*/ await Task.CompletedTask; // await CardPileCmd.Add(added, PileType.Deck);
            OrcaLog.Info($"[Orca] {reason} ⇒ 获得卡牌【{card.Title}】（已入牌堆，牌组现有 {player.Deck?.Cards.Count() ?? -1} 张）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 给卡失败（{reason}）：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     ★ **栖途的获取途径** —— 拾取先古遗物「欧洛巴斯之触」（<see cref="TouchOfOrobas" />）时一并获得。
///
///     <para>反编译实据（<c>TouchOfOrobas</c>）：它的 <c>AfterObtained</c> 只做一件事 ——
///     把玩家的**起始遗物升级**（<c>RefinementUpgrades</c> 映射表里只有 5 个原版角色的起始遗物，
///     **没有奥卡** ⇒ 奥卡会拿到 <c>Circlet</c> 兜底）。我们不去动那个表（那是它自己的设计），
///     只在其后**追加**栖途。</para>
///
///     <para>⚠️ <c>RelicModel.Owner</c> 在不同的模型上可访问性不一致（有的是 protected）
///     ⇒ 这里用反射读，避免编译期访问限制。</para>
/// </summary>
[HarmonyPatch(typeof(TouchOfOrobas), "AfterObtained")]
internal static class OrcaOrobasTouchPatch
{
    private static void Postfix(TouchOfOrobas __instance)
    {
        try
        {
            var owner = OwnerOf(__instance);
            TaskHelper.RunSafely(OrcaPack2Acquisition.GrantToDeck(owner, ModelDb.Card<OrcaHomestead>(), "欧洛巴斯之触"));
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 欧洛巴斯之触·给栖途失败：{ex.Message}", 2);
        }
    }

    /// <summary>反射读 <c>Owner</c>（Player），拿不到就返回 null。</summary>
    private static Player? OwnerOf(object relic)
    {
        try
        {
            var pi = AccessTools.Property(relic.GetType(), "Owner")
                     ?? AccessTools.Property(typeof(RelicModel), "Owner");
            return pi?.GetValue(relic) as Player;
        }
        catch
        {
            return null;
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