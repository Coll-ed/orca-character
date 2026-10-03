using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★★ 嗜血魔剑 —— **嗜血龙剑的先古版**（用户口径 2026-09-16）。
///
///     <para>获得方式：原版遗物「**古老牙齿**」（<c>ARCHAIC_TOOTH</c>，拾起时把一张初始卡牌变化为先古版本）
///     ⇒ 把「嗜血龙剑 → 嗜血魔剑」注册进它的映射即可（见 <c>OrcaAncientTooth.cs</c>）。</para>
///
///     <para>用户给的效果原文：
///     <code>
///     嗜血魔剑 / 永恒 / 狂躁
///     消耗当前生命 25%（具体数值），获得 1 层吸血，并附加全额失去生命（具体数值）点伤害
///     当被狂躁打出时，对所有敌人造成附加数值（具体数值）点伤害，
///     若只对一名敌人造成伤害，吸血的倍率提升到 50%
///     </code>
///     追问确认：**1 费**、稀有度 **Ancient（先古）**、敲后**固有**。</para>
///
///     <para>与龙剑的三处差别：
///     <list type="number">
///       <item>失去生命 **25%**（龙剑 20%）；</item>
///       <item>附加的是**全额**失去生命（龙剑只附加 50%）；</item>
///       <item>主动打出时**额外叠 1 层吸血**；狂躁打出时**只有一名敌人**的话吸血倍率升到 50%。</item>
///     </list></para>
///
///     <para>⚠️ 用户口径里**没有**提到"击杀 +生命上限"（龙剑有）⇒ 本牌**暂不实现**该条，待确认。</para>
/// </summary>
public sealed class OrcaBloodBlade : OrcaCard
{
    /// <summary>本场战斗内累计的伤害附加（与龙剑同一套写法：是字段，不是 DynamicVars）。</summary>
    private int _bonus;

    /// <summary>主动打出时失去当前生命的百分比（用户口径：25%）。</summary>
    private const int LifePercent = 25;

    /// <summary>★ 形态标签：魔剑体系（打出时能量球切成魔剑形态）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    /// <summary>「永恒」＝ 原版 <c>CardKeyword.Eternal</c>。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Eternal };

    /// <summary>★ 狂躁：回合结束时仍在手牌 ⇒ 自动打出（必须 override，否则整条链不触发）。</summary>
    public override bool HasTurnEndInHandEffect => true;

    /// <summary>★ 1 费 · 技能 · **先古 Ancient(5)** · 目标是自己。</summary>
    public OrcaBloodBlade() : base(1, (CardType)2, (CardRarity)5, (TargetType)1) { }

    /// <summary>本牌**没有任何自己的伤害**（基础伤害 0），所以不放 <c>DamageVar</c>。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    /// <summary>卡面文案的动态变量 + 关键词标签置顶（狂躁 / 永恒）。</summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int lifeLoss = 0;
        try
        {
            var creature = Owner?.Creature;
            if (creature != null && creature.CurrentHp > 0)
                lifeLoss = (int)Math.Floor(creature.CurrentHp * LifePercent / 100.0);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 魔剑卡面数字：取当前生命失败（按 0 显示）：{ex.Message}", 2);
        }

        description.Add("LifeLoss", lifeLoss);
        description.Add("HpLoss", lifeLoss);
        description.Add("Gain", lifeLoss);              // ★ 全额（龙剑是 50%）
        description.Add("LifePercent", LifePercent);
        description.Add("Bonus", _bonus);
        description.Add("Dealt", _bonus);
        description.Add("LifestealPercent", 50);        // 单敌时的吸血倍率
        description.Add("KeywordTags", $"[gold]{OrcaSwordState.Title}[/gold]。\n[gold]永恒[/gold]。");
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ══════════ 狂躁（回合结束自动打出 / 被召唤出来直接打出）⇒ 全体敌人伤害 ══════════
        bool viaCodex = ConsumeViaCodex();
        if (play.IsAutoPlay && !viaCodex)
        {
            await FrenzyStrike(ctx, play);
            return;
        }

        // ══════════ 主动打出 / 魔典打出 ⇒ 25% 生命换全额附加 + 1 层吸血 ══════════
        if (viaCodex)
            Log.Info("[Orca] 嗜血魔剑：本次由**龙族魔典**打出 → 走正常使用的强化", 2);

