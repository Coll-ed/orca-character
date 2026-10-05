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
///     ★★ **栖途的获取途径** —— 拾取先古遗物「欧洛巴斯之触」（<see cref="TouchOfOrobas" />）时获得。
///
///     <para><b>用户口径 2026-10-05</b>：<i>"欧洛巴斯之触的话，我觉得我们**拦截它的遗物强化效果**，
///     转而改成**给一张牌**"</i>。</para>
///
///     <para><b>为什么拦</b>（反编译 <c>TouchOfOrobas</c> 实据）：它的 <c>AfterObtained</c>
///     **只做一件事** —— 把起始遗物替换成 <c>RefinementUpgrades</c> 映射表里对应的强化版：
///     <code>
///     public override async Task AfterObtained()
///     {
///         ModelId id2 = UpgradedRelic ?? GetUpgradedStarterRelic(relicById).Id;
///         await RelicCmd.Replace(relicById, ModelDb.GetById&lt;RelicModel&gt;(id2).ToMutable());
///     }
///     // 而 GetUpgradedStarterRelic 的兜底是：
///     return ModelDb.Relic&lt;Circlet&gt;().ToMutable();      // ← 表里没有奥卡 ⇒ 白拿一个「头环」
///     </code>
///     ⇒ 奥卡的起始遗物是「银龙血统」，不在那张表里 ⇒ 会被换成**没用的头环**。
///     我们不去改它那张表（那是它自己的设计），而是**整段拦下**，改成给栖途。</para>
///
///     <para>★★ <b>为什么用 Prefix 返回 false、而不是原来的 Postfix</b>：
///     <list type="number">
///       <item><c>AfterObtained</c> 是 <c>async Task</c>，而 **Harmony 不 await** ——
///         原来的 Postfix 在它**刚起步（第一个 await 之前）**就跑，时机根本靠不住
///         （这也正是"换了头环但没给牌"的成因之一）；</item>
///       <item>Prefix 返回 false 是**整个方法都不执行** ⇒ 既拦掉了替换，又彻底绕开 async 时序问题；</item>
///       <item>该方法**只有替换这一个动作** ⇒ 跳过它不会丢任何别的必要逻辑（已逐行核对）。</item>
///     </list></para>
///
///     <para>⚠️ 边界显式：<b>拿不到 Owner 就放行原逻辑</b>（返回 true），绝不因为我们的改动
///     让玩家拿不到这个遗物本该给的东西 —— 宁可拿到头环，也不能什么也没有。</para>
///
///     <para>⚠️ <c>RelicModel.Owner</c> 在不同模型上可访问性不一致（有的是 protected）
///     ⇒ 这里用反射读，避免编译期访问限制。</para>
/// </summary>
[HarmonyPatch(typeof(TouchOfOrobas), "AfterObtained")]
internal static class OrcaOrobasTouchPatch
{
    /// <summary>Prefix 返回 false ⇒ **跳过原方法**（拦下"起始遗物 → 头环"的替换），改为给栖途。</summary>
    private static bool Prefix(TouchOfOrobas __instance)
    {
        try
        {
            var owner = OwnerOf(__instance);
            if (owner == null)
            {
                OrcaLog.Warn("[Orca] 欧洛巴斯之触：拿不到 Owner ⇒ 放行原逻辑（本次不改给栖途）", 2);
                return true;
            }

            OrcaLog.Info("[Orca] 欧洛巴斯之触：拦下起始遗物强化（奥卡不在 RefinementUpgrades 表里，"
                       + "原逻辑会换成没用的头环）⇒ 改为给【栖途】", 2);

            TaskHelper.RunSafely(OrcaPack2Acquisition.GrantToDeck(
                owner, ModelDb.Card<OrcaHomestead>(), "欧洛巴斯之触（拦截遗物强化）"));

            return false;                    // ★ 跳过原方法：不做那次 RelicCmd.Replace
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 欧洛巴斯之触·拦截出错 ⇒ 放行原逻辑：{ex.Message}", 2);
            return true;
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