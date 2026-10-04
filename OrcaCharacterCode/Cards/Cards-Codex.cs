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

    /// <summary>
    ///     权威口径（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c> L2）：「1能量，魔典，技能，**消耗**，金卡，**敲后去消耗**」。
    ///
    ///     <para>实现方式照 <see cref="OrcaCrimsonTemper" />（本工程既有做法）：用**动态关键词**
    ///     而不是在 <c>OnUpgrade</c> 里加词条 —— 敲后返回空集即等于"去掉消耗"，
    ///     不需要引擎提供"移除关键词"的 API。</para>
    /// </summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords
        => IsUpgraded ? Array.Empty<CardKeyword>() : new[] { CardKeyword.Exhaust };

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

    // 敲后的变化（去掉【消耗】）由上面的 CanonicalKeywords 动态表达（照 OrcaCrimsonTemper）
    // ⇒ 不需要额外 override。原实现是 EnergyCost.UpgradeBy(-1)（1 费 → 0 费），
    //    与权威「敲后去消耗」不符，已按审计建议删除该降费。
}

/// <summary>
///     ★ 焚文（1 费 · 魔典 · **能力牌 Power** · 蓝卡 Uncommon · **敲后固有**）。
///
///     <para>权威口径（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c> L5-7）：
///     <i>"1能量，魔典，能力牌，蓝卡，**敲后固有** /
///     获得 **20%** 额外治疗加成，消耗卡牌获得 **10%** 的治疗加成于本场战斗"</i>。</para>
///
///     <para>★ 敲后 **固有** 走 <c>CardCmd.ApplyKeyword(Innate)</c>；原先的"敲后降 1 费"与权威不符，已删除。</para>
///
///     <para>★ 这是本批两张**能力牌**之一 —— 商店固定要 1 张 Power，正好补位。</para>
/// </summary>
public sealed class OrcaCodexEmber : OrcaCard
{
    /// <summary>常驻治疗加成（%）。</summary>
    private const int PersistentHealPercent = 20;   // 权威：20%（卡牌说明1.txt L7「获得20%额外治疗加成」）

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    public OrcaCodexEmber() : base(1, (CardType)3, (CardRarity)3, (TargetType)1) { }  // 1 费 · Power · Uncommon · Self　（按卡牌说明1.txt：1能量）

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await PowerCmd.Apply<OrcaHealBonusPower>(ctx, Owner.Creature, PersistentHealPercent, Owner.Creature, this);
        Log.Info($"[Orca] 焚文：获得 {PersistentHealPercent}% 常驻治疗加成"
                 + $"（每次消耗卡牌再 +{OrcaHealBonusPower.PerExhaustPercent}%，累计到本场战斗结束）", 2);
    }

    /// <summary>
    ///     卡面数值注入 —— 权威 L7 的两个百分比都不再写死在 <c>cards.json</c> 里：
    ///     <list type="bullet">
    ///       <item><c>Percent</c> = 常驻加成 ← <see cref="PersistentHealPercent" />（单一来源：卡牌自己）；</item>
    ///       <item><c>PerExhaust</c> = 每烧一张的追加 ← <see cref="OrcaHealBonusPower.PerExhaustPercent" />
    ///         （单一来源：Power 自己，卡牌不复制这个值）。</item>
    ///     </list>
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("Percent", (decimal)PersistentHealPercent));
        description.Add(new DynamicVar("PerExhaust", (decimal)OrcaHealBonusPower.PerExhaustPercent));
    }

    /// <summary>敲后带**固有**（权威 L6「敲后固有」；不再改费用）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, new[] { CardKeyword.Innate });
}

/// <summary>
///     ★ 逐焰（1 费 · **无色（不变形态）** · **攻击牌** · 白卡 Common）。
///
///     <para>权威口径（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c> L41-43）：
///     <i>"1费，无色，攻击牌，白卡，敲后8点伤害5层焚烧效果 /
///     对一名敌人造成 **5** 点伤害并挂上 **2** 层【焚烧】效果"</i>。
///     ⚠️ 「无色」按 <c>work/奥卡卡包集/备注.txt</c> L1 = <b>不会改变形态的卡牌</b> ⇒ <see cref="OrcaOrbForm.None" />。</para>
///
///     <para>「焚烧」= <see cref="OrcaBurnPower" />（敌方回合结束时炸开，波及其它带焚烧的敌人）。</para>
/// </summary>
public sealed class OrcaEmberChase : OrcaCard
{
    /// <summary>焚烧层数：2 → 敲后 5。</summary>
    private int _burn = 2;

    /// <summary>★ 无色 = **不改变**能量球形态（权威 L42「无色」；定义见 备注.txt L1）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    public OrcaEmberChase() : base(1, (CardType)1, (CardRarity)2, (TargetType)2) { }   // 1 费 · Attack · Common · AnyEnemy

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DamageVar(5m, (ValueProp)8) };

    /// <summary>
    ///     ★ 用户反馈：「逐焰敲后卡面升级错了，卡面是 2 焚烧 + 10 伤害，实际打出效果与我们写得一致」
    ///     ⇒ <c>{Damage:diff()}</c> 会自己跟着升级变，但**焚烧层数是普通字段**，
    ///     写死在文案里的"2 层"升级后不会变 ⇒ 注入成动态变量 <c>{Burn}</c>。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        // ★ 2026-10-04 修复：裸值 ⇒ `No suitable Formatter` ⇒ 整条卡面回退成原文。改为 DynamicVar。
        description.Add(new DynamicVar("Burn", (decimal)_burn));
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