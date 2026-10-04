using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★ 一条**自定义关键词**的完整表现：卡面金色标签 + 悬停浮窗（标题 + 正文）。
///
///     <para>
///     用户口径 2026-09-16（狂躁）与 2026-09-23（推广到全部 buff）：
///     <i>"狂躁变成和永恒一样的标签类，说明也要和永恒一样带说明"</i>、
///     <i>"不要写到卡面，而是写到标签去"</i>、<i>"帮所有的 buff 都这么搞"</i>。
///     </para>
///
///     <para>
///     原版这两件事是**两条独立管线**，而且都只认编译期枚举 <c>CardKeyword</c>
///     （标签走 <c>CardModel.GetDescriptionForPile</c> → <c>CardKeywordOrder.afterDescription</c>；
///     浮窗走 <c>CardModel.HoverTips</c> → <c>HoverTipFactory.FromKeyword</c>）——
///     模组**加不了新枚举值**，所以两条路都得自己造：
///     ① 标签：卡面文案里直接写 <c>[gold]吸血[/gold]</c>（与「永恒。」同一套渲染标记）；
///     ② 浮窗：<see cref="OrcaCardHoverTipPatch" /> 给 <c>get_ExtraHoverTips</c> 挂 postfix。
///     </para>
///
///     <para>
///     ⚠️ **不要**借别的 <c>CardKeyword</c> 枚举值当"壳"：每个原版关键词都带真实逻辑，
///     例如 <c>Sly</c> 会被 <c>CardCmd.DiscardAndDraw</c> 认出来并**在每次弃牌时自动打出**
///     ⇒ 借 Sly 会让龙剑每弃一次就打一次，直接毁掉卡牌行为。
///     文案取不到时退回兜底文本，**绝不显示成裸键名**。
///     </para>
/// </summary>
internal sealed class OrcaCardKeyword
{
    /// <summary>本地化表（<c>OrcaCharacter/localization/zhs/card_keywords.json</c>）。</summary>
    private const string Table = "card_keywords";

    private readonly string _prefix;
    private readonly string _fallbackTitle;
    private readonly string _fallbackDesc;
    private string? _title;
    private string? _desc;

    internal OrcaCardKeyword(string prefix, string fallbackTitle, string fallbackDesc)
    {
        _prefix = prefix;
        _fallbackTitle = fallbackTitle;
        _fallbackDesc = fallbackDesc;
    }

    /// <summary>关键词标题（"吸血"）—— 也是"卡面文案里出现它 ⇒ 该挂这条浮窗"的匹配串。</summary>
    internal string Title => _title ??= Resolve(".title", _fallbackTitle);

    /// <summary>关键词说明（浮窗正文）。</summary>
    internal string Description => _desc ??= Resolve(".description", _fallbackDesc);

    /// <summary>卡面那一行金色标签：<c>[gold]吸血[/gold]。</c></summary>
    internal string CardText => $"[gold]{Title}[/gold]。";

    /// <summary>
    ///     悬停浮窗。标题用本地化 <see cref="LocString" />（走游戏自己的管线，构造时
    ///     <c>GetFormattedText()</c> 落成字符串）；正文用已解析好的 <see cref="Description" />（含兜底）
    ///     ⇒ 两条路都不会出现裸键名。
    /// </summary>
    internal IHoverTip HoverTip => new HoverTip(new LocString(Table, _prefix + ".title"), Description);

