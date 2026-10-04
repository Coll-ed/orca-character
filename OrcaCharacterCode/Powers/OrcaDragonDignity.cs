using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace OrcaCharacter;

/// <summary>
///     **龙之威仪**（1 费 · 技能 · 蓝卡 · 己方，敲后 2 易伤 2 虚弱）——
///     对所有敌人附加 <c>_stacks</c> 层【易伤】与同层【虚弱】。
///
///     <para><b>本文件是「重建源码树」的第 10 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(IEnumerable&lt;CardKeyword&gt;)(object)</c> 数组转换、
///     两行 <c>//IL_</c> 残留与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardKeyword.Exhaust=1</c>（该牌消耗 ✓ 规格「消耗」）、
///     <c>CardType.Skill=2</c>、<c>CardRarity.Uncommon=3</c>、<c>TargetType.Self=1</c>。</para>
/// </summary>
public sealed class OrcaDragonDignity : OrcaCard
{
    /// <summary>基础层数。</summary>
    private const int BaseStacks = 1;

    /// <summary>敲后层数（2 易伤 + 2 虚弱）。</summary>
    private const int UpgradedStacks = 2;

    /// <summary>当前层数（易伤与虚弱**同层**，规格与卡面均如此）。</summary>
    private int _stacks = BaseStacks;

    /// <summary>无色：不改变能量球形态。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>消耗。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    public OrcaDragonDignity()
        : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("Stacks", (decimal)_stacks));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var combat = Owner.Creature.CombatState;
        var hit = 0;

        if (combat != null)
        {
            // 先固定名单再逐个施加（避免遍历中集合被改动）
            foreach (var enemy in combat.Enemies.Where(e => !e.IsDead).ToList())
            {
                await PowerCmd.Apply<VulnerablePower>(ctx, enemy, _stacks, Owner.Creature, this, false);
                await PowerCmd.Apply<WeakPower>(ctx, enemy, _stacks, Owner.Creature, this, false);
                hit++;
            }
        }

        OrcaLog.Info($"[Orca] 龙之威仪：给 {hit} 个敌人各 {_stacks} 层易伤 + {_stacks} 层虚弱");
    }

    /// <summary>敲后：1 层 → 2 层（易伤与虚弱同时提高）。</summary>
    protected override void OnUpgrade() => _stacks = UpgradedStacks;
}
