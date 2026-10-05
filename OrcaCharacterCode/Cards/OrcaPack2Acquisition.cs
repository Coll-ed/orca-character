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

    /// <summary>
    ///     是否启用"把遗物格换成卡牌选项"。
    ///
    ///     <para>⚠️ <b>2026-10-05 置为 false（停用）</b>：启用时会造成**事件软锁** ——
    ///     用户实测「如图，继续没了，卡在这里了」，事件页一个选项都没有。
    ///     根因见 <see cref="Postfix" /> 的注释（引擎的 <c>RelicOption</c> 会把**事件页推进回调**
    ///     一起传进去，而我们只给了裸回调 ⇒ 事件停在原地）。</para>
    ///
    ///     <para>⇒ 查清并照抄"事件页推进"那个回调之后，才可以把这里改回 <c>true</c>。
    ///     在此之前**保持 false**：事件页照原样显示遗物（还自带图标），效果由
    ///     <see cref="OrcaOrobasDirectObtainPatch" /> 保证为"给【栖途】、不给头环"。</para>
    ///
    ///     <para>用 <c>static readonly</c>（而非 <c>const</c>）：常量 <c>false</c> 会让下面的实现变成
    ///     "编译期不可达代码"并报 CS0162 警告；运行时判定既保留开关、又不产生噪音警告。</para>
    /// </summary>
    private static readonly bool EnableCardOptionSwap = false;

    private static void Postfix(MegaCrit.Sts2.Core.Models.Events.Orobas __instance,
                                ref IEnumerable<MegaCrit.Sts2.Core.Events.EventOption> __result)
    {
        if (!EnableCardOptionSwap) return;      // ★ 停用中（原因见常量的说明）

        try
        {
            if (__result == null) return;

            // ★★ 角色门禁（用户口径 2026-10-05：「要加入一个，只有角色是奥卡时才启用」）——
            //    别的角色拿到欧洛巴斯之触时必须**保持原样**（他们的起始遗物确实有先古版本可换，
            //    原始机制对他们是正确的）。判法照抄引擎自己的写法（NRestSiteCharacter 里就是
            //    `Player.Character is Necrobinder` 这样判具体角色的）。
            if (__instance.Owner?.Character is not Orca)
            {
                OrcaLog.Info("[Orca] 欧洛巴斯之触：当前角色不是奥卡 ⇒ 不介入（保持原版选项）", 2);
                return;
            }

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
                    await OfferHomesteadReward(owner, "欧洛巴斯之触（卡牌选项）");
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

    /// <summary>
    ///     把【栖途】作为**奖励**发出去（**不是**直接塞进卡组）。
    ///
    ///     <para><b>为什么必须走奖励流程</b>（用户 2026-10-05：<i>"没有像其他牌一样，出现卡牌加入到卡组的动画"</i>）：
    ///     直接 <c>CardPileCmd.Add(card, PileType.Deck)</c> 只是把牌放进卡组 —— **没有奖励界面，
    ///     也就没有那套"卡牌飞进卡组"的动画**。走奖励流程才有。</para>
    ///
    ///     <para><b>引擎自己的写法（两处实据，照抄）</b>：
    ///     <code>
    ///     // Commands/RewardsCmd.cs:49 与 Models/Relics/NeowsBones.cs:44
    ///     await new RewardsSet(player).WithCustomRewards(rewards).Offer();
    ///     </code>
    ///     奖励对象用 <c>SpecialCardReward</c>（引擎原文：*"A reward that adds a specific card to
    ///     the player's deck"*，构造签名 <c>SpecialCardReward(CardModel, Player)</c>）。</para>
    ///
    ///     <para>⚠️ 兜底：万一奖励流程抛错（例如当前不在能开奖励界面的时机），**退回直接进卡组**
    ///     —— 宁可少个动画，也不能让玩家拿不到这张牌。</para>
    /// </summary>
    internal static async Task OfferHomesteadReward(Player owner, string reason)
    {
        // ★★★ 2026-10-05 **撤回"走奖励流程"这条路**（用户实测：<i>"点击加入了后，界面卡死，卡牌没有正常按照流程加入"</i>）。
        //
        //   走过的弯路（留档，别再走）：
        //     · 先试 `new RewardsSet(owner).WithCustomRewards(...).Offer()` —— 奖励界面**确实弹出来了**
        //       （用户截图见「搜刮！将栖途加入你的牌组。」），但**点完之后整个界面卡死**、
        //       卡牌也没按流程进牌组。
        //     · 原因：`RewardsSet.Offer()` 会 **await 玩家把奖励取走**，
        //       而它是在**事件选项的 onChosen 回调里**被调用的 ⇒ 事件流程在等这个 Task 返回、
        //       这个 Task 又在等奖励界面的完成信号 ⇒ **互相等待 = 死锁**。
        //   结论：**在事件回调里不能直接弹奖励界面**。想要"加入卡组的动画"，
        //   必须找引擎认可的时机（例如事件关闭之后再弹），那是另一件要单独查证的事 ——
        //   在此之前**宁可没有动画，也绝不能卡死**。
        // ★★★ 2026-10-05 撤回上一版"改传 .ToMutable()" —— **那是改错了层**。
        //     实机日志（改传实例之后）：
        //         [WARN] 给卡失败：Mutable model of type OrcaCharacter.OrcaHomestead used in incorrect place.
        //     引擎里两个异常是**配对**的（Models/Exceptions 下）：
        //         CanonicalModelException : "Canonical model … used in incorrect place."   ← 该处要**实例**
        //         MutableModelException   : "Mutable model … used in incorrect place."     ← 该处要**模板**
        //     ⇒ 不同 API 对"模板/实例"的要求**相反**，不能一刀切。
        //     本函数内的正确分工（GrantToDeck 早就写对了）：
        //         RunState.CreateCard(card,…)     需要**模板** ⇒ 内部自己 ToMutable()（见下方 A4 注释）
        //         CardPileCmd.Add(added,…)        用 CreateCard **产出的实例**
        //     ⇒ 所以调用方必须传**模板**；把 ToMutable 加在这一层，等于让它去 ToMutable 一个已经是实例的对象 ✗
        await OrcaPack2Acquisition.GrantToDeck(owner, ModelDb.Card<OrcaHomestead>(), reason);
    }
}

/// <summary>
///     ★★★ **欧洛巴斯之触的第二道防线：不管从哪条路拿到它，都不给头环、都给【栖途】**
///     （用户实测 2026-10-05：<i>"我用指令获得欧洛巴斯之触后还是直接把初始遗物变成头环了"</i>）。
///
///     <para><b>为什么还需要这一层</b>：上面那个 <see cref="OrcaOrobasCardOptionPatch" /> 挂在
///     **先古事件的选项池**上 —— 只有"从事件页选它"那条路才会被拦。
///     而**指令/控制台、其它模组、任何直接 `RelicCmd.Obtain(TouchOfOrobas)`** 的路径
///     **完全不经过事件页** ⇒ 选项池补丁不会被触发，遗物的 <c>AfterObtained</c> 照跑
///     ⇒ 起始遗物照旧被换成头环（用户实测的正是这条）。</para>
///
///     <para><b>两层不冲突、不会重复给牌</b>：事件路径下那一格已被换成卡牌选项
///     ⇒ 玩家**根本不会获得**这个遗物 ⇒ 本补丁的 <c>AfterObtained</c> 不会被调用；
///     只有"绕过事件直接拿到遗物"时才轮到本层生效。</para>
///
///     <para><b>为什么用 Prefix 返回 false</b>（同 <c>TouchOfOrobas.AfterObtained</c> 的性质）：
///     它是 <c>async Task</c>、而 **Harmony 不 await** ⇒ Postfix 的时机不可靠；
///     已逐行核对该方法**只做一次 <c>RelicCmd.Replace</c>**（把起始遗物换成
///     <c>GetUpgradedStarterRelic</c>，奥卡不在 <c>RefinementUpgrades</c> 表里 ⇒ 兜底头环）
///     ⇒ 整段跳过既拦掉替换，又不丢任何别的必要逻辑。</para>
///
///     <para>⚠️ 边界显式：拿不到 Owner 就**放行原逻辑**（返回 true）；出错也放行
///     —— 宁可拿到头环，也不能因为我们的改动让玩家什么都没有。</para>
/// </summary>
[HarmonyPatch(typeof(TouchOfOrobas), "AfterObtained")]
internal static class OrcaOrobasDirectObtainPatch
{
    private static bool Prefix(TouchOfOrobas __instance)
    {
        try
        {
            var owner = OwnerOf(__instance);
            if (owner == null)
            {
                OrcaLog.Warn("[Orca] 欧洛巴斯之触（直接获得）：拿不到 Owner ⇒ 放行原逻辑（本次仍会换成头环）", 2);
                return true;
            }

            // ★★ 角色门禁（同事件页那条）：**只有奥卡**才拦 —— 别的角色的起始遗物本来就有先古版本，
            //    原始强化机制对他们是正确的，绝不能被我们改掉。
            if (owner.Character is not Orca)
            {
                OrcaLog.Info("[Orca] 欧洛巴斯之触（直接获得）：当前角色不是奥卡 ⇒ 放行原版强化", 2);
                return true;
            }

            OrcaLog.Info("[Orca] 欧洛巴斯之触（直接获得，没走事件页）：拦下起始遗物强化，改为给【栖途】", 2);

            // ★ 同样走**奖励流程**（而非直接塞卡组）⇒ 与事件页那条路一致，都有"加入卡组"的动画
            TaskHelper.RunSafely(OrcaOrobasCardOptionPatch.OfferHomesteadReward(
                owner, "欧洛巴斯之触（直接获得）"));

            return false;                    // ★ 跳过原方法：不做那次 RelicCmd.Replace
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 欧洛巴斯之触（直接获得）·拦截出错 ⇒ 放行原逻辑：{ex.Message}", 2);
            return true;
        }
    }

    /// <summary>反射读 <c>Owner</c>（Player）—— <c>RelicModel.Owner</c> 的可访问性在各模型上不一致。</summary>
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