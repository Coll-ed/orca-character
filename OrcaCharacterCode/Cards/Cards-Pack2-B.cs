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
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★ 睥睨（1 费 · **银龙** · 技能牌 · **金卡 Rare** · **消耗**，敲后去消耗）。
///
///     <para><b>权威口径</b>（<c>work/奥卡卡包集/卡牌包2/卡牌说明2.txt</c> L38-40，2026-10-07 改版）：
///     <i>"睥睨 / 1费用，银龙，金卡，技能卡，消耗，敲后去消耗（图标） /
///     选取消耗**任意张**手牌，抽取等额卡牌，并为所有敌人附加等额层【焚烧】，
///     并获得获得等额层buff-【睥睨】——消耗一层，使打出的牌额外打出一次"</i>
///     （"获得"重复是原文笔误，语义是"获得**等额层** buff【睥睨】"。
///     ★ 改版点：旧权威写的是"消耗当前**所有**手牌"，现改为"**选取**消耗任意张"。）</para>
///
///     <para><b>实现</b>（"等额"三连共用**一个**变量 <c>n</c> ⇒ 单一来源）：
///     先用引擎的多选选择器 <c>CardSelectCmd.FromHand</c>（<c>min=0 / max=候选张数</c>）让玩家
///     **任选**，再把选中的逐张 <c>CardCmd.Exhaust</c>（候选里**不含本卡自己** —— 它正在被打出）⇒
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
        // ① 选取消耗**任意张**手牌（权威 L40，2026-10-07 改版：由「消耗当前**所有**手牌」改成「**选取**消耗任意张」）。
        //    候选里**不含本卡自己** —— 它正在被打出；引擎是否已把它移出手牌随版本/时序而定，
        //    这里显式排除 ⇒ 两种时序下的口径一致。
        var hand = PileType.Hand.GetPile(Owner);
        var candidates = hand.Cards.Where(c => !ReferenceEquals(c, this)).ToList();

        // ★ 引擎的多选选择器：min=0 / max=候选张数 ⇒ **选 0 张也合法**。
        //   实据（反编译 sts2.dll）：CardSelectorPrefs(prompt, minCount, maxCount) 会
        //   `RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect`（= 选完要点确认），
        //   见 MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs.cs:68-78。
        //   过滤器与候选集合**同一判据**（都是"不含本卡"）⇒ 出现与可选的集合不可能漂移。
        // ★ "等额"的**唯一**口径 = 本次实际消耗成功的张数（抽牌 / 焚烧 / 【睥睨】层数都读它）
        // ★★ 2026-10-07 修「自动打出时一张都烧不掉、手牌全进了弃牌堆」
        //    （用户实报：睥睨被【乱战】自动打出后，烧牌全部烧到弃牌堆了）：
        //    自动打出（乱战 / 龙族魔典免费打出 / 狂躁…）时**玩家没法做选择** ⇒
        //    引擎的选择器拿不到任何点选结果 ⇒ 一张都不消耗，回合结束时它们就按普通手牌被弃掉 ✗。
        //    ⇒ 自动打出时退化成**确定性行为**：把候选里的牌**全部**消耗掉 ——
        //      "选取任意张"里选"全部"本身就是合法选择 ✓，只是这一次不由玩家点。
        List<CardModel> chosen;
        if (candidates.Count == 0)
        {
            chosen = new List<CardModel>();
        }
        else if (play.IsAutoPlay)
        {
            OrcaLog.Info($"[Orca] 睥睨：本次是**自动打出**（乱战 / 魔典 / 狂躁…）⇒ 无法弹选择界面，"
                       + $"按「全部消耗」处理（候选 {candidates.Count} 张）", 2);
            chosen = candidates;
        }
        else
        {
            chosen = (await CardSelectCmd.FromHand(
                ctx,
                Owner,
                new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 0, candidates.Count),
                c => !ReferenceEquals(c, this),
                this)).ToList();
        }

        int n = 0;
        foreach (var card in chosen)
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
        // ★ 2026-10-07 权威改版：现在是"**选取**消耗任意张" ⇒ 选 0 张是**合法操作**，
        //   不再当作异常边界（旧文案"权威未规定此边界…待用户确认"已随本轮作废）。
        if (n == 0)
        {
            OrcaLog.Info("[Orca] 睥睨：本次没有消耗任何手牌（选了 0 张 / 手牌里只有本卡）"
                       + " ⇒ 不抽牌、不附加【焚烧】、不获得【睥睨】", 2);
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
///     ★ **归墟**（2 费 · 魔剑 · Power · Uncommon · Self，见下方构造函数）——
///     场上所有角色无法回复生命；被阻止的回复按一定比例转入【嗜血魔剑】的伤害附加；
///     **不拦截吸血**（权威 2026-10-07：<c>卡牌说明2.txt</c> L52-54）。
///
///     <para>用户口径：比例 50%，敲后 75%（两个百分点数值定义在
///     <see cref="OrcaVoidReturnPower" /> 的 <c>BasePercent</c> / <c>UpgradedPercent</c>，
///     本卡牌侧只引用，不另存副本）。</para>
///
///     <para>⚠️ <b>2026-10-05 订正</b>：本摘要原来写的是「栖途（3 费 · 银龙 · 能力牌 Power ·
///     先古 Ancient，敲后减费）」—— 那是**另一张卡** <see cref="OrcaHomestead" /> 的说明，
///     串到这里了（**既有笔误**，不是本次改动引入的）。按本类实际实现订正，免得误导下一个读代码的人。</para>
/// </summary>
public sealed class OrcaVoidReturn : OrcaCard
{
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    /// <summary>2 费 · Power · Uncommon · Self。</summary>
    public OrcaVoidReturn() : base(2, (CardType)3, (CardRarity)3, (TargetType)1) { }

    /// <summary>
    ///     本局的转入比例（敲后 75%，否则 50%）—— 卡面、日志、Power 结算共用这一个口径。
    ///
    ///     <para>★ 两个百分点数值**定义在 <see cref="OrcaVoidReturnPower" /> 里**，这里只**引用**
    ///     （纪律：同一个常量只定义一次；卡牌侧不再各留一份副本，比例换算也走 Power 侧的
    ///     <see cref="OrcaVoidReturnPower.RatioFromPercent" />）。</para>
    /// </summary>
    private int CurrentPercent => IsUpgraded ? OrcaVoidReturnPower.UpgradedPercent : OrcaVoidReturnPower.BasePercent;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var power = await PowerCmd.Apply<OrcaVoidReturnPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);
        if (power != null) power.Ratio = OrcaVoidReturnPower.RatioFromPercent(CurrentPercent);

        OrcaLog.Info($"[Orca] 归墟：场上所有角色的回复将被阻止（**不拦截【吸血】**），"
                 + $"其中 {CurrentPercent}% 转入【嗜血魔剑】的附加伤害", 2);
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
        description.Add(new DynamicVar("Ratio", (decimal)CurrentPercent));
    }

    /// <summary>敲后：比例 50% → **75%**（在 OnPlay 里按 <c>IsUpgraded</c> 写入 Power）。</summary>
    protected override void OnUpgrade() { }
}