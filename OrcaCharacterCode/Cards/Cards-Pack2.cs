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
//  卡牌扩充第二版 · 第一批（设计原文见 `奥卡卡包集\卡牌包2\新建 文本文档.txt`）
//
//  ★ 用户格式约定（`奥卡卡包集\备注.txt`）：
//      名字 / 描述特性等 / 效果 / （备注代码） / "打出卡牌时需要说的话"
//  ★ **「无色」= 不改变能量球形态**（`OrcaOrbForm.None`），**不是**原版无色牌。
//  ★ 枚举值（`_api\sts2.api.txt` 实据）：
//      CardType   : Attack=1 Skill=2 Power=3
//      CardRarity : Basic=1 Common=2 Uncommon=3 Rare=4 Ancient=5
//      TargetType : Self=1 AnyEnemy=2 AllEnemies=3
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
///     ★ 融血（1 费 → 敲后 **0 费** · **银龙** · 技能 · 蓝卡 Uncommon · 目标自己）。
///
///     <para>用户口径：<i>"消耗一张卡牌，获得 2 点能量"</i>。</para>
///
///     <para>实现照抄同项目的 <see cref="OrcaCodexIgnition" />（选一张手牌 → 消耗）：
///     <c>CardSelectCmd.FromHand</c> + <c>CardCmd.Exhaust</c>，再 <c>PlayerCmd.GainEnergy</c>。</para>
/// </summary>
public sealed class OrcaMeltBlood : OrcaCard
{
    /// <summary>获得能量（用户口径：2 点）。</summary>
    private const int EnergyGain = 2;

    /// <summary>★ 银龙体系（打出时能量球切回龙头）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    /// <summary>1 费 · Skill · Uncommon · Self。</summary>
    public OrcaMeltBlood() : base(1, (CardType)2, (CardRarity)3, (TargetType)1) { }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var picked = await CardSelectCmd.FromHand(
            ctx,
            Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1)
            {
                Cancelable = true,
                PretendCardsCanBePlayed = true
            },
            c => c != play.Card,          // 不能选自己
            this);

        var card = picked?.FirstOrDefault();
        if (card == null)
        {
            Log.Info("[Orca] 融血：没有选择要消耗的牌 ⇒ 不给能量", 2);
            return;
        }

        await CardCmd.Exhaust(ctx, card, false, false);
        await PlayerCmd.GainEnergy(EnergyGain, Owner);
        Log.Info($"[Orca] 融血：消耗 {card.Id.Entry} → 获得 {EnergyGain} 点能量", 2);
    }

    /// <summary>敲后 1 费 → **0 费**。</summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

/// <summary>
///     ★ 毁烧（1 费 · **魔典** · **攻击牌** · 白卡 Common · 目标任一敌人）。
///
///     <para>用户口径：<i>"消耗 1 张牌，对一名敌人造成 9 点伤害并获得 5 格挡"</i>；
///     敲后 <i>"15 点伤 9 防"</i>。</para>
/// </summary>
public sealed class OrcaRuinBurn : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    /// <summary>1 费 · Attack · Common · AnyEnemy。</summary>
    public OrcaRuinBurn() : base(1, (CardType)1, (CardRarity)2, (TargetType)2) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(9m, (ValueProp)8),
        new BlockVar(5m, (ValueProp)8),
    };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 消耗 1 张手牌（不消耗自己；没选也照样打伤害/格挡）
        var picked = await CardSelectCmd.FromHand(
            ctx,
            Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1)
            {
                Cancelable = true,
                PretendCardsCanBePlayed = true
            },
            c => c != play.Card,
            this);

        var burned = picked?.FirstOrDefault();
        if (burned != null) await CardCmd.Exhaust(ctx, burned, false, false);

        // ② 对目标造成伤害
        var target = play.Target;
        if (target != null)
        {
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash", null, null)
                .Execute(ctx);
        }
        else
        {
            Log.Warn("[Orca] 毁烧：没有目标 ⇒ 不打伤害", 2);
        }

        // ③ 自己获得格挡
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play, false);

        Log.Info($"[Orca] 毁烧：消耗 {burned?.Id.Entry ?? "（无）"} → "
                 + $"对 {target?.Name ?? "（无目标）"} 造成 {DynamicVars.Damage.BaseValue} 点伤害，"
                 + $"自身获得 {DynamicVars.Block.BaseValue} 点格挡", 2);
    }

    /// <summary>敲后：伤害 9 → **15**，格挡 5 → **9**。</summary>
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(6m);
        DynamicVars.Block.UpgradeValueBy(4m);
    }
}

/// <summary>
///     ★ 卷焰斩（1 费 · **无色（不变形态）** · **攻击牌** · 白卡 Common · **全体敌人**）。
///
///     <para>用户口径：<i>"对所有敌人造成 4 点伤害并附加 3 层【焚烧】；**若此牌被消耗，则立刻打出一次**"</i>；
///     敲后 <i>"7 攻 5 焚烧"</i>。</para>
///
///     <para>「焚烧」= <see cref="OrcaBurnPower" />（敌方回合结束时炸开，波及其它带焚烧的敌人）。</para>
///
///     <para>★ "被消耗则立刻打出一次"的挂点 = <c>CardCmd.Exhaust</c> 里的
///     <c>Hook.AfterCardExhausted</c>（与战鼓 <c>DrumOfBattle</c> 同一处，反编译实据见
///     `进度-卡牌扩充第一版.md` §32）。</para>
/// </summary>
public sealed class OrcaRollingFlame : OrcaCard
{
    /// <summary>焚烧层数：3 → 敲后 5。</summary>
    private int _burn = 3;

