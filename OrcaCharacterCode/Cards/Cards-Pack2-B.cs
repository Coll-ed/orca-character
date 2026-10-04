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

/// <summary>
///     ★ 睥睨（1 费 · **银龙** · 技能牌 · **金卡 Rare** · **消耗**，敲后去消耗）。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L38-40）：
///     <i>"睥睨 / 1费用，银龙，金卡，技能卡，消耗，敲后去消耗（单个图标） /
///     消耗当前所有的手牌，抽取等额张卡牌，并为所有敌人附加等额层【焚烧】，
///     并获得获得等额层buff-【睥睨】——消耗一层，使打出的牌额外打出一次"</i>
///     （"获得"重复是原文笔误，语义是"获得**等额层** buff【睥睨】"）。</para>
///
///     <para><b>实现</b>（"等额"三连共用**一个**变量 <c>n</c> ⇒ 单一来源）：
///     先把手牌逐张 <c>CardCmd.Exhaust</c>（**不含本卡自己** —— 它正在被打出）⇒
///     <c>n</c> = 实际消耗成功的张数 ⇒ 抽 <c>n</c> 张 /
///     给每个存活敌人各 <c>n</c> 层 <see cref="OrcaBurnPower" /> /
///     给自己 <c>n</c> 层 <see cref="OrcaOverlookPower" />。顺序与权威原文逐句对应。</para>
///
///     <para>「额外打出一次」由 <see cref="OrcaOverlookPower" /> 承担：新权威把措辞从
///     "使**这张牌**"改成了"使**打出的牌**" ⇒ 与本 Power 的"不分牌型、下 1 张打出的牌多打一次"
///     同口径（每多打出一次消耗 1 层）。</para>
///
///     <para>⚠️ 2026-10-04 重写前，本卡与权威有七处不符（不消耗手牌 / 固定抽 1 / 固定 1 层焚烧 /
///     睥睨恒 1 层 / 无 Exhaust / 敲后加 Retain / <c>OrbForm=None</c>），
///     旧卡面还承诺过"杀意/智慧/血统三件套"（那三个 Power 全工程无 Apply 点，已随本轮删除）。
///     逐项改前→改后见 <c>docs\睥睨重写记录.md</c>。</para>
/// </summary>
public sealed class OrcaOverlook : OrcaCard
{
    /// <summary>
    ///     ★ **银龙**体系（权威 L39「1费用，<b>银龙</b>，金卡，技能卡」）。
    ///     旧实现是 <see cref="OrcaOrbForm.None" />（不切形态），与本条不符。
    /// </summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    /// <summary>1 费 · Skill · Rare · Self（权威 L39「1费用，银龙，金卡，技能卡」）。</summary>
    public OrcaOverlook() : base(1, (CardType)2, (CardRarity)4, (TargetType)1) { }

    /// <summary>
    ///     权威 L39：「**消耗**，敲后**去消耗**」。
    ///
    ///     <para>★★ 2026-10-05 修正：**不能在 <c>CanonicalKeywords</c> 里判 <c>IsUpgraded</c>** ——
    ///     <c>CardModel.LocalKeywords</c> 是 <c>_keywords ??= UnionWith(CanonicalKeywords)</c>，
    ///     **只算一次就缓存**，升级后不重算 ⇒ 敲后卡面照样显示「消耗」（用户实机截图实锤）。
    ///     改用引擎标准做法 <c>RemoveKeyword</c>（照 <see cref="OrcaCrimsonTemper" />），
    ///     详见那边的反编译实据。</para>
    ///
    ///     <para>旧实现在 <c>OnUpgrade</c> 里加的是 <c>CardKeyword.Retain</c>（保留），
    ///     权威写的是"去消耗" ⇒ 已删除。</para>
    /// </summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 消耗当前所有的手牌（权威 L40）。
        //    不含本卡自己 —— 它正在被打出；引擎是否已把它移出手牌随版本/时序而定，
        //    这里显式排除 ⇒ 两种情况下的口径一致（"所有手牌"不含正在打出的这一张）。
        var hand = PileType.Hand.GetPile(Owner);
        var toExhaust = hand.Cards.Where(c => !ReferenceEquals(c, this)).ToList();

