using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;   // ★ 卡面动态数值要用 LocString（AddExtraArgsToDescription）
using MegaCrit.Sts2.Core.Localization.DynamicVars;   // ★ 必须用 DynamicVar 注入，裸值会 No suitable Formatter
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

// ═══════════════════════════════════════════════════════════════════════
//  魔剑体系（OrcaOrbForm.Sword）—— 用户口径 2026-09-16
//  血焰剑鞘 / 红莲淬 / 狂热斩击 / 嗜血魔剑
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
///     ★ 血焰剑鞘（1 费 · 魔剑 · 技能 · 蓝卡 Uncommon；敲后扣除比例 10% → **20%**）。
///
///     <para>用户口径：<i>"消耗 10% 当前生命值，为【嗜血龙剑】附加（消耗生命）点伤害"</i>。</para>
///
///     <para>实现：失去生命走 <c>CreatureCmd.SetCurrentHp</c>（**绕开伤害管线**，与龙剑主动打出同一套写法
///     —— 自残不该触发受击类遗物，但**会**触发银龙血统的"当前生命变化"钩子，符合既有口径）；
///     附加直接进龙剑的 <c>_bonus</c>（<see cref="OrcaBloodSword.AddBonus" />）⇒ 与龙剑自己攒的是**同一个池子**。</para>
/// </summary>
public sealed class OrcaBloodScabbard : OrcaCard
{
    /// <summary>消耗当前生命的百分比（10 → 敲后 20）。</summary>
    private int _percent = 10;

    /// <summary>
    ///     ★ 卡面格式（用户口径 2026-09-16）：**「XX%（具体数值）」** ——
    ///     百分比后面用括号带上**此刻的实际数值**（单位"血"），方便玩家直接算，例如 <c>20%（30血）</c>。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int loss = 0;
        try
        {
            var me = Owner?.Creature;
            if (me != null) loss = (int)Math.Floor(me.CurrentHp * _percent / 100.0m);
        }
        catch
        {
            // 百科 / 牌库浏览时还没有 Owner ⇒ 显示 0
        }
        // ★ 2026-10-04 修复：裸值 ⇒ `No suitable Formatter` ⇒ 整条卡面回退成原文。改为 DynamicVar。
        description.Add(new DynamicVar("HpLoss", (decimal)loss));
        // ★★ 2026-10-04 二次修复（用户实机截图：卡面显示成 `消耗{Percent:diff()}%({HpLoss:diff()}血)`）：
        //    文案用了**两个**带格式化器的占位符，而这里只注入了 `HpLoss` ⇒ 缺 `Percent`
        //    ⇒ SmartFormat 对 `{Percent:diff()}` 报 No suitable Formatter ⇒ **整条**卡面回退成原文。
        //    ⚠️ 两条教训：
        //      ① 一个键缺失就整条崩，不存在"只坏那一处"的中间状态；
        //      ② 核对必须按【本卡类自己声明了什么】，不能全工程搜字符串 ——
        //         那样会被别的卡的声明骗过（`Percent` 在栖途里声明过，于是血焰剑鞘被误判为 OK）。
        description.Add(new DynamicVar("Percent", (decimal)_percent));
    }

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    public OrcaBloodScabbard() : base(1, (CardType)2, (CardRarity)3, (TargetType)1) { }  // 1 费 · Skill · Uncommon · Self

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var me = Owner.Creature;
        int loss = (int)Math.Floor(me.CurrentHp * _percent / 100.0);
        if (loss <= 0)
        {
            Log.Info("[Orca] 血焰剑鞘：当前生命过低，无可消耗", 2);
            return;
        }

        await CreatureCmd.SetCurrentHp(me, me.CurrentHp - loss);
        Log.Info($"[Orca] 血焰剑鞘：失去 {loss} 点生命（{_percent}%）", 2);

        // 把失去的生命**全额**附加给战斗中的每一张【嗜血龙剑】
        var swords = Owner.PlayerCombatState?.AllCards?.OfType<OrcaBloodSword>().ToList() ?? new List<OrcaBloodSword>();
        if (swords.Count == 0)
        {
            Log.Warn("[Orca] 血焰剑鞘：战斗中没有【嗜血龙剑】，本次附加落空", 2);
            return;
        }
        foreach (var sword in swords) sword.AddBonus(loss);
        Log.Info($"[Orca] 血焰剑鞘：为 {swords.Count} 张【嗜血龙剑】各附加 +{loss} 点伤害"
                 + $"（现有附加 {swords[0].Bonus}）", 2);
    }

    protected override void OnUpgrade() => _percent = 20;
}