        var me = Owner.Creature;
        int lifeLoss = (int)Math.Floor(me.CurrentHp * LifePercent / 100.0);
        if (lifeLoss <= 0)
        {
            Log.Info("[Orca] 嗜血魔剑·主动打出：当前生命过低，无可支付（附加不变）", 2);
            return;
        }

        // 生命"失去"（非伤害）⇒ 直接设当前生命，绕开伤害管线（与龙剑同套写法）
        await CreatureCmd.SetCurrentHp(me, me.CurrentHp - lifeLoss);
        _bonus += lifeLoss;                      // ★ 全额附加
        Log.Info($"[Orca] 嗜血魔剑·主动打出：失去生命 {lifeLoss}（{LifePercent}%）→ "
                 + $"伤害附加 +{lifeLoss}（累计 {_bonus}），本次不造成伤害", 2);

        // ★ 额外获得 1 层吸血（用户口径）
        await PowerCmd.Apply<OrcaLifestealPower>(ctx, me, 1m, me, this);
        Log.Info("[Orca] 嗜血魔剑：获得 1 层【吸血】", 2);

        OrcaCardUi.Refresh(this);                // 卡面数字立刻重绘
    }

    /// <summary>
    ///     狂躁：**对所有敌人**造成（伤害附加 × 红莲淬倍率）点伤害。
    ///     ★ 若只对**一名**敌人造成伤害 ⇒ 给吸血 Power 设"本次倍率 50%"（用户口径）。
    /// </summary>
    private async Task FrenzyStrike(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;
        if (combat == null)
        {
            Log.Warn("[Orca] 魔剑·狂躁：不在战斗中，本次不结算", 2);
            return;
        }

        int aliveEnemies = combat.Enemies.Count(e => !e.IsDead);

        // ★ 红莲淬：消费倍率档数（1 + 0.1×档数）
        decimal mult = 1m;
        var temper = Owner.Creature.GetPower<OrcaCrimsonTemperPower>();
        if (temper != null)
        {
            mult = temper.Multiplier;
            temper.Consume();
        }

        int damage = (int)Math.Floor(_bonus * mult);

        // ★ 单敌 ⇒ 吸血倍率提升到 50%（覆盖只作用一次，吸血结算后自动清零）
        if (aliveEnemies <= 1)
        {
            var ls = Owner.Creature.GetPower<OrcaLifestealPower>();
            if (ls != null)
            {
                // [已废弃] ls.PercentOverride = 50;  ← OrcaLifesteal.cs 新版本已删除该覆盖机制（原文见其 L89）✓
                // 单敌 50% 的效果改由 OrcaLifestealPower 内部按 MaxBladePercent 处理 ✓
                Log.Info("[Orca] 嗜血魔剑·狂躁：只对一名敌人造成伤害 ⇒ 本次吸血倍率提升到 50%", 2);
            }
        }

        if (damage <= 0)
        {
            Log.Info("[Orca] 魔剑·狂躁：伤害为 0（伤害附加为空）⇒ 只走流程", 2);
            return;
        }

        try
        {
            var attack = DamageCmd.Attack((decimal)damage).FromCard(this, play)
                .TargetingAllOpponents(combat)
                .WithHitFx("vfx/vfx_attack_slash", null, null);
            Log.Info($"[Orca] 嗜血魔剑·狂躁：对全体敌人造成 {damage} 点伤害"
                     + $"（附加 {_bonus} × 红莲淬 {mult:0.0}）", 2);
            await attack.Execute(ctx);
        }
        catch (Exception ex)
        {
            Log.Error($"[Orca] 魔剑·狂躁结算抛异常（本次没造成伤害）：{ex}", 2);
        }
    }

    /// <summary>狂躁：回合结束时仍在手牌 ⇒ 自动打出（传 null 因为目标是自己）。</summary>
    protected override async Task OnTurnEndInHand(PlayerChoiceContext ctx)
    {
        Log.Info("[Orca] 魔剑·狂躁触发：回合结束仍在手牌 → 自动打出（全体敌人）", 2);
        await CardCmd.AutoPlay(ctx, this, null);
    }

    /// <summary>敲后追加**固有**（用户口径："龙剑和魔剑都是敲后固有"）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, CardKeyword.Innate);
}
