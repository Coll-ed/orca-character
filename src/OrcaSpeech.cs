using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace OrcaCharacter;

/// <summary>
///     银龙奥卡的**战斗文字气泡**（角色说话）。
///
///     <para><b>用户口径（2026-09-17）</b>：
///     <i>"说话气泡颜色：板甲灰黑／婚礼红白 + 奥卡战斗冒文字气泡"</i>。</para>
///
///     <para><b>API 依据（全部来自 `_api\sts2.api.txt`，不是猜的）</b>：
///     <list type="bullet">
///       <item><c>TalkCmd.Play(LocString line, Creature speaker, VfxColor vfxColor, VfxDuration duration)</c>
///         —— **官方"说话"命令**（只有这一个重载，所以必须给它 <c>LocString</c>）；</item>
///       <item><c>LocString(String locTable, String locEntryKey)</c> —— 只能"表名 + 键"，**没有收原始文本的构造**；</item>
///       <item>⇒ 自定义台词走 <c>LocManager.GetTable(name).MergeWith(dict)</c>
///         （<c>LocTable</c> 的 public 方法）**在运行时把玩家写的句子注入本地化表**，
///         之后照常按 <c>ORCA.custom.&lt;分类&gt;.&lt;n&gt;</c> 取用。</item>
///     </list></para>
///
///     <para><b>配色跟皮肤走</b>（与 <see cref="OrcaSkin.EnergyOrbMaterialPath" /> 同一套口径）：
///     板甲 ⇒ <c>VfxColor.DarkGray</c>（灰黑）；婚礼 ⇒ <c>VfxColor.Red</c>（红白）。</para>
///
///     <para><b>为什么必须节流</b>：狂躁是"每回合结束都可能触发"的机制，
///     若每次都冒气泡，一场战斗能刷十几条 ⇒ 每场最多 <see cref="MaxPerCombat" /> 句，
///     且**同一句一场只说一次**（<see cref="_used" />）。
///     额度由设置里的「气泡弹出频率」（<see cref="OrcaConfig.Frequency" />）缩放。</para>
/// </summary>
internal static class OrcaSpeech
{
    /// <summary>台词所在的本地化表（与原版 <c>banter.*</c> / <c>bestiaryQuote</c> 同一张）。</summary>
    private const string Table = "characters";

    /// <summary>卡牌台词所在的本地化表（`{卡id}.line`）。</summary>
    private const string CardTable = "cards";

    // ── 额度（★ 2026-09-23 起由设置里的「气泡弹出频率」缩放）──────
    //   基础口径是用户 2026-09-17 定的：每场 4 句 + 卡牌台词 3 句。
    //   Scale() 在"关闭"档返回 0 ⇒ `_spoken >= 0` 恒真 ⇒ 自动闭嘴，不用额外分支。

    private const int BasePerCombat = 4;
    private const int BaseCardLines = 3;

    /// <summary>每场战斗最多说几句（防刷屏）。</summary>
    private static int MaxPerCombat => OrcaConfig.Scale(BasePerCombat);

    /// <summary>卡牌台词每场最多说几句（与 banter/狂躁 的额度**分开算**，免得互相挤掉）。</summary>
    private static int MaxCardLines => OrcaConfig.Scale(BaseCardLines);

    private static int _spoken;
    private static int _cardLines;

    /// <summary>本场已经说过的句子（同一句不重复）。</summary>
    private static readonly HashSet<string> _used = new(StringComparer.Ordinal);

    private static readonly Random Rng = new();

    /// <summary>气泡颜色：板甲＝灰黑 / 婚礼＝红（与 <see cref="OrcaSkin" /> 同主题）。</summary>
    internal static VfxColor BubbleColor =>
        OrcaSkin.IsWedding ? VfxColor.Red : VfxColor.DarkGray;

    /// <summary>新战斗开始：清空计数（由 <see cref="OrcaBloodline" /> 在第一回合调用）。</summary>
    internal static void ResetCombat()
    {
        _spoken = 0;
        _cardLines = 0;
        _used.Clear();
        _catCount.Clear();
        InjectCustomLines();          // ★ 自定义台词在这里注入（改设置 ⇒ 下一场战斗生效）
    }