/// <summary>
///     ★ 红莲淬（1 费 · 魔剑 · **能力牌 Power** · 金卡 Rare；敲后追加**固有**）。
///
///     <para>⚠️⚠️ <b>2026-10-04：本卡与权威口径不符，当前是【空卡】，待实现。</b></para>
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c>）：
///     <i>"红莲淬 / 1费，无色，技能牌，金卡，消耗，敲后去消耗 /
///     将当前场上所有存在的【焚烧】立刻无消耗触发一次"</i></para>
///
///     <para><b>旧实现</b>（本文件下面那个 <c>OrcaCrimsonTemperPower</c>，已按用户裁定"彻底断开"拆除）：
///     挂一个 Power，每次**消耗卡牌**累加一档，倍率 <c>1 + 0.1 × 档数</c>，由【嗜血魔剑】结算时读取并清零。
///     用户原话：<i>"红莲强化魔剑都是很久之前的初版"</i> ⇒ 该联动已从魔剑侧删除，
///     本 Power 的倍率机制随之**没有任何消费者**，故一并拆除。</para>
///
///     <para>⇒ <b>待实现</b>：遍历战斗中所有生物的 <see cref="OrcaBurnPower" />，
///     各**无消耗地触发一次**其回合结束伤害（即不扣层数地结算一次焚烧）。
///     注意权威还写了「无色」「消耗」「敲后去消耗」三个词条 —— 现在这三点也没实现。</para>
/// </summary>
public sealed class OrcaCrimsonTemper : OrcaCard
{
    /// <summary>
    ///     权威口径：「**无色**」。⚠️ 本模组里的"无色"**不是**原版的无色杂卡 ——
    ///     见 <c>work/奥卡卡包集/备注.txt</c>：<i>"里面的无色不是指原版的无色杂卡，而是指不会改变形态的卡牌"</i>
    ///     ⇒ 形态标签为 <see cref="OrcaOrbForm.None" />（打出时能量球不切形态）。
    /// </summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>
    ///     权威口径：「**消耗**，敲后去消耗」。
    ///
    ///     <para>实现方式：用**动态关键词**而不是在 <c>OnUpgrade</c> 里加词条 ——
    ///     敲后返回空集即等于"去掉消耗"，不需要引擎提供"移除关键词"的 API。</para>
    /// </summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords
        => IsUpgraded ? Array.Empty<CardKeyword>() : new[] { CardKeyword.Exhaust };

    public OrcaCrimsonTemper() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ★ 权威效果：「将当前场上所有存在的【焚烧】立刻无消耗触发一次」
        //   实现走 OrcaBurnPower.TriggerAllNow —— 与回合结束的结算**共用同一段伤害逻辑**
        //   （单一来源），只差"是否扣层"一个参数。
        int n = await OrcaBurnPower.TriggerAllNow(ctx);
        if (n == 0)
            OrcaLog.Info("[Orca] 红莲淬：场上没有任何【焚烧】⇒ 本次无效果", 2);
    }

    // 敲后的变化由上面的 CanonicalKeywords 动态表达（去掉【消耗】）⇒ 不需要额外 override。
}

// ── 已删除：OrcaCrimsonTemperPower（红莲淬的"消耗卡牌累加倍率"记账 Power）────────────
//
//   2026-10-04 按用户裁定「彻底断开」拆除。它原本做的是：
//     · PerStack = 0.1 —— 每消耗一张牌累加一档
//     · Multiplier = 1 + 0.1 × 档数 —— 供【嗜血魔剑】狂躁结算时乘算
//     · Consume() —— 被魔剑读走之后清零
//   拆除理由（两条，任一条都足够）：
//     ① 用户原话：「红莲强化魔剑都是很久之前的初版」⇒ 魔剑侧的乘算与 Consume 调用已删除，
//        该倍率**再无任何消费者**，留着就是死代码；
//     ② 它与【权威口径】根本不是同一个效果 —— 权威写的是
//        「将当前场上所有存在的【焚烧】立刻无消耗触发一次」，与"消耗卡牌累计倍率"毫无关系。
//   ⇒ 红莲淬整张卡待按权威重写（见 OrcaCrimsonTemper 的类注释里记的"待实现"）。
//   删除方式：整类移除 + 打出时显式打 WARN 记录"尚未实现"，不留静默空卡。

