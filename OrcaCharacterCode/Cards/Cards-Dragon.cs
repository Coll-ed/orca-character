using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace OrcaCharacter;

// ═══════════════════════════════════════════════════════════════════════
//  银龙体系（OrcaOrbForm.Dragon）—— 用户口径 2026-09-16
//  铸血 / 涅槃之握 / 践踏
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
///     ★ 铸血（0 费 · 银龙 · 技能 · **白卡 Common** · 消耗；敲后追加**保留**）。
///
///     <para>用户口径：<i>"减少当前 10% 战斗中的最大生命上限，回复等量生命"</i>，
///     并明确「**不是**临时生命上限……建议用两个池子管理 —— 战斗临时生命上限、总生命上限」，
///     拍板为「与**银龙血统**同一个池子」。</para>
///
///     <para>实现：走 <see cref="OrcaCombatHp" />（负数入账）⇒ 战斗结束时与遗物一起还原。</para>
/// </summary>
public sealed class OrcaBloodForge : OrcaCard
{
    /// <summary>减少当前战斗上限的比例（%）。用户未给升级数值 ⇒ 升级只追加"保留"词条。</summary>
    private const int Percent = 10;

    /// <summary>★ 银龙体系：打出时能量球切回龙头。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    public OrcaBloodForge() : base(0, (CardType)2, (CardRarity)2, (TargetType)1) { }   // 0 费 · Skill · Common · Self

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    /// <summary>
    ///     ★ 用户口径（他备注过的卡面格式约定）：
    ///     <b>「XX%（具体数值）」= 卡面在百分比后面用括号带上此刻的实际数值</b>，方便玩家计算。
    ///     这里注入 <c>{Percent}</c>（10）与 <c>{MaxHpLoss}</c>（按当前**总上限**算出来的实际点数）。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int loss = 0;
        try
        {
            var me = Owner?.Creature;
            if (me != null) loss = (int)Math.Floor(me.MaxHp * Percent / 100.0m);
        }
        catch
        {
            // 取不到就按 0 显示（百科/牌库浏览时没有 Owner）
        }
        // ★★ 必须用 DynamicVar（不是裸 decimal）—— `{X:diff()}` 只在值是 DynamicVar 时才生效
        //   （反编译实据 HighlightDifferencesFormatter：非 DynamicVar 直接 return false）。
        description.Add(new DynamicVar("Percent", Percent));
        description.Add(new DynamicVar("MaxHpLoss", loss));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var me = Owner.Creature;
        decimal want = Math.Floor(me.MaxHp * Percent / 100.0m);
        if (want <= 0)
        {
            Log.Info("[Orca] 铸血：当前上限过低，无可扣除", 2);
            return;
        }

        // ★★ 用户口径 2026-09-16（"注血就是铸血"）：
        //    "注血看**总生命上限**池子去扣上限；总生命上限 = 原有生命上限 + 临时生命上限；
        //     临时生命上限没了，扣除的就是原有生命上限了"
        //    ⇒ 走 OrcaCombatHp.Deduct：**先吃临时池，临时池空了才动真上限**
        //      （临时那段战斗结束不再还原 —— 已经抵账了；原有那段是永久损失）
        decimal actual = await OrcaCombatHp.Deduct(me, want);
        if (actual <= 0) return;

        await CreatureCmd.Heal(me, actual, true);
        Log.Info($"[Orca] 铸血：总上限 -{actual}（临时池余 {OrcaCombatHp.Delta:+#;-#;0}）"
                 + $"→ 回复 {actual} 点生命（{me.CurrentHp}/{me.MaxHp}）", 2);
    }

    /// <summary>敲后追加**保留**（用户口径："敲后带保留词条"）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, CardKeyword.Retain);
}

/// <summary>
///     ★ 涅槃之握（1 费 · 银龙 · 技能 · **金卡 Rare**）。
///
///     <para>用户口径：<i>"消耗一张牌，回复 10% 最大生命上限的生命"</i>，敲后 15%。</para>
///     <para>⚠️ 「消耗一张牌」按最自然理解实现为**由玩家选择一张手牌**消耗（与龙族魔典同一套选牌 UI）。</para>
/// </summary>
public sealed class OrcaNirvanaGrasp : OrcaCard
{
    /// <summary>回复比例（%）：10 → 敲后 15。</summary>
    private int _percent = 10;