    // ── ★ 自定义预设（用户 2026-09-23）──────────────────────────
    //   LocString 没有"收原始文本"的构造 ⇒ 把玩家写的句子 **注入本地化表**，
    //   存成 ORCA.custom.<分类>.<n>，之后走与内置台词**完全相同**的取值路径。

    private static void InjectCustomLines()
    {
        try
        {
            if (OrcaConfig.Preset != OrcaConfig.BanterPreset.Custom) return;

            var dict = new Dictionary<string, string>();
            foreach (var cat in OrcaConfig.Categories)
            {
                var lines = OrcaConfig.Split(OrcaConfig.RawFor(cat));
                if (lines == null) continue;
                for (var i = 0; i < lines.Length; i++) dict[$"ORCA.custom.{cat}.{i + 1}"] = lines[i];
            }

            if (dict.Count == 0) return;                 // 玩家一句没写 ⇒ 全走内置库
            LocManager.Instance?.GetTable(Table)?.MergeWith(dict);
            OrcaLog.Info($"[Orca] 自定义台词已注入 {dict.Count} 句（分类 {dict.Count} 项）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 自定义台词注入失败（本次回落到内置台词）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     把内置候选键 <c>ORCA.banter.&lt;分类&gt;.&lt;n&gt;</c> 换成自定义键
    ///     <c>ORCA.custom.&lt;分类&gt;.&lt;n&gt;</c>（该分类写了自定义台词才换）。
    /// </summary>
    private static string[] ApplyPreset(string[] keys)
    {
        if (OrcaConfig.Preset != OrcaConfig.BanterPreset.Custom || keys.Length == 0) return keys;

        try
        {
            // 分类从第一个键里取：ORCA.banter.<分类>.<序号>
            var parts = keys[0].Split('.');
            if (parts.Length < 4 || parts[0] != "ORCA" || parts[1] != "banter") return keys;
            var cat = parts[2];

            var custom = OrcaConfig.Split(OrcaConfig.RawFor(cat));
            if (custom == null) return keys;             // 该分类留空 ⇒ 回落内置库

            return Enumerable.Range(1, custom.Length).Select(i => $"ORCA.custom.{cat}.{i}").ToArray();
        }
        catch
        {
            return keys;
        }
    }

    // ── ★ 分类额度（2026-09-17 新增）─────────────────────────────
    //   "受伤 / 击杀"这类触发**非常频繁**，若和开场白共用同一个池子，
    //   它们会把额度吃光 ⇒ 战斗开场白、狂躁台词全都出不来。
    //   所以给每个类别一个**小上限**，总上限仍是 MaxPerCombat。

    private static readonly Dictionary<string, int> _catCount = new(StringComparer.Ordinal);

    /// <summary>
    ///     带**分类上限**的说话：同一类别本场最多 <paramref name="maxInCategory" /> 句。
    ///     总上限仍受 <see cref="MaxPerCombat" /> 约束 ⇒ 频率与以前一致。
    ///     <para>分类上限也跟频率档位缩放（"少"档不该还被高频类别刷满）。</para>
    /// </summary>
    internal static void SayCapped(
        Creature? speaker, VfxDuration duration, string category, int maxInCategory, params string[] keys)
    {
        var cap = OrcaConfig.Scale(maxInCategory);
        if (cap <= 0) return;
        if (_catCount.TryGetValue(category, out var used) && used >= cap) return;

        var before = _spoken;
        SayOne(speaker, duration, keys);
        if (_spoken > before)                                  // 真说出口了才计数
        {
            _catCount[category] = (_catCount.TryGetValue(category, out var c) ? c : 0) + 1;
        }
    }

    /// <summary>
    ///     从 <paramref name="keys" /> 里随机挑一句（跳过本场已说过的）说出来。
    ///     任何异常都吞掉 —— 气泡只是表现，绝不能影响战斗结算。
    /// </summary>
    internal static void SayOne(Creature? speaker, VfxDuration duration, params string[] keys)
    {
        try
        {
            if (speaker == null || keys.Length == 0) return;
            if (_spoken >= MaxPerCombat) return;          // 频率档（含"关闭"=0）在这里生效

            keys = ApplyPreset(keys);                     // ★ 自定义预设

            // 候选中挑还没说过的
            var pool = new List<string>();
            foreach (var k in keys)
            {
                if (!_used.Contains(k)) pool.Add(k);
            }
            if (pool.Count == 0) return;                 // 这一组全说过了

            var key = pool[Rng.Next(pool.Count)];
            var loc = LocString.GetIfExists(Table, key);
            if (loc == null)
            {
                OrcaLog.Warn($"[Orca] 说话气泡缺键（{Table}:{key}）⇒ 跳过", 2);
                _used.Add(key);                          // 别每次都报同一个缺失
                return;
            }

            TalkCmd.Play(loc, speaker, BubbleColor, duration);
            _used.Add(key);
            _spoken++;
            OrcaLog.Info($"[Orca] 说话气泡：{key}（颜色 {BubbleColor}，本场第 {_spoken}/{MaxPerCombat} 句）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 说话气泡出错：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     ★ 按**卡牌类型**说一句（用户 2026-09-23："攻击牌 / 防御牌 / 技能牌 / 能力牌都有对应的台词"）。
    ///     <para>分类键：<c>ORCA.banter.attack|defend|skill|power.1..6</c>。
    ///     <c>CardType</c>：Attack=1 / Skill=2 / Power=3（已核实）。
    ///     防御牌**单独看"这张牌给不给格挡"**，所以判定顺序是：Power → 有格挡的 Skill → Attack → 其它 Skill。</para>
    /// </summary>
    internal static void SayCardType(Creature? speaker, int cardType, bool grantsBlock)
    {
        string cat;
        if (cardType == 3) cat = "power";
        else if (grantsBlock) cat = "defend";
        else if (cardType == 1) cat = "attack";
        else cat = "skill";

        SayCapped(speaker, VfxDuration.VeryShort, cat, 2, Keys(cat));
    }

    /// <summary>事件类台词：<c>heavyHurt</c> / <c>blocked</c> / <c>heal</c>。</summary>
    internal static void SayEvent(Creature? speaker, string category, int maxInCategory = 2)
        => SayCapped(speaker, VfxDuration.Short, category, maxInCategory, Keys(category));

    /// <summary>取某个分类的 1..6 号候选键。</summary>
    private static string[] Keys(string category)
        => Enumerable.Range(1, 6).Select(i => $"ORCA.banter.{category}.{i}").ToArray();

    // ── ★ 卡牌台词（用户 `备注.txt` 的卡牌格式里那条"打出卡牌时需要说的话"）──────

    /// <summary>
    ///     打出某张牌时说一句它的台词（键 = <c>cards</c> 表的 <c>{cardEntry}.line</c>）。
    ///
    ///     <para>由 <see cref="OrcaCard.AfterCardPlayed" /> **统一调用** ——
    ///     新卡只要在 `cards.json` 里写一行 `.line` 就自动有台词，**不用改 C#**。</para>
    ///
    ///     <para>没写 `.line` 的牌安静跳过（不报错、不刷日志）。</para>
    /// </summary>
    internal static void SayCardLine(Creature? speaker, string? cardEntry)
    {
        try
        {
            if (speaker == null || string.IsNullOrEmpty(cardEntry)) return;
            if (_cardLines >= MaxCardLines) return;

            var key = cardEntry + ".line";
            if (_used.Contains(key)) return;                  // 同一张牌一场只说一次

            var loc = LocString.GetIfExists(CardTable, key);
            if (loc == null) return;                          // 这张牌没写台词 ⇒ 安静跳过

            TalkCmd.Play(loc, speaker, BubbleColor, VfxDuration.Short);
            _used.Add(key);
            _cardLines++;
            OrcaLog.Info($"[Orca] 卡牌台词：{key}（本场第 {_cardLines}/{MaxCardLines} 句）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 卡牌台词出错（{cardEntry}）：{ex.Message}", 2);
        }
    }
}
