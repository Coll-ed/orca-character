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
        description.Add("HpLoss", loss);
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
///     <para>用户口径：<i>"每次消耗卡牌时，提升下次【嗜血魔剑】造成伤害的 0.1 倍"</i>。</para>
///
///     <para>实现：<see cref="OrcaCrimsonTemperPower" /> 挂在玩家身上，每次**消耗卡牌**累加一档；
///     伤害倍率 = <c>1 + 0.1 × 次数</c>，由【嗜血魔剑】在结算时读取并**消费**（清零）。</para>
///
///     <para>★ 本批第二张**能力牌**。</para>
/// </summary>
public sealed class OrcaCrimsonTemper : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    public OrcaCrimsonTemper() : base(1, (CardType)3, (CardRarity)4, (TargetType)1) { }  // 1 费 · Power · Rare · Self

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await PowerCmd.Apply<OrcaCrimsonTemperPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);
        Log.Info("[Orca] 红莲淬：本场战斗内，每次消耗卡牌都会提升下次【嗜血魔剑】的伤害倍率", 2);
    }

    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, CardKeyword.Innate);
}

/// <summary>
///     ★ 红莲淬的记账 Power：累计"消耗卡牌"次数，给出【嗜血魔剑】的伤害倍率。
///
///     <para><c>Amount</c> 固定为 1（表示"存在"）；真正的档数在 <c>_stacks</c> 里，
///     并通过 <see cref="DisplayAmount" /> 显示成玩家看得懂的"档数"。</para>
/// </summary>
public sealed class OrcaCrimsonTemperPower : PowerModel
{
    /// <summary>每档提升的倍率（用户口径：0.1 倍）。</summary>
    public const decimal PerStack = 0.1m;

    private int _stacks;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>UI 上显示"积累了几档"，而不是内部的 1。</summary>
    public override int DisplayAmount => _stacks;

    /// <summary>【嗜血魔剑】下次造成伤害的倍率 = 1 + 0.1 × 档数。</summary>
    public decimal Multiplier => 1m + PerStack * _stacks;

    /// <summary>每次**消耗卡牌** → 累加一档（用户口径）。</summary>
    public override Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        try
        {
            _stacks++;
            Log.Info($"[Orca] 红莲淬：消耗 {card.Id.Entry} → 累计 {_stacks} 档，"
                     + $"下次【嗜血魔剑】伤害 ×{Multiplier:0.0}", 2);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 红莲淬·消耗钩子出错：{ex.Message}", 2);
        }
        return Task.CompletedTask;
    }

    /// <summary>被【嗜血魔剑】结算时**消费**（清零）。</summary>
    internal void Consume()
    {
        if (_stacks == 0) return;
        Log.Info($"[Orca] 红莲淬：倍率 ×{Multiplier:0.0} 已被【嗜血魔剑】消费，档数清零", 2);
        _stacks = 0;
    }
}

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

    public OrcaFrenzySlash() : base(3, (CardType)2, (CardRarity)3, (TargetType)1) { }  // 3 费 · Skill · Uncommon · Self

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