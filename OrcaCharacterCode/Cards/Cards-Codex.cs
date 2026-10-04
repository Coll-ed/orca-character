using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

// ═══════════════════════════════════════════════════════════════════════
//  魔典体系（OrcaOrbForm.Codex）—— 用户口径 2026-09-16
//  焚卷入典 / 焚文 / 逐焰
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
///     ★ 焚卷入典（1 费 → 敲后 **0 费** · 魔典 · 技能 · **金卡 Rare** · 消耗）。
///
///     <para>用户口径：<i>"消耗 1 张手牌，使下次的【龙族魔典】打出次数额外加 1"</i>；
///     追问后确认「额外加 1」＝ 魔典选中 1 张后**连续打出 2 次**。</para>
///
///     <para>实现：消耗掉选中的手牌后，给玩家叠 1 层 <see cref="OrcaCodexEchoPower" />（回响）；
///     <see cref="OrcaDragonCodex" /> 打出时会消费该层数并按次数循环打出。</para>
/// </summary>
public sealed class OrcaCodexIgnition : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    public OrcaCodexIgnition() : base(1, (CardType)2, (CardRarity)4, (TargetType)1) { }  // 1 费 · Skill · Rare · Self

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 选一张手牌消耗（排除自己）
        var picked = await CardSelectCmd.FromHand(
            ctx,
            Owner,
            new CardSelectorPrefs(IgnitionPrompt, 1) { Cancelable = true, PretendCardsCanBePlayed = true },
            c => c != play.Card,
            this);

        var card = picked?.FirstOrDefault();
        if (card == null)
        {
            Log.Info("[Orca] 焚卷入典：没有选择要消耗的牌 ⇒ 不给回响", 2);
            return;
        }

        await CardCmd.Exhaust(ctx, card, false, false);
        Log.Info($"[Orca] 焚卷入典：消耗 {card.Id.Entry}", 2);

        // ② 给 1 层「回响」⇒ 下次龙族魔典额外打出 1 次
        await PowerCmd.Apply<OrcaCodexEchoPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);
        Log.Info("[Orca] 焚卷入典：获得 1 层【回响】⇒ 下次龙族魔典额外打出 1 次", 2);
    }

    /// <summary>敲后 1 费 → **0 费**。</summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    private static LocString IgnitionPrompt
    {
        get
        {
            try
            {
                var loc = new LocString("cards", "ORCA_CODEX_IGNITION.pick");
                loc.Add("Amount", 1);
                return loc;
            }
            catch
            {
                return CardSelectorPrefs.ExhaustSelectionPrompt;
            }
        }
    }
}

/// <summary>
///     ★ 焚文（2 费 → 敲后 **1 费** · 魔典 · **能力牌 Power** · 蓝卡 Uncommon）。
///
///     <para>用户口径：<i>"获得 50% 额外治疗加成，若消耗卡牌还会获得额外 5% 的治疗加成于本回合"</i>；
///     追问后确认：**50% 常驻整场；每次消耗卡牌 +5%，只到本回合结束**。</para>
///
///     <para>★ 这是本批两张**能力牌**之一 —— 商店固定要 1 张 Power，正好补位。</para>
/// </summary>
public sealed class OrcaCodexEmber : OrcaCard
{
    /// <summary>常驻治疗加成（%）。</summary>
    private const int BasePercent = 50;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    public OrcaCodexEmber() : base(1, (CardType)3, (CardRarity)3, (TargetType)1) { }  // 1 费 · Power · Uncommon · Self　（按卡牌说明1.txt：1能量）

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await PowerCmd.Apply<OrcaHealBonusPower>(ctx, Owner.Creature, BasePercent, Owner.Creature, this);
        Log.Info($"[Orca] 焚文：获得 {BasePercent}% 常驻治疗加成"
                 + $"（每次消耗卡牌再 +{OrcaHealBonusPower.PerExhaustPercent}%，只到本回合结束）", 2);
    }

    /// <summary>敲后 2 费 → **1 费**。</summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

/// <summary>
///     ★ 逐焰（1 费 · 魔典 · **攻击牌** · 白卡 Common）。
///
///     <para>用户口径：<i>"对一名敌人造成 5 点伤害并挂上 2 层焚烧效果"</i>，敲后 <i>"8 点伤害 5 层焚烧"</i>。</para>
///     <para>「焚烧」= <see cref="OrcaBurnPower" />（敌方回合结束时炸开，波及其它带焚烧的敌人）。</para>
/// </summary>
public sealed class OrcaEmberChase : OrcaCard
{
    /// <summary>焚烧层数：2 → 敲后 5。</summary>
    private int _burn = 2;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    public OrcaEmberChase() : base(1, (CardType)1, (CardRarity)2, (TargetType)2) { }   // 1 费 · Attack · Common · AnyEnemy

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DamageVar(5m, (ValueProp)8) };

    /// <summary>
    ///     ★ 用户反馈：「逐焰敲后卡面升级错了，卡面是 2 焚烧 + 10 伤害，实际打出效果与我们写得一致」
    ///     ⇒ <c>{Damage:diff()}</c> 会自己跟着升级变，但**焚烧层数是普通字段**，
    ///     写死在文案里的"2 层"升级后不会变 ⇒ 注入成动态变量 <c>{Burn}</c>。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add("Burn", _burn);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var target = play.Target;
        if (target == null)
        {
            Log.Warn("[Orca] 逐焰：没有目标（不打伤害、不挂焚烧）", 2);
            return;
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_slash", null, null)
            .Execute(ctx);

        await PowerCmd.Apply<OrcaBurnPower>(ctx, target, _burn, Owner.Creature, this);
        Log.Info($"[Orca] 逐焰：对 {target.Name} 造成 {DynamicVars.Damage.BaseValue} 点伤害 + {_burn} 层焚烧", 2);
    }

    /// <summary>敲后：伤害 +3、焚烧 2 → 5 层。</summary>
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        _burn = 5;
    }
}