    /// <summary>
    ///     ★★ 解析顺序（**单一真源，不许两处各写一份**）：
    ///     <list type="number">
    ///       <item><c>card_keywords/&lt;PREFIX&gt;.xxx</c> —— 本模组自建关键词表。
    ///         只有**不是 Power** 的关键词才需要写这里（狂躁 / 翱翔）；</item>
    ///       <item><c>powers/ORCA_&lt;PREFIX&gt;_POWER.xxx</c> —— **buff 自己的说明**。
    ///         绝大多数关键词走这条 ⇒ "卡面浮窗"和"buff 浮窗"共用同一份文案，
    ///         改一处两处都变。**2026-09-23 就是因为两边各写一份，才出现"卡面/说明没跟上"的连环 bug**；</item>
    ///       <item>C# 兜底串 —— 两处都取不到时用，**绝不显示裸键名**。</item>
    ///     </list>
    /// </summary>
    private string Resolve(string suffix, string fallback)
    {
        string primary = _prefix + suffix;
        string buff = "ORCA_" + _prefix + "_POWER" + suffix;

        foreach (var (table, entry) in new[] { (Table, primary), ("powers", buff) })
        {
            try
            {
                var loc = LocString.GetIfExists(table, entry);
                if (loc != null && !loc.IsEmpty)
                {
                    var text = loc.GetFormattedText();
                    if (!string.IsNullOrWhiteSpace(text) && text != entry) return text;
                }
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 关键词文案 {table}/{entry} 读取异常：{ex.Message}", 2);
            }
        }

        OrcaLog.Info($"[Orca] 关键词文案 {primary} / {buff} 两处都取不到，用兜底文本", 2);
        return fallback;
    }
}

/// <summary>
///     ★ 本模组的**全部自定义关键词**登记表。
///
///     <para>
///     ★★ 2026-09-23 用户口径：<i>"帮所有的 buff 都这么搞"</i> ——
///     卡面只留 <c>[gold]名称[/gold]</c> 这一行标签，**长说明一律进悬停浮窗**。
///     所以这里不再是"给龙剑单独塞一条"，而是一张表 + <see cref="TipsFor" /> 自动挂载。
///     </para>
/// </summary>
internal static class OrcaKeyword
{
    /// <summary>「狂躁」（嗜血龙剑 / 嗜血魔剑）。</summary>
    internal static readonly OrcaCardKeyword Rampage = new(
        "RAMPAGE", "狂躁",
        "玩家回合结束后，若此牌在手牌中则自动打出；若本回合没有打出这张牌，则回到抽牌堆的第一位。");

    /// <summary>「吸血」（龙族魔典叠层 → 攻击牌回血）。</summary>
    internal static readonly OrcaCardKeyword Lifesteal = new(
        "LIFESTEAL", "吸血",
        "打出攻击牌造成未被格挡的伤害时回复生命：龙剑类型（嗜血龙剑 / 嗜血魔剑）→ 伤害的 50%（向上取整）× 当前层数，一口气结清所有层数；其他攻击牌 → 伤害的 25%（向下取整，最低 1 点），不消耗层数。");

    /// <summary>「焚烧」（敌方回合结束炸开，波及其它带焚烧的敌人）。</summary>
    internal static readonly OrcaCardKeyword Burn = new(
        "BURN", "焚烧",
        "敌方回合结束时，消耗所有层数，对每个带焚烧的敌人造成（层数）点伤害。");

    /// <summary>「回响」（焚卷入典 → 下次魔典多打一次）。</summary>
    internal static readonly OrcaCardKeyword CodexEcho = new(
        "CODEX_ECHO", "回响",
        "接下来每次打出龙族魔典，额外多打出 1 次（每次消耗 1 层）。");

    /// <summary>「杀意」/「智慧」/「血统」（睥睨：三种牌型各能额外打一次）。</summary>
    internal static readonly OrcaCardKeyword KillingIntent = new(
        "KILLING_INTENT", "杀意", "你的下一张**攻击牌**会额外打出一次。");

    internal static readonly OrcaCardKeyword Wisdom = new(
        "WISDOM", "智慧", "你的下一张**技能牌**会额外打出一次。");

    internal static readonly OrcaCardKeyword Lineage = new(
        "LINEAGE", "血统", "你的下一张**能力牌**会额外打出一次。");

    /// <summary>「红莲淬」（赤焰淬：魔剑下一击增幅）。</summary>
    internal static readonly OrcaCardKeyword CrimsonTemper = new(
        "CRIMSON_TEMPER", "红莲淬", "嗜血魔剑下次造成的伤害提升（每档 0.1 倍）。");

    /// <summary>「焚文」（治疗加成，层数即当前总加成）。</summary>
    internal static readonly OrcaCardKeyword HealBonus = new(
        "HEAL_BONUS", "焚文",
        "获得 20% 额外治疗加成；每消耗一张牌，本场战斗再 +10%（层数即当前总加成）。");

