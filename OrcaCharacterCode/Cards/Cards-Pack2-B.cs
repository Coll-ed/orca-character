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
///
///     <para>⚠️ <b>2026-10-04（第 2 批文案修复）：本卡的卡面文案已按审计 §2.2-O1/O3 改成
///     与上面这段实现一致</b> —— 旧文案承诺的"消耗 1 张手牌"与"杀意/智慧/血统三件套"
///     在 <see cref="OnPlay" /> 里**一条都没做**（那三个 Power 在
///     <c>OrcaOverlookBuffs.cs</c> 里只有定义、全工程无 Apply 点）。
///     恢复权威口径（<c>卡牌说明2.txt</c> L40：消耗所有手牌 / 等额张数 / 单个【睥睨】）
///     需要**重写效果**，已另行安排 ⇒ 本次只动文案，一行效果代码都没改。</para>
/// </summary>
public sealed class OrcaOverlook : OrcaCard
{
    /// <summary>
    ///     抽牌数。文案与代码同源：<c>ORCA_OVERLOOK.description</c> 里是 <c>{DrawCount:diff()}</c>。
    ///     用户口径：<i>"下 1 张卡牌额外打出一次，而后将其消耗并抽取 1 张卡牌"</i>。
    /// </summary>
    private const int DrawCount = 1;

    /// <summary>
    ///     给全体敌人挂的焚烧层数。文案同源：<c>{BurnStacks:diff()}</c>。
    ///     ⚠️ 2026-10-04 修：此前这两个值**只有常量、没有 DynamicVar**
    ///     ⇒ SmartFormat 对 <c>{DrawCount:diff()}</c> 报 <c>No suitable Formatter</c>
    ///     ⇒ **整条卡面**回退成未格式化的原文（用户报的"卡面爆变量名"）。缺一个键就会整条崩，所以两个都要声明。
    /// </summary>
    private const int BurnStacks = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>
    ///     ★ 文案里用到 <c>{…:diff()}</c> 的键**必须**在这里声明 ——
    ///     声明后由基类 <c>Description</c> 流水线统一 <c>DynamicVars.AddTo(description)</c> 注入。
    ///     裸值（<c>description.Add(键, 值)</c>）只对**无格式化器**的占位符有效。
    /// </summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar("DrawCount", DrawCount),
        new DynamicVar("BurnStacks", BurnStacks),
    };

    /// <summary>1 费 · Skill · Rare · Self。</summary>
    public OrcaOverlook() : base(1, (CardType)2, (CardRarity)4, (TargetType)1) { }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 下 1 张卡牌额外打出一次
        await PowerCmd.Apply<OrcaOverlookPower>(ctx, Owner.Creature, 1m, Owner.Creature, this);

        // ② 抽 1 张
        await CardPileCmd.Draw(ctx, DrawCount, Owner);

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
        // ★ 2026-10-04 恢复并修正：A7 原被 TOGGLE-OFF 注释掉 ⇒ 卡面里的 {Ratio} 没有值
        //   ⇒ SmartFormat 失败 ⇒ 整条卡面回退成原文。且必须用 DynamicVar（裸值同样会失败）。
        description.Add(new DynamicVar("Ratio", (decimal)(IsUpgraded ? 75 : 50)));
    }

    /// <summary>敲后：比例 50% → **75%**（在 OnPlay 里按 <c>IsUpgraded</c> 写入 Power）。</summary>
    protected override void OnUpgrade() { }
}