    /// <summary>
    ///     ⚠️ 防重入闸门：本牌"被消耗 ⇒ 再打出一次"若不加锁，
    ///     一旦它自己也带消耗效果就会「打出→消耗→再打出→再消耗」无限递归。
    /// </summary>
    private bool _replaying;

    /// <summary>★ 无色 = **不改变**能量球形态。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>2 费 · Attack · Common · AllEnemies　（按卡牌说明2.txt：2费）。</summary>
    public OrcaRollingFlame() : base(2, (CardType)1, (CardRarity)2, (TargetType)3) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DamageVar(4m, (ValueProp)8) };

    /// <summary>焚烧层数是普通字段 ⇒ 必须注入成动态变量，卡面才会跟着升级变。
    /// ★ 2026-10-04 修复：原用裸值 ⇒ `No suitable Formatter` ⇒ 整条卡面回退成原文。</summary>
    protected override void AddExtraArgsToDescription(LocString description) =>
        description.Add(new DynamicVar("Burn", (decimal)_burn));

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;
        if (combat == null)
        {
            Log.Warn("[Orca] 卷焰斩：不在战斗中，本次不结算", 2);
            return;
        }

        // ① 全体伤害
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play)
            .TargetingAllOpponents(combat)
            .WithHitFx("vfx/vfx_attack_slash", null, null)
            .Execute(ctx);

        // ② 给每个存活敌人挂焚烧
        int hit = 0;
        foreach (var enemy in combat.Enemies.Where(e => !e.IsDead).ToList())
        {
            await PowerCmd.Apply<OrcaBurnPower>(ctx, enemy, _burn, Owner.Creature, this);
            hit++;
        }

        Log.Info($"[Orca] 卷焰斩：对 {hit} 个敌人各造成 {DynamicVars.Damage.BaseValue} 点伤害 + {_burn} 层焚烧", 2);
    }

    /// <summary>
    ///     ★ **若此牌被消耗 ⇒ 立刻打出一次**（用户口径）。
    ///     挂点与狂躁的兜底钩子相同：<c>CardCmd.Exhaust</c> 里的 <c>Hook.AfterCardExhausted</c>。
    /// </summary>
    public override async Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        if (!ReferenceEquals(card, this) || CombatState == null || _replaying) return;

        try
        {
            _replaying = true;
            Log.Info("[Orca] 卷焰斩：被消耗 ⇒ 立刻再打出一次", 2);
            await CardCmd.AutoPlay(choiceContext, this, null);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 卷焰斩·被消耗再打出失败：{ex.Message}", 2);
        }
        finally
        {
            _replaying = false;
        }
    }

    /// <summary>敲后：伤害 4 → **7**，焚烧 3 → **5** 层。</summary>
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        _burn = 5;
    }
}

/// <summary>
///     ★ 飞火披肩（1 费 · **无色（不变形态）** · 技能 · 蓝卡 Uncommon · 目标自己）。
///
///     <para>用户口径：<i>"获得 10 点格挡，场上每存在 1 点【焚烧】额外获得 1 点格挡"</i>；
///     敲后 <i>"获得 13 格挡"</i>。</para>
///
///     <para>⇒ 实际格挡 = <c>基础值（10/13） + 场上所有敌人的焚烧层数之和</c>。
///     基础值走 <c>DynamicVars.Block</c>（这样卡面的 `{Block}` 会跟着升级变）。</para>
/// </summary>
public sealed class OrcaFireCloak : OrcaCard
{
    /// <summary>★ 无色 = 不改变能量球形态。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>1 费 · Skill · Uncommon · Self。</summary>
    public OrcaFireCloak() : base(1, (CardType)2, (CardRarity)3, (TargetType)1) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new BlockVar(10m, (ValueProp)8) };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;

        // ① 场上所有存活敌人的焚烧层数之和
        int burnTotal = 0;
        if (combat != null)
        {
            foreach (var enemy in combat.Enemies.Where(e => !e.IsDead))
            {
                burnTotal += (int)(enemy.GetPower<OrcaBurnPower>()?.Amount ?? 0);
            }
        }

        // ② 基础 + 焚烧加成
        int total = (int)DynamicVars.Block.BaseValue + burnTotal;
        await CreatureCmd.GainBlock(Owner.Creature, (decimal)total, (ValueProp)8, play, false);

        Log.Info($"[Orca] 飞火披肩：基础 {DynamicVars.Block.BaseValue} + 场上焚烧 {burnTotal} "
                 + $"= 获得 {total} 点格挡", 2);
    }

    /// <summary>敲后：基础格挡 10 → **13**。</summary>
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);
}