    // ── 以下为 2026-09-23「彻底检测」补齐的 buff（此前卡面提到却没有浮窗、buff 自己也没有文案）──

    /// <summary>「翱翔」（自写的 <c>OrcaSoarPower</c>，烬血之翼专用；可叠层、挨伤害减一层）。</summary>
    internal static readonly OrcaCardKeyword Soar = new(
        "SOAR", "翱翔", "受到的伤害减半；每受到一次未格挡的伤害减少一层。");

    /// <summary>「生死一线」（熔渊枯骨）。</summary>
    internal static readonly OrcaCardKeyword MoltenBone = new(
        "MOLTEN_BONE", "生死一线",
        "你造成的焚烧每次触发只消耗一半层数，且改为对场上所有人（包括你自己）造成伤害。");

    /// <summary>「额外回合」（逆鳞）。</summary>
    internal static readonly OrcaCardKeyword ExtraTurn = new(
        "EXTRA_TURN", "额外回合", "本回合结束后，你将额外获得一个回合（一次性）。");

    /// <summary>「烬血之翼」（翱翔的一次性记号）。</summary>
    internal static readonly OrcaCardKeyword EmberWing = new(
        "EMBER_WING", "烬血之翼", "翱翔将在你的下个回合开始时消失（记号）。");

    /// <summary>「栖途」。</summary>
    internal static readonly OrcaCardKeyword Homestead = new(
        "HOMESTEAD", "栖途", "战斗结束后，将你的一部分战斗临时生命上限转化为真实生命上限（比例随卡牌升级提高）。");

    /// <summary>「归墟」。</summary>
    internal static readonly OrcaCardKeyword VoidReturn = new(
        "VOID_RETURN", "归墟", "场上所有角色无法回复生命；被阻止的回复按 50% 转入嗜血龙剑的伤害附加。");

    /// <summary>「浴血涅槃」。</summary>
    internal static readonly OrcaCardKeyword BloodNirvana = new(
        "BLOOD_NIRVANA", "浴血涅槃", "当你受到致命伤害时，消耗所有战斗临时生命上限，并回复其中 50% 的生命（触发一次后消失）。");

    /// <summary>全部关键词（顺序 = 浮窗排列顺序）。</summary>
    internal static readonly OrcaCardKeyword[] All =
    {
        Rampage, Lifesteal, Burn, CodexEcho,
        KillingIntent, Wisdom, Lineage, CrimsonTemper, HealBonus,
        Soar, MoltenBone, ExtraTurn, EmberWing, Homestead,
        VoidReturn, BloodNirvana,
    };

