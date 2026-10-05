using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace OrcaCharacter;

/// <summary>
///     ★ **嗜血龙剑**（2 费 · 技能 · 稀有 · 己方）—— 银龙奥卡的核心机制牌。
///
///     <para><b>两种打出方式</b>：
///     <list type="bullet">
///       <item><b>主动打出</b>：失去最大生命的 <see cref="BaseLifePercent" />%（敲后
///         <see cref="UpgradedLifePercent" />%），按失去量的一半累积「伤害附加」，**本次不造成伤害**；</item>
///       <item><b>被狂躁打出</b>（回合结束自动打出）：对**全体敌人**造成等同于当前伤害附加的伤害。</item>
///     </list></para>
///
///     <para><b>本文件是「重建源码树」的第 2 个文件</b>。改写前它是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、14 行 <c>//IL_xxxx</c> 残留、
///     两处 <c>DefaultInterpolatedStringHandler</c> **机器码**（本该是普通字符串插值），
///     以及被反编译成数字的枚举。改后：枚举名化、机器码还原为插值字符串、字面量收敛为具名常量。</para>
///
///     <para>★ <b>行为等价已核</b>：反编译【官方基准 dll】可见官方此文件与我们改写前**逐行相同**
///     （同样的 <c>2m</c>、同样的 5 处 <c>2</c>）⇒ 那些重复是原始源码风格，
///     本次按「单一来源」收敛，**不改变任何数值与分支**。</para>
/// </summary>
public sealed class OrcaBloodSword : OrcaFrenzyCard
{
    /// <summary>基础代价：失去最大生命的百分比。</summary>
    private const int BaseLifePercent = 20;

    /// <summary>敲后代价：失去最大生命的百分比。</summary>
    private const int UpgradedLifePercent = 30;

    /// <summary>每击杀一个敌人获得的生命上限（卡面描述 / 结算 / 日志三处共用，单一来源）。</summary>
    private const int MaxHpPerKill = 2;

    /// <summary>「失去生命」转化为「伤害附加」的比例（一半）。卡面 Gain 与结算必须同一口径。</summary>
    private const decimal BonusConversion = 0.5m;

    /// <summary>卡面/浮窗里显示的"单敌吸血倍率"（详情见 嗜血龙剑·战况 浮窗）。</summary>
    private const int LifestealPercentDisplay = 25;

    /// <summary>伤害附加（被狂躁打出时的伤害量）。</summary>
    private int _bonus;

    /// <summary>当前代价百分比（敲后变成 <see cref="UpgradedLifePercent" />）。</summary>
    private int _lifePercent = BaseLifePercent;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    internal int Bonus => _bonus;

    internal int LifePercent => _lifePercent;

    /// <summary>对外暴露的"每击杀 +生命上限"（<c>OrcaKeyword</c> 的龙剑战况浮窗要用，必须 internal）。</summary>
    internal int MaxHpPerKillValue => MaxHpPerKill;

    /// <summary>
    ///     能不能打出：代价不能把自己打死（至少留 1 点生命）。
    ///     打不出时说一句"卖血被拒"的台词（用户口径），并返回 false。
    /// </summary>
    protected override bool IsPlayable
    {
        get
        {
            try
            {
                var creature = Owner?.Creature;
                if (creature == null) return true;                          // 取不到就不拦
                if (LifeCostOf(creature.MaxHp) < creature.CurrentHp) return true;

                OrcaSpeech.SayCapped(creature, VfxDuration.Short, "swordRefuse", 1,
                    "ORCA.banter.swordRefuse.1", "ORCA.banter.swordRefuse.2", "ORCA.banter.swordRefuse.3",
                    "ORCA.banter.swordRefuse.4", "ORCA.banter.swordRefuse.5", "ORCA.banter.swordRefuse.6");
                return false;
            }
            catch
            {
                return true;                                                // 判定失败不拦牌
            }
        }
    }