/// <summary>
///     ★ 狂热斩击（3 费 · 魔剑 · 技能 · 蓝卡 Uncommon；**未升级时带虚无**，敲后去掉）。
///
///     <para>用户口径：<i>"将当前手牌中的攻击牌消耗，打出卡组中狂躁的【嗜血魔剑】"</i>，
///     并指明参考**储君的【征召上前】**：<i>"召唤到手里来，以狂躁的方式直接打出"</i>。</para>
///
///     <para>
///     实现照抄原版 <c>SummonForth</c>（反编译实据）：
///     <code>
///     Owner.PlayerCombatState.AllCards.OfType&lt;SovereignBlade&gt;()
///          .Where(c =&gt; c.Pile == null || c.Pile.Type != PileType.Hand)
///     → await CardPileCmd.Add(cards, PileType.Hand);
///     </code>
///     —— <c>PlayerCombatState.AllCards</c> 就是"不论何处"（抽牌堆 / 弃牌堆 / 消耗堆都会覆盖到）。
///     </para>
///
///     <para>
///     ★ "以**狂躁**的方式打出"＝ 直接走 <c>CardCmd.AutoPlay</c>：它会把 <c>IsAutoPlay</c> 置 true，
///     而龙剑/魔剑的 <c>OnPlay</c> 正是按 <c>IsAutoPlay &amp;&amp; !viaCodex</c> 分流到狂躁那条（全体伤害）
///     ⇒ 不需要任何特判，天然就是"狂躁方式"。
///     </para>
/// </summary>
public sealed class OrcaFrenzySlash : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    public OrcaFrenzySlash() : base(2, (CardType)1, (CardRarity)3, (TargetType)1) { }  // 2 费 · Attack · Uncommon · Self　（按卡牌说明1.txt：2费/攻击牌）

    /// <summary>★ 未升级时带**虚无**（用户口径）；敲后由 <see cref="OnUpgrade" /> 去掉。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Ethereal };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var state = Owner.PlayerCombatState;
        if (state == null) return;

        // ① 消耗**当前手牌中的所有攻击牌**
        var hand = PileType.Hand.GetPile(Owner);
        var attacks = hand.Cards.Where(c => c.Type == CardType.Attack).ToList();
        foreach (var atk in attacks)
        {
            await CardCmd.Exhaust(ctx, atk, false, false);
        }
        Log.Info($"[Orca] 狂热斩击：消耗了手牌中 {attacks.Count} 张攻击牌", 2);

        // ② 不论何处，把【嗜血龙剑】召唤到手里（照 SummonForth 的写法）
        var swords = state.AllCards.OfType<OrcaBloodSword>()
            .Where(c => c.Pile == null || c.Pile.Type != PileType.Hand)
            .ToList();
        if (swords.Count == 0)
        {
            Log.Warn("[Orca] 狂热斩击：牌堆里没有可召唤的【嗜血龙剑】", 2);
            return;
        }
        await CardPileCmd.Add(swords, PileType.Hand);
        Log.Info($"[Orca] 狂热斩击：把 {swords.Count} 张【嗜血龙剑】召唤到手牌", 2);

        // ③ 以**狂躁方式**直接打出（AutoPlay ⇒ IsAutoPlay = true ⇒ 龙剑走 FrenzyStrike）
        foreach (var sword in swords)
        {
            if (sword.Pile?.Type != PileType.Hand) continue;      // 保险：不在手牌就别打
            await CardCmd.AutoPlay(ctx, sword, null);
            Log.Info("[Orca] 狂热斩击：以狂躁方式打出【嗜血龙剑】", 2);
        }
    }

    /// <summary>敲后**去除虚无**。</summary>
    protected override void OnUpgrade() => CardCmd.RemoveKeyword(this, CardKeyword.Ethereal);
}