    /// <summary>
    ///     ★★ **自动挂载**：扫这张牌的**本地化原文**，凡是文案里出现了哪个关键词的标题，
    ///     就把那条说明作为悬停浮窗挂上去。
    ///
    ///     <para>
    ///     这样"卡面只写标签、说明进浮窗"就是**规则驱动**的：以后加一张提到【焚烧】的牌、
    ///     或给某个 buff 改个名字，只要文案里写着标题，浮窗自动就有 —— 不需要逐卡登记。
    ///     </para>
    ///
    ///     <para>
    ///     ⚠️ 为什么读**本地化原文**（<c>LocString.GetRawText()</c>）而不是卡面渲染结果：
    ///     渲染结果里的 <c>[gold]</c> 已被转成 BBCode、动态变量已被替换，
    ///     而 <c>OrcaBloodSword</c> 的「狂躁」标签还来自运行时注入的 <c>{KeywordTags}</c>
    ///     ⇒ 全靠渲染结果会漏。原文是稳定的、也就是我们写文案时看到的那一份。
    ///     </para>
    /// </summary>
    internal static List<IHoverTip> TipsFor(CardModel card)
    {
        var tips = new List<IHoverTip>();

        try
        {
            var entry = card.Id.Entry;
            var loc = LocString.GetIfExists("cards", entry + ".description");
            var raw = loc?.GetRawText();
            if (string.IsNullOrEmpty(raw)) return tips;

            foreach (var kw in All)
            {
                if (raw.Contains(kw.Title)) tips.Add(kw.HoverTip);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Info($"[Orca] 关键词浮窗自动挂载跳过：{ex.Message}", 2);
        }

        return tips;
    }
}

/// <summary>
///     ★ 给**奥卡自己的卡**挂上关键词悬停浮窗（用户口径：<i>"不要写到卡面，而是写到标签去"</i>、
///     <i>"帮所有的 buff 都这么搞"</i>）。
///
///     <para>
///     两条来源：
///     ① **自动**：<see cref="OrcaKeyword.TipsFor" /> 扫卡面文案，出现哪个 buff 名就挂哪条说明；
///     ② **特例**：嗜血龙剑额外挂「当前战况」浮窗（用户口径 2026-09-16：
///        「龙剑放大细看卡牌的时候，才会显示隐藏的百分比数字」）。
///     </para>
///
///     <para>
///     为什么数字走浮窗：原版**卡面本身没有"放大版"** —— <c>NCardHolder.DoCardHoverEffects</c>
///     只是把卡 <c>Scale</c> 到 <c>HoverScale</c> 并调 <c>CreateHoverTips()</c>
///     ⇒ "放大细看"时唯一额外出现的东西就是浮窗。所以：卡面写规则，浮窗写此刻的实际数值。
///     </para>
/// </summary>
[HarmonyPatch(typeof(CardModel), "get_ExtraHoverTips")]
internal static class OrcaCardHoverTipPatch
{
    private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        try
        {
            // ★ 只碰奥卡自己的牌 —— 绝不往原版 / 别的模组的卡上加浮窗
            if (__instance is not OrcaCard card) return;

            var custom = OrcaKeyword.TipsFor(card);

            // 龙剑：战况数字（关键词那条已由自动挂载覆盖）
            if (card is OrcaBloodSword sword) custom.Add(OrcaSwordState.TipFor(sword));

            if (custom.Count == 0) return;
            __result = custom.Concat(__result ?? Array.Empty<IHoverTip>());
        }
        catch (Exception)
        {
            // ★ 出错静默：补丁只是表现层，不能影响游戏
        }
    }
}

/// <summary>
///     龙剑的"当前战况"浮窗：把卡面上**不显示**的实际数字列出来
///     （卡面写规则/百分比，浮窗写此刻的具体数值）。
/// </summary>
internal static class OrcaSwordState
{
    private const string Table = "cards";
    private const string Entry = "ORCA_BLOOD_SWORD.state";

    private const string FallbackTitle = "当前战况";

    /// <summary>浮窗标题。</summary>
    internal static string Title
    {
        get
        {
            var loc = LocString.GetIfExists(Table, Entry + ".title");
            var text = loc?.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? FallbackTitle : text;
        }
    }