        // ★ "等额"的**唯一**口径 = 本次实际消耗成功的张数（抽牌 / 焚烧 / 【睥睨】层数都读它）
        int n = 0;
        foreach (var card in toExhaust)
        {
            try
            {
                await CardCmd.Exhaust(ctx, card, false, false);
                n++;
            }
            catch (Exception ex)
            {
                // 不静默吞：没消耗掉的牌不计入等额，且留下记录（权威的"等额"以实际消耗为准）
                OrcaLog.Warn($"[Orca] 睥睨：消耗 {card.Id.Entry} 失败（不计入等额张数）：{ex.Message}", 2);
            }
        }

        // ★ 边界（权威未规定，不猜）：手牌为 0 ⇒ 本卡**整条效果不发生**
        //   （抽 0 张 / 附加 0 层焚烧 / 获得 0 层【睥睨】都没有意义，而挂一个 0 层的 Power 会多出一个空图标）
        //   ⇒ 显式记 WARN，不静默通过。口径待用户确认：见 docs\睥睨重写记录.md。
        if (n == 0)
        {
            OrcaLog.Warn("[Orca] 睥睨：手牌中没有其它牌 ⇒ 本次不抽牌、不附加【焚烧】、不获得【睥睨】"
                       + "（权威未规定此边界，暂按「整条效果不发生」处理，待用户确认）", 2);
            return;
        }

        // ② 抽取等额张卡牌（权威 L40）
        await CardPileCmd.Draw(ctx, n, Owner);

        // ③ 为所有敌人附加等额层【焚烧】（权威 L40）
        var combat = Owner.Creature.CombatState;
        int hit = 0;
        if (combat == null)
        {
            OrcaLog.Warn("[Orca] 睥睨：不在战斗中 ⇒ 不附加【焚烧】", 2);
        }
        else
        {
            foreach (var enemy in combat.Enemies.Where(e => !e.IsDead).ToList())
            {
                await PowerCmd.Apply<OrcaBurnPower>(ctx, enemy, n, Owner.Creature, this);
                hit++;
            }
        }

        // ④ 获得等额层 buff【睥睨】（消耗一层 ⇒ 使打出的牌额外打出一次）（权威 L40）
        await PowerCmd.Apply<OrcaOverlookPower>(ctx, Owner.Creature, n, Owner.Creature, this);

        OrcaLog.Info($"[Orca] 睥睨：消耗 {n} 张手牌 ⇒ 抽 {n} 张；"
                 + $"给 {hit} 个敌人各 {n} 层焚烧；获得 {n} 层【睥睨】", 2);
    }

    /// <summary>敲后**去掉【消耗】**（权威 L39：「敲后去消耗（单个图标）」）。
    /// 旧实现在这里加 <c>CardKeyword.Retain</c>（保留），与权威不符，已删除。</summary>
    protected override void OnUpgrade() => CardCmd.RemoveKeyword(this, CardKeyword.Exhaust);
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
        // ★ 2026-10-04 恢复并修正：A7 原被 TOGGLE-OFF 注释掉 ⇒ 卡面里的 {Ratio} 没有值
        //   ⇒ SmartFormat 失败 ⇒ 整条卡面回退成原文。且必须用 DynamicVar（裸值同样会失败）。
        description.Add(new DynamicVar("Ratio", (decimal)(IsUpgraded ? 75 : 50)));
    }

    /// <summary>敲后：比例 50% → **75%**（在 OnPlay 里按 <c>IsUpgraded</c> 写入 Power）。</summary>
    protected override void OnUpgrade() { }
}