    /// <summary>
    ///     本次实际要付出的生命：取"最大生命的百分比"与"当前生命 - 1"的较小者（不低于 0）。
    ///     卡面 <c>{HpLoss}</c> 与本值同源。
    /// </summary>
    internal int CurrentLifeCost
    {
        get
        {
            try
            {
                var creature = Owner?.Creature;
                if (creature == null || creature.MaxHp <= 0) return 0;

                var cost = LifeCostOf(creature.MaxHp);
                var payable = creature.CurrentHp - 1;
                return cost > payable ? Math.Max(payable, 0) : cost;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>最大生命的 <see cref="_lifePercent" />%（向下取整）。</summary>
    private int LifeCostOf(int maxHp) => (int)Math.Floor(maxHp * _lifePercent / 100.0);

    /// <summary>代价 → 伤害附加（向上取整的一半）。卡面 <c>{Gain}</c> 与结算共用此口径。</summary>
    private static int BonusOf(int lifeCost) => (int)Math.Ceiling(lifeCost * (double)BonusConversion);

    /// <summary>描述里的数值全部由 <see cref="AddExtraArgsToDescription" /> 注入（本牌无 CanonicalVars）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    /// <summary>
    ///     关键词：**空**（原来这里是「永恒」<c>CardKeyword.Eternal</c>，2026-10-05 撤掉）。
    ///
    ///     <para>★★ <b>为什么必须撤</b>（引擎实据 + 实机日志）：
    ///     <code>
    ///     CardModel:              IsRemovable     =&gt; !Keywords.Contains(CardKeyword.Eternal);
    ///     CardTransformation:     if (!card.IsTransformable) throw new InvalidOperationException(
    ///                                 "Non-removable cards cannot be transformed!");
    ///     ArchaicTooth:           await CardCmd.Transform(起始卡, 先古牌);      // 古老牙齿的唯一动作
    ///     </code>
    ///     实机日志：<c>[ERROR] System.InvalidOperationException: Non-removable cards cannot be transformed!
    ///     at ... ArchaicTooth.AfterObtained()</c>
    ///     ⇒ 龙剑挂了「永恒」⇒ 不可移除 ⇒ **不可转换** ⇒ 古老牙齿**必抛异常**；
    ///     异常让整个事件流程中断 ⇒ **事件页卡死（实测软锁）**。</para>
    ///
    ///     <para>⇒ 也就是说：这条「龙剑 → 魔剑」的升级链路**从来没成功过**
    ///     （不是欧洛巴斯那条线的问题，与它无关）。</para>
    ///
    ///     <para>⚠️ <b>代价（需用户确认）</b>：龙剑从此**可以从卡组移除**（例如商店删牌）。
    ///     这是引擎的硬规则 —— 想被古老牙齿转换，就必须可移除，**两者不可兼得**。
    ///     若坚持要"永恒"，则必须放弃古老牙齿升级，二选一。</para>
    /// </summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => Array.Empty<CardKeyword>();

    /// <summary>累积伤害附加（血焰剑鞘 / 其它牌给它加值）。</summary>
    internal void AddBonus(int amount)
    {
        if (amount <= 0) return;

        _bonus += amount;
        OrcaCardUi.Refresh(this);
    }

    public OrcaBloodSword()
        : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override void AddExtraArgsToDescription(LocString description)
    {
        var lifeCost = CurrentLifeCost;

        description.Add(new DynamicVar("LifeLoss", (decimal)lifeCost));
        description.Add(new DynamicVar("HpLoss", (decimal)lifeCost));
        description.Add(new DynamicVar("Gain", (decimal)BonusOf(lifeCost)));
        description.Add(new DynamicVar("LifePercent", (decimal)_lifePercent));
        description.Add(new DynamicVar("Bonus", (decimal)_bonus));
        description.Add(new DynamicVar("Dealt", (decimal)_bonus));
        description.Add(new DynamicVar("MaxHpPerKill", (decimal)MaxHpPerKillValue));
        description.Add(new DynamicVar("LifestealPercent", (decimal)LifestealPercentDisplay));
        // ★★ 2026-10-05 删掉写死的「永恒」（用户口径：「龙剑介绍的永恒可以删除了」）。
        //    上一版这里是 `$"...{Rampage.Title}。\n[gold]永恒[/gold]。"` ——
        //    「永恒」是**我们自己拼死的字面量**，**不会**随 CanonicalKeywords 撤掉而自动消失
        //    （我此前判断它会自动消失，是错的）。龙剑现在不带 CardKeyword.Eternal ⇒ 卡面也不该写它。
        description.Add("KeywordTags", $"[gold]{OrcaKeyword.Rampage.Title}[/gold]。");
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (IsFrenzyPlay(play))
        {
            await OnFrenzyPlay(ctx, play);
            return;
        }

        if (play.IsAutoPlay)
        {
            OrcaLog.Info("[Orca] 嗜血龙剑：本次由**龙族魔典**打出 → 走正常使用的强化（不是狂躁的全体伤害）");
        }

        var creature = Owner.Creature;
        var lifeLoss = LifeCostOf(creature.MaxHp);
        var payable = creature.CurrentHp - 1;

        if (lifeLoss > payable)
        {
            OrcaLog.Info($"[Orca] 嗜血龙剑：代价 {lifeLoss} 超过当前生命可支付上限 {payable} ⇒ 钳到 {Math.Max(payable, 0)}");
            lifeLoss = Math.Max(payable, 0);
        }

        if (lifeLoss <= 0)
        {
            OrcaLog.Info("[Orca] 嗜血龙剑·主动打出：当前生命不足以支付代价（失去 0），伤害附加不变");
            return;
        }

        await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - lifeLoss);

        var gain = BonusOf(lifeLoss);
        _bonus += gain;
        OrcaLog.Info($"[Orca] 嗜血龙剑·主动打出：失去生命 {lifeLoss}（{_lifePercent}% × 最大上限 {creature.MaxHp}）→ 伤害附加 +{gain}（累计 {_bonus}），本次不造成伤害");
        OrcaCardUi.Refresh(this);
    }

    /// <summary>被狂躁打出：对全体敌人造成等同于当前伤害附加的伤害；击杀则加生命上限。</summary>
    protected override async Task OnFrenzyPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var bonus = _bonus;
        var combat = Owner.Creature.CombatState;
        if (combat == null)
        {
            OrcaLog.Warn("[Orca] 狂躁：不在战斗中，本次不结算");
            return;
        }

        OrcaLog.Info($"[Orca] 嗜血龙剑·狂躁（回合结束自动打出）：对**全体敌人**造成 {bonus} 点伤害（伤害附加 {_bonus}）");
        if (bonus <= 0)
        {
            OrcaLog.Info("[Orca] 狂躁：伤害附加为 0 ⇒ 正常进弃牌堆（不回抽牌堆）");
            return;
        }

        try
        {
            var attack = DamageCmd.Attack(bonus).FromCard(this, play).TargetingAllOpponents(combat)
                .WithHitFx("vfx/vfx_attack_slash", null, null);

            var aliveCount = combat.Enemies.Count(e => !e.IsDead);
            OrcaLog.Info($"[Orca] 狂躁结算：攻击者={Owner.Creature.Name}、存活敌人 {aliveCount} 个、伤害 {bonus}");

            await attack.Execute(ctx);

            var hits = attack.Results.SelectMany(r => r).ToList();
            OrcaLog.Info($"[Orca] 狂躁结算完成：命中 {hits.Count} 次，合计 {hits.Sum(r => r.TotalDamage)} 点伤害"
                      + $"（{string.Join("、", hits.Select(r => $"{r.Receiver?.Name}:{r.TotalDamage}"))}）");
            OrcaLog.Info($"[Orca] 狂调结算明细：Results 组数={attack.Results.Count()}、展平后 {hits.Count} 条 → "
                      + string.Join(" | ", hits.Select(r => $"{r.Receiver?.Name}(伤{r.TotalDamage},杀={(r.WasTargetKilled ? 1 : 0)})")));

            var killed = hits.Count(r => r.WasTargetKilled);
            if (killed > 0)
            {
                var gain = killed * MaxHpPerKillValue;
                await CreatureCmd.GainMaxHp(Owner.Creature, gain);
                OrcaLog.Info($"[Orca] 嗜血龙剑击杀 {killed} 个敌人 → 生命上限 +{gain}（每个 +{MaxHpPerKillValue}）");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Orca] 狂躁结算抛异常（本次没造成伤害）：{ex}", 2);
        }
    }

    /// <summary>敲后：代价 20% → 30%，并去掉消耗、获得**固有**（用户口径"敲后固有"）。</summary>
    protected override void OnUpgrade()
    {
        _lifePercent = UpgradedLifePercent;
        CardCmd.ApplyKeyword(this, new[] { CardKeyword.Innate });
    }
}