    /// <summary>
    ///     浮窗正文：**现场向龙剑要最新数字**（主动打出的生命代价 / 已经攒了多少伤害附加 /
    ///     狂躁会打出多少）。数值全部来自 <see cref="OrcaBloodSword" /> 自己的状态，
    ///     所以浮窗永远和实际结算一致。
    /// </summary>
    internal static IHoverTip TipFor(OrcaBloodSword sword)
    {
        var loc = LocString.GetIfExists(Table, Entry + ".description");
        if (loc != null)
        {
            int lifeLoss = sword.CurrentLifeCost;

            // ★ 修复 A-龙剑浮窗（2026-10-03）：原来这里用 loc.Add("名字", 裸值) ✗ ——
            //   只给值、不给 DynamicVar ⇒ SmartFormat 的 :diff() 找不到格式器 ⇒
            //   报 "No suitable Formatter could be found" ⇒ 回退原文，并在**悬停信号链**上抛 ⇒ 卡死
            //   （实机日志两轮实证：OrcaCardHoverTipPatch → get_ExtraHoverTips → OnHoverHandler）
            //   ⇒ 改为与卡面完全同一套写法 DynamicVar ✓（满足本方法注释里"同一套、一个都不能少"的要求）

            // ★★ 变量必须与卡面（OrcaBloodSword.AddExtraArgsToDescription）**同一套、一个都不能少**：
            //    SmartFormat 只要有一个变量取不到就抛 FormattingException，
            //    而 LocManager.SmartFormat 会 catch 住并**返回原文**
            //    ⇒ 整段所有占位符一起裸露（连已经 Add 过的 {Bonus} 也显示成字面的 "{Bonus}"）。
            //    2026-09-16 实机日志实锤：
            //      Localization formatting error! … No source extension could handle the selector named "HpLoss"
            //      table=cards key=ORCA_BLOOD_SWORD.state.description
            //      variables={LifeLoss:7,LifePercent:20,Bonus:0,MaxHpPerKill:2}    ← 当时漏了 HpLoss / Gain
            loc.Add(new DynamicVar("LifeLoss", (decimal)lifeLoss));
            loc.Add(new DynamicVar("HpLoss", (decimal)lifeLoss));
            loc.Add(new DynamicVar("Gain", (decimal)((lifeLoss + 1) / 2)));          // ceil(lifeLoss × 50%)，与卡面同源
            loc.Add(new DynamicVar("LifePercent", (decimal)sword.LifePercent));
            loc.Add(new DynamicVar("Bonus", (decimal)sword.Bonus));
            loc.Add(new DynamicVar("Dealt", (decimal)sword.Bonus));
            loc.Add(new DynamicVar("MaxHpPerKill", (decimal)sword.MaxHpPerKillValue));

            var text = loc.GetFormattedText();

            // 收口：万一将来文案又加了我们没喂的变量（SmartFormat 会整段回退成原文），
            // 也绝不把裸的 "{XXX}" 端给玩家 —— 直接换下面的兜底文案。
            if (!string.IsNullOrWhiteSpace(text) && text != Entry + ".description" && !text.Contains('{'))
            {
                return new HoverTip(new LocString(Table, Entry + ".title"), text);
            }

            OrcaLog.Warn($"[Orca] 龙剑战况浮窗格式化不完整（有未替换的变量），改用兜底文案：{text}", 2);
        }

        // 兜底：本地化取不到时自己拼一句（绝不显示裸键名）
        return new HoverTip(
            new LocString(Table, Entry + ".title"),
            $"主动打出失去 {sword.CurrentLifeCost} 点生命（最大生命的 {sword.LifePercent}%）；"
            + $"已累计伤害附加 {sword.Bonus}；被狂躁打出时对所有敌人造成 {sword.Bonus} 点伤害。");
    }
}

