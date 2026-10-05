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
///     <para>★ 枚举已查证：<c>CardType.Skill=2</c>、<c>CardRarity.Uncommon=3</c>、
///     <c>TargetType.Self=1</c>。（旧注释里的 <c>CardKeyword.Exhaust=1</c> 随 A5 一并删除 —— 见下方
///     <c>CanonicalKeywords</c> 处的裁定说明：权威没有「消耗」。）</para>
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

    /// <summary>
    ///     ★★ 2026-10-05（A5）裁定：**本卡没有【消耗】关键词**。
    ///
    ///     <para>权威 <c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L31 原文：
    ///     <i>「1费，无色，蓝卡，技能牌，敲后为2易2虚」</i>—— **通篇没有「消耗」**，
    ///     且敲后不取消 ⇒ 原实现比权威多一个「消耗」。</para>
    ///
    ///     <para>反证「不是漏写」：同批「睥睨」在**同一份权威** L39 写作
    ///     <i>「1费用，银龙，金卡，技能卡，消耗，敲后去消耗」</i>—— 该权威会明确写「消耗」，
    ///     龙之威仪这行不写即为没有。</para>
    ///
    ///     <para>用户 2026-10-05 裁定：「按 work 权威改」⇒ 去掉本卡的 <c>Exhaust</c>。</para>
    ///
    ///     <para>⚠ 改法依据（本项目历史坑）：<c>CardModel.LocalKeywords</c> 是
    ///     <c>_keywords ??= UnionWith(CanonicalKeywords)</c> —— **关键词只算一次就缓存**，
    ///     所以去掉关键词必须让 <c>CanonicalKeywords</c> 本身**不含**它。本卡属**静态**
    ///     <c>CanonicalKeywords</c>（全项目无任何一处对本卡调用 <c>AddKeyword</c>）⇒ 不走
    ///     <c>RemoveKeyword</c>；后者是给"敲前有、敲后没有"那种卡用的（见 <c>Cards-Sword.cs</c>
    ///     OrcaCrimsonTemper 的反编译实据）。</para>
    /// </summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => Array.Empty<CardKeyword>();

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
