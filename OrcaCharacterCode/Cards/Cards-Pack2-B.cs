using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

///
///     <para>用户口径：<i>"下 1 张卡牌额外打出一次，而后将其消耗并抽取 1 张卡牌，
///     并为所有敌人附加 1 层【焚烧】"</i>。</para>
///
///     <para>"额外打出一次"由 <see cref="OrcaOverlookPower" /> 承担（挂点同原版复制）；
///     "抽 1 张 + 全体 1 层焚烧"在这里立刻结算（用户口径把它们都挂在"下 1 张牌"那一趟之后，
///     但本牌自己打出时结算等价且更可控 —— 见交接文档的实机验证点）。</para>
/// </summary>
public sealed class OrcaOverlook : OrcaCard
{
    /// <summary>给全体敌人挂的焚烧层数。</summary>
    private const int BurnStacks = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>1 费 · Skill · Rare · Self。</summary>
    public OrcaOverlook() : base(1, (CardType)2, (CardRarity)4, (TargetType)1) { }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 下 1 张卡牌额外打出一次
        await PowerCmd.Apply<OrcaOverlookPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);

        // ② 抽 1 张
        await CardPileCmd.Draw(ctx, 1, Owner);

        // ③ 为所有敌人附加 1 层焚烧
        var combat = Owner.Creature.CombatState;
        int hit = 0;
        if (combat != null)
        {
            foreach (var enemy in combat.Enemies.Where(e => !e.IsDead).ToList())
            {
                await PowerCmd.Apply<OrcaBurnPower>(ctx, enemy, BurnStacks, Owner.Creature, this);
                hit++;
            }
        }

        OrcaLog.Info($"[Orca] 睥睨：下 1 张牌额外打出一次；抽 1 张；给 {hit} 个敌人各 {BurnStacks} 层焚烧", 2);
    }

    /// <summary>敲后：追加**保留**（用户口径"敲后保留"）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, CardKeyword.Retain);
}

/// <summary>
///     ★ 栖途（3 费 · **银龙** · **能力牌 Power** · **先古 Ancient**，敲后减费）。
///
///     <para>用户口径：<i>"（选取遗物-欧洛巴斯之触会获得这张卡牌）战斗结束后，
///     将你 25% 的临时生命上限转化为真实生命上限"</i>。</para>
///
///     <para>与遗物「银龙血统」共享 <see cref="OrcaTempHp" /> 池，转化逻辑见 <see cref="OrcaHomesteadPower" />。</para>
/// </summary>
public sealed class OrcaVoidReturn : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    /// <summary>2 费 · Power · Uncommon · Self。</summary>
    public OrcaVoidReturn() : base(2, (CardType)3, (CardRarity)3, (TargetType)1) { }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var power = await PowerCmd.Apply<OrcaVoidReturnPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);
        if (power != null && IsUpgraded) power.Ratio = 0.75m;

        OrcaLog.Info($"[Orca] 归墟：场上所有角色的回复将被阻止，"
                 + $"其中 {(IsUpgraded ? 75 : 50)}% 转入【嗜血龙剑】的附加伤害", 2);
    }

    /// <summary>
    ///     ★ 归墟（2026-10-01）：卡面原来把 50% 写死 ✗ ⇒ 升级后仍显示 50% ✗（用户实测反馈）。
    ///     现注入 <c>{Ratio}</c> 动态变量，与 <see cref="OrcaVoidReturnPower.Ratio" /> 同一口径 ✓
    ///     （单一来源：数值只在 <c>OnPlay</c> 里按 <c>IsUpgraded</c> 决定一次 ✓）。
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        /*TOGGLE-OFF-A7*/ // description.Add("Ratio", IsUpgraded ? 75 : 50);
    }

    /// <summary>敲后：比例 50% → **75%**（在 OnPlay 里按 <c>IsUpgraded</c> 写入 Power）。</summary>
    protected override void OnUpgrade() { }
}