/// <summary>
///     ★ 把原版自动追加的「永恒。」标签从龙剑的卡面文字里**摘掉**
///     （用户口径：「**将永恒与狂躁两个标签放到最上面**」）。
///
///     <para>
///     原版关键词标签的位置是写死的：<c>CardKeywordOrder.afterDescription = { Exhaust, Eternal }</c>，
///     由 <c>CardModel.GetDescriptionForPile</c> 在说明**之后**逐个 <c>list2.Add(keyword.GetCardText())</c>。
///     所以做法是：我们自己把两行标签拼在说明**开头**（<c>{KeywordTags}</c> = 狂躁。/ 永恒。），
///     再把原版追加的那一行去掉。
///     </para>
///
///     <para>
///     ★ 打两个重载：<c>GetDescriptionForPile(PileType, Creature)</c>（public，卡面实际走这条）
///     与它内部调用的三参数版本（private）。三参数版本的第 2 个参数是 <c>CardModel</c> 的**私有嵌套枚举**
///     <c>DescriptionPreviewType</c>，编译期拿不到类型 ⇒ 只能用 <c>AccessTools.Method</c> 按参数个数捞出来。
///     （<c>NCard.cs:888</c> 实锤调用：<c>Model.GetDescriptionForPile(pileType, target)</c>）
///     </para>
/// </summary>
internal static class OrcaKeywordTagsOnTopPatch
{
    /// <summary>
    ///     原版追加的那一行，必须与 <c>CardKeywordExtensions.GetCardText()</c> **逐字一致**：
    ///     <code>"[gold]" + keyword.GetTitle().GetFormattedText() + "[/gold]" + _period.GetRawText()</code>
    ///
    ///     <para>
    ///     ★★★ 两个键名都是**全大写**：原版本地化表 <c>card_keywords.json</c> 里的键就是
    ///     <c>"ETERNAL.title"</c> 与 <c>"PERIOD"</c>（原版用
    ///     <c>StringHelper.Slugify(keyword.ToString())</c> 拼出来 ⇒ 全大写）。
    ///     </para>
    ///
    ///     <para>
    ///     ★★ 踩过的坑（2026-09-16 实机截图实锤：卡面末尾多出一行「永恒。」）：
    ///     这里曾写成**小写** <c>"eternal.title"</c>，而 <see cref="LocString" /> 的构造函数
    ///     **不校验键是否存在**，取不到时 <c>GetFormattedText()</c> 会把**键名原样返回**
    ///     ⇒ <c>EternalTag</c> 变成 <c>"[gold]eternal.title[/gold]。"</c> ⇒
    ///     <see cref="Strip" /> 里的 <c>EndsWith</c> 永远为 false ⇒ 剥离**静默失效**，
    ///     卡面顶部自己拼的「永恒。」与末尾原版追加的「永恒。」同时出现。
    ///     </para>
    /// </summary>
    private static string EternalTag
    {
        get
        {
            if (_eternalTag != null) return _eternalTag;

            string title = "永恒";
            try
            {
                var loc = LocString.GetIfExists("card_keywords", "ETERNAL.title");
                var text = loc?.GetFormattedText();

                // 取不到时 GetFormattedText() 会回退成键名本身 ⇒ 那种情况宁可走字面量兜底
                if (!string.IsNullOrWhiteSpace(text) && text != "ETERNAL.title") title = text;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 永恒标签标题取不到，用兜底文本：{ex.Message}", 2);
            }

            string period = "。";
            try
            {
                // 原版就是用 GetRawText()（不是 GetFormattedText()）取这个句号
                var raw = new LocString("card_keywords", "PERIOD").GetRawText();
                if (!string.IsNullOrEmpty(raw)) period = raw;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 关键词句号（PERIOD）取不到，用兜底文本：{ex.Message}", 2);
            }

            _eternalTag = $"[gold]{title}[/gold]{period}";
            return _eternalTag;
        }
    }

    private static string? _eternalTag;

    /// <summary>把结尾那一行「永恒。」连同它前面那个换行一起剪掉。</summary>
    internal static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.EndsWith(EternalTag, StringComparison.Ordinal)) return text;

        int cut = text.Length - EternalTag.Length;
        int nl = text.LastIndexOf('\n', Math.Max(0, cut - 1));
        return (nl >= 0 ? text.Substring(0, nl) : text.Substring(0, cut)).TrimEnd('\n', '\r');
    }

    /// <summary>公有两参数重载（卡面实际走这条）。</summary>
    [HarmonyPatch(typeof(CardModel), "GetDescriptionForPile", new[] { typeof(PileType), typeof(Creature) })]
    internal static class PublicOverload
    {
        private static void Postfix(CardModel __instance, ref string __result)
        {
            try
            {
                if (__instance is not OrcaBloodSword) return;
                __result = Strip(__result);
            }
            catch (Exception)
            {
                // ★ 出错静默：补丁只是表现层，不能影响游戏
            }
        }
    }

    /// <summary>私有三参数重载（参数 2 是私有嵌套枚举，只能按参数个数 + 第一个参数类型定位）。</summary>
    [HarmonyPatch]
    internal static class PrivateOverload
    {
        private static MethodBase? TargetMethod()
        {
            try
            {
                var candidates = AccessTools.GetDeclaredMethods(typeof(CardModel))
                    .Where(m => m.Name == "GetDescriptionForPile" && m.GetParameters().Length == 3)
                    .ToList();

                if (candidates.Count == 0)
                {
                    OrcaLog.Warn("[Orca] 找不到三参数的 GetDescriptionForPile（标签置顶可能只对卡面生效）", 2);
                    return null;
                }

                return candidates[0];
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 定位 GetDescriptionForPile 三参数版本失败：{ex.Message}", 2);
                return null;
            }
        }

        private static void Postfix(CardModel __instance, ref string __result)
        {
            try
            {
                if (__instance is not OrcaBloodSword) return;
                __result = Strip(__result);
            }
            catch (Exception)
            {
                // ★ 出错静默：补丁只是表现层，不能影响游戏
            }
        }
    }
}