    /// <summary>
    ///     ★ 卡面格式（用户口径 2026-09-16）：**「XX%（具体数值）」** ——
    ///     括号里带**此刻的实际回复量**（单位"血"），方便玩家直接算。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int heal = 0;
        try
        {
            var me = Owner?.Creature;
            if (me != null) heal = (int)Math.Ceiling(me.MaxHp * _percent / 100.0m);
        }
        catch
        {
            // 百科 / 牌库浏览时还没有 Owner ⇒ 显示 0
        }
        // ★★ 数值全部走 DynamicVar —— 卡面文案里**不硬编码百分比**，
        //   否则升级（10% → 15%）后卡面还写着 10%，玩家看到的就是错的。
        description.Add(new DynamicVar("Percent", _percent));
        description.Add(new DynamicVar("Heal", heal));
    }

    /// <summary>
    ///     ★ 卡面格式（用户口径 2026-09-16）：**「XX%（具体数值）」** ——
    ///     括号里带**此刻的实际回复量**（单位"血"），方便玩家直接算。
    /// </summary>

    /// <summary>
    ///     ★ 卡面格式（用户口径 2026-09-16）：**「XX%（具体数值）」** ——
    ///     括号里带**此刻的实际回复量**（单位"血"），方便玩家直接算。
    /// </summary>

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    public OrcaNirvanaGrasp() : base(1, (CardType)2, (CardRarity)4, (TargetType)1) { }  // 1 费 · Skill · Rare · Self

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 选一张手牌消耗（排除自己；**不允许取消** —— 见下方 A6 裁定）
        // ★★ 2026-10-05（A6）裁定：**不可取消**。
        //   权威 work/奥卡卡包集/卡牌包1/卡牌说明1.txt L29 原文：
        //   「消耗一张牌，回复10%最大生命上限的生命」—— 「消耗一张牌」是**必付代价**；
        //   原实现 Cancelable = true ⇒ 玩家可取消，取消的后果是"只回血、不消耗牌"，与权威不符。
        //   用户 2026-10-05 裁定「按 work 权威改」⇒ Cancelable = false。
        //   （对比：焚卷入典 / 龙族魔典那类"选牌"保持可取消，本卡按权威改为强制。）
        var picked = await CardSelectCmd.FromHand(
            ctx,
            Owner,
            new CardSelectorPrefs(NirvanaPrompt, 1) { Cancelable = false, PretendCardsCanBePlayed = true },
            c => c != play.Card,
            this);

        var card = picked?.FirstOrDefault();
        if (card != null)
        {
            await CardCmd.Exhaust(ctx, card, false, false);
            Log.Info($"[Orca] 涅槃之握：消耗 {card.Id.Entry}", 2);
        }
        else
        {
            Log.Info("[Orca] 涅槃之握：没有选择要消耗的牌（照常回血）", 2);
        }

        // ② 回复 10%（敲后 15%）**最大生命上限**
        var me = Owner.Creature;
        decimal heal = Math.Ceiling(me.MaxHp * _percent / 100.0m);
        if (heal <= 0) return;
        await CreatureCmd.Heal(me, heal, true);
        Log.Info($"[Orca] 涅槃之握：回复 {heal} 点生命（{_percent}% × 上限 {me.MaxHp}）→ {me.CurrentHp}/{me.MaxHp}", 2);
    }

    protected override void OnUpgrade() => _percent = 15;

    private static LocString NirvanaPrompt
    {
        get
        {
            try
            {
                var loc = new LocString("cards", "ORCA_NIRVANA_GRASP.pick");
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
///     ★ 践踏（1 费 · **无色（不变形态）** · **攻击牌** · 白卡 Common · 消耗；敲后**去掉消耗**）。
///
///     <para>权威口径（<c>work/奥卡卡包集/卡牌包1/卡牌说明1.txt</c> L38-40，2026-10-07 改版；改版前为 6 点伤害）：
///     <i>"1费，无色，攻击牌，白卡，消耗，敲后去消耗 / 对所有敌人造成 **10** 点伤害，并附加 **2** 层【焚烧】"</i>。
///     ⚠️ 「无色」按 <c>work/奥卡卡包集/备注.txt</c> L1 = <b>不会改变形态的卡牌</b> ⇒ <see cref="OrcaOrbForm.None" />。</para>
///
///     <para>★ 这是本批**两张攻击牌之一** —— 商店固定要 2 张 Attack，
///     之前池子里只有 Basic 的「打击」被排除在外，正是商店报错的根因。</para>
/// </summary>
public sealed class OrcaTrample : OrcaCard
{
    /// <summary>★ 无色 = **不改变**能量球形态（权威 L38「无色」；定义见 备注.txt L1）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    public OrcaTrample() : base(1, (CardType)1, (CardRarity)2, (TargetType)3) { }   // 1 费 · Attack · Common · AllEnemies　（按卡牌说明1.txt：白卡）

    /// <summary>践踏的伤害值。</summary>
    private const decimal TrampleDamage = 10m;  // 权威：10 点（卡牌说明1.txt L40「对所有敌人造成10点伤害」，2026-10-07 由 6 上调）

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DamageVar(TrampleDamage, (ValueProp)8) };

    /// <summary>
    ///     践踏附加的【焚烧】层数。权威（卡牌说明1.txt L39）：「对所有敌人造成 6 点伤害，并附加 **2 层【焚烧】**」，
    ///     且敲后只「去消耗」、不改层数 ⇒ 固定 2 层。
    /// </summary>
    private const int BurnStacks = 2;

    /// <summary>
    ///     ★ 2026-10-04 修复：卡面写 <c>{Burn:diff()}</c> 但本卡**从未提供过 Burn 变量**
    ///     ⇒ SmartFormat 失败 ⇒ 整条卡面回退成原文（用户实测：践踏显示成 {Damage:diff()}）。
    ///     同时把 <c>WeakPower</c> 改为 <see cref="OrcaBurnPower" /> —— 卡面与规格写的都是【焚烧】，
    ///     原实现给的是【虚弱】，文案与行为不一致。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description) =>
        description.Add(new DynamicVar("Burn", BurnStacks));

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;
        if (combat == null) return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play)
            .TargetingAllOpponents(combat)
            .WithHitFx("vfx/vfx_attack_slash", null, null)
            .Execute(ctx);

        int hits = 0;
        foreach (var enemy in combat.Enemies.Where(e => !e.IsDead))
        {
            // ★ 2026-10-04：原为 WeakPower（虚弱）1 层 —— 与卡面/规格写的【焚烧】不符，改为焚烧。
            await PowerCmd.Apply<OrcaBurnPower>(ctx, enemy, BurnStacks, Owner.Creature, this);
            hits++;
        }
        Log.Info($"[Orca] 践踏：全体 {DynamicVars.Damage.BaseValue} 点伤害 + 给 {hits} 个敌人各 {BurnStacks} 层【焚烧】", 2);
    }

    /// <summary>敲后**去掉消耗**（用户口径："敲后去消耗"）。</summary>
    protected override void OnUpgrade() => CardCmd.RemoveKeyword(this, CardKeyword.Exhaust);
}
