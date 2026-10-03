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

// ⚠️ 部分恢复：会话记录里 Cards.cs 只留下这一部分（约 34% ✗，且从方法中间截断）。
public sealed class OrcaBloodSword : OrcaCard
{
    /// <summary>
    ///     「强化值」—— 本场战斗内累计的伤害附加（用户口径 2026-09-16 定稿：**这张牌在本场战斗内临时提升**）。
    ///     每次主动打出（或被魔典强制打出）失去生命后，按失去量的 50%（向上取整）累加。
    ///     ⚠️ 是**字段**而不是 <c>DynamicVars</c>：战斗结束后随卡牌实例一起消失 = 天然"仅本场战斗"；
    ///        <c>_bonus</c> 绝不能塞进 <c>DynamicVars</c>，否则会被算进卡面的基础伤害数字。
    /// </summary>
    private int _bonus;

    /// <summary>
    ///     主动打出时"失去当前生命的百分比"（%）。默认 20，升级后 30 ——
    ///     升级加的是**这个比例**，因为本牌的基础伤害已被移除（打 0 点），加基础伤害毫无意义。
    /// </summary>
    private int _lifePercent = 20;

    /// <summary>
    ///     「此牌击杀敌人获得 N 点生命上限」的 N。用户口径 2026-09-16：**固定 2 点**
    ///     （原来是 1 点）。卡面那一行与这里的常量同源，改一处两处都变。
    /// </summary>
    private const int MaxHpPerKill = 2;

    /// <summary>★ 形态标签：打出这张牌时能量球切成<b>魔剑</b>形态（放大穿出外环）。</summary>
    public override OrcaOrbForm OrbForm => OrcaOrbForm.Sword;

    // ── 给"放大细看"的悬停浮窗读的只读数值（见 OrcaKeyword.cs 的 OrcaSwordState）──
    /// <summary>当前累计的伤害附加（本场战斗内）。</summary>
    internal int Bonus => _bonus;

    /// <summary>主动打出时失去当前生命的百分比。</summary>
    internal int LifePercent => _lifePercent;

    /// <summary>击杀一个敌人给多少生命上限。</summary>
    internal int MaxHpPerKillValue => MaxHpPerKill;

    /// <summary>此刻主动打出会失去多少生命（不在战斗中时为 0）。</summary>
    internal int CurrentLifeCost
    {
        get
        {
            try
            {
                var creature = Owner?.Creature;
                return creature == null || creature.CurrentHp <= 0
                    ? 0
                    : (int)Math.Floor(creature.CurrentHp * _lifePercent / 100.0);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    ///     ★ 本牌**没有任何自己的伤害**（用户口径："默认为造成（伤害附加）点伤害，**没强化就是 0**"）。
    ///
    ///     所以 <c>CanonicalVars</c> 里**不放 <c>DamageVar</c>** ——
    ///     之前那张 <c>DamageVar(8m)</c> 只是"让卡看起来像攻击牌"的装饰，我却把它加进了伤害计算，
    ///     就是用户质问的「为什么硬编码了 8 点伤害」。现在本牌是**技能牌**、基础伤害 0，
    ///     狂躁打出的伤害**只等于伤害附加**（<c>_bonus</c>），卡面数字由 <c>{Dealt}</c> 提供。
    ///     （<c>_bonus</c> 是"本场战斗内累加"的临时值，也**不能**塞进 <c>DynamicVars</c>，
    ///       否则会被当成卡面基础伤害。）
    /// </summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    /// <summary>「永恒」= **原版** <c>CardKeyword.Eternal</c>（不可被移除），用户口径如此。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Eternal };

    /// <summary>
    ///     ★★★ **必须 override 成 true，否则整个狂躁都不会触发！**
    ///
    ///     反编译 <c>CombatManager</c>（回合结束那段）：
    ///     <code>
    ///     foreach (CardModel card in pile.Cards)
    ///         if (card.HasTurnEndInHandEffect) turnEndCards.Add(card);   // ← 这里是门槛
    ///         else                            list.Add(card);
    ///     …
    ///     await card.OnTurnEndInHandWrapper(choiceContext);              // ← 才轮到我们的代码
    ///     </code>
    ///     而 <c>CardModel.HasTurnEndInHandEffect</c> 是 <c>public virtual bool =&gt; false</c>，
    ///     只重写 <c>OnTurnEndInHand</c> **不会**让它被调用 —— 2026-09-16 实机"自动打出不生效"就是这个原因。
    /// </summary>
    public override bool HasTurnEndInHandEffect => true;

    /// <summary>
    ///     这是一张**技能牌**（`CardType.Skill = 2`），不是攻击牌 —— 用户口径 2026-09-16 定稿。
    ///
    ///     <para>
    ///     为什么必须是技能：本牌**自己没有伤害数字**。
    ///     主动打出是"失去生命换伤害附加、本次不造成伤害"；只有被狂躁打出时才把伤害附加
    ///     转成**对所有敌人的伤害**。<c>CardType.Attack</c> 会在卡面渲染伤害图标/数值，
    ///     而我们没有 <c>DamageVar</c> ⇒ 卡面挂个 0 毫无意义（用户问过"为什么硬编码 8 点"）。
    ///     </para>
    ///
    ///     <para>
    ///     ★★ 目标类型是 <c>TargetType.Self = 1</c>（用户口径 2026-09-16 修订：
    ///     「**龙剑的目标应该是自己，不是敌方全体**」「和放血一样的打出」）。
    ///
    ///     先用过 <c>AllEnemies = 3</c> —— 那是把"狂躁对全体敌人造成伤害"误当成了**卡牌的目标类型**；
    ///     实际上本牌打出时**不指定任何敌方目标**（只有狂躁结算内部才用
    ///     <c>AttackCommand.TargetingAllOpponents</c> 自己去扫全体，与卡面 TargetType 无关）。
    ///     改成 <c>Self</c> 之后：
    ///       · 卡面不再要求选敌人（和「放血」一样直接打出）；
    ///       · <c>CardCmd.AutoPlay</c> **只对 AnyEnemy / AnyAlly 做目标解析与换目标检查**
    ///         （<c>CardCmd.cs:73</c> / <c>:85</c>）⇒ <c>Self</c> 直接跳过，那个"必须传合法目标
    ///         否则回合卡死"的历史坑从根上消失。
    ///     </para>
    /// </summary>
    public OrcaBloodSword() : base(2, (CardType)2, (CardRarity)4, (TargetType)1) { }   // 2 = Skill / 1 = Self

    /// <summary>
    ///     ★★ 卡面文案的动态变量 + 关键词标签置顶。
    ///
    ///     <para>
    ///     挂 <c>AddExtraArgsToDescription</c>（基类空实现，且**排在 DynamicVars.AddTo 之后**调用）
    ///     ⇒ 每次渲染卡面文字时现场算一遍：
    ///       · <c>{LifePercent}</c>  = 失去当前生命的百分比（20 / 升级后 30）—— **卡面显示这个**
    ///       · <c>{Bonus}</c>        = 当前累计的伤害附加（本场战斗内）
    ///       · <c>{Dealt}</c>        = 狂躁会打出多少伤害 ＝ **就是伤害附加本身**（没强化 = 0）
    ///       · <c>{MaxHpPerKill}</c> = 击杀一个敌人给多少生命上限（固定 2）
    ///     </para>
    ///
    ///     <para>
    ///     ★ 用户口径 2026-09-16：「**龙剑放大细看卡牌的时候，才会显示隐藏的百分比数字**」
    ///     ⇒ 卡面只写**规则**（百分比 + 伤害附加），"此刻会失去多少生命"这类**实际数字**
    ///     改由**悬停浮窗**给出（见 <c>OrcaKeyword.cs</c> 的 <c>OrcaSwordState</c>）。
    ///     依据：原版卡面没有独立"放大版"，<c>NCardHolder.DoCardHoverEffects</c>（第 448 行）
    ///     放大时唯一额外出现的就是 `CreateHoverTips()`（第 456 行）⇒ 浮窗正是"细看"那一层。
    ///     </para>
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        int lifeLoss = 0;
        try
        {
            var creature = Owner?.Creature;
            if (creature != null && creature.CurrentHp > 0)
                lifeLoss = (int)Math.Floor(creature.CurrentHp * _lifePercent / 100.0);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 龙剑卡面数字：取当前生命失败（按 0 显示）：{ex.Message}", 2);
        }

        description.Add("LifeLoss", lifeLoss);
        description.Add("HpLoss", lifeLoss);             // 卡面直接显示的具体数值（用户口径："（具体数值）"）
        description.Add("Gain", (lifeLoss + 1) / 2);     // ceil(lifeLoss × 50%)：这次能加多少伤害附加
        description.Add("LifePercent", _lifePercent);
        description.Add("Bonus", _bonus);
        description.Add("Dealt", _bonus);
        description.Add("MaxHpPerKill", MaxHpPerKill);
        description.Add("LifestealPercent", OrcaLifestealPower.Percent);

        // ★★ 关键词标签（用户口径：**永恒与狂躁两个标签放到最上面**）
        //    原版把关键词排在说明**之后**（CardKeywordOrder.afterDescription），要挪到最上面只能自己拼：
        //    `{KeywordTags}` 写在说明字符串的**开头**，同时用上面那个 Postfix 把原版自动追加的「永恒。」
        //    从 GetDescriptionForPile 的返回值里摘掉（否则会重复出现一次）。
        description.Add("KeywordTags", $"[gold]{OrcaSwordState.Title}[/gold]。\n");
    }

    /// <summary>
    ///     ★★ 用户口径 2026-09-16（最终定稿，逐条对照）：
    ///     <code>
    ///     IF   玩家主动打出 / 魔典打出：
    ///             失去生命获得（强化值）点伤害附加，**不造成伤害**，进入正常流程
    ///     ELSE 打出（狂躁：回合结束自动打出）：
    ///             造成（强化值）点伤害，进入正常流程；若当时不在手牌 ⇒ **回到抽牌堆**
    ///     </code>
    ///     ⇒ 所以 <c>OnPlay</c> 里**不再有任何攻击**；伤害只发生在"狂躁自动打出"那一次路径
    ///        （也就是本方法里 <c>play.IsAutoPlay &amp;&amp; !forcedByCodex</c> 的那个分支）。
    ///     </summary>
    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        // ══════════ ELSE 分支：狂躁（回合结束自动打出）⇒ 对全体敌人造成（伤害附加）点伤害 ══════════
        // ★ 判定只看 IsAutoPlay：本牌**不再有任何"被魔典强制打出"的特判**
        //   （用户口径 2026-09-17：吸血是**魔典**的逻辑；魔典现在就是"正常打出"选中的牌）。
        if (play.IsAutoPlay)
        {
            await FrenzyStrike(ctx, play);
            return;
        }

        // ══════════ IF 分支：主动打出 ⇒ 失去生命换伤害附加，**不造成伤害** ══════════
        var creature = Owner.Creature;
        var lifeLoss = (int)Math.Floor(creature.CurrentHp * _lifePercent / 100.0);
        if (lifeLoss > 0)
        {
            // 生命"失去"（非伤害）⇒ 直接设当前生命，避免走伤害管线触发受击遗物
            await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - lifeLoss);
            int gain = (int)Math.Ceiling(lifeLoss * 0.5);
            _bonus += gain;
            Log.Info($"[Orca] 嗜血龙剑·主动打出：失去生命 {lifeLoss}（{_lifePercent}%）→ 伤害附加 +{gain}（累计 {_bonus}），本次不造成伤害", 2);

            // ★ 立刻重绘卡面（否则玩家看到的就是"扣了血但没强化"——
            //    数值确实加了，但 NCard 的说明标签是烘死的，不会自己重算）
            OrcaCardUi.Refresh(this);
        }
        else
        {
            Log.Info("[Orca] 嗜血龙剑·主动打出：当前生命不足以支付代价（失去 0），伤害附加不变", 2);
        }

        // 「进入正常流程」＝ 交给原版默认去向（弃牌堆）。
        // ★★ 这里**绝不能**加 CardKeyword.Retain —— 用户实测："不能有保留！狂躁无法自动打出"。
        //     原因：Retain 让它回合结束仍留在手牌 ⇒ 它永远不进弃牌堆 ⇒
        //     ①「永恒」的"进弃牌堆就回抽牌堆"不触发，②下回合抽不到第二张，狂躁那条链条断掉。
        //     它回手牌的路径是**正常循环**：弃牌堆 →（永恒）→ 抽牌堆 → 下回合抽到手里。
    }

    /// <summary>
    ///     ELSE 分支：狂躁 —— **对全体敌人**造成 **（伤害附加）** 点伤害。
    ///
    ///     <para>
    ///     ★ 用户口径（2026-09-16 修正）：<i>"默认为造成（伤害附加）点伤害，没强化就是 0"</i>。
    ///     所以这里**只有 `_bonus`、没有任何基础伤害** —— 之前写成
    ///     <c>_bonus + DynamicVars.Damage.BaseValue</c> 是错的：那张 <c>DamageVar(8)</c> 是**卡面上的
    ///     展示数字**，不是"这牌自带 8 点伤害"。没强化时 <c>_bonus == 0</c> ⇒ 打 0 点（等于没伤害），完全正确。
    ///     </para>
    ///
    ///     <para>
    ///     ★ 用户口径（同一条）：<i>"打出了，但是没有造成伤害……对全体敌人造成伤害，这样应该会绕过去"</i>
    ///     ⇒ 改成 <c>TargetingAllOpponents(combatState)</c> 的**全体扫**，不再依赖 <c>play.Target</c> 那个单体目标。
    ///     （单体路径要走目标解析/合法性校验，容易出现"打出去但没结算"；全体扫不需要目标。）
    ///     </para>
    /// </summary>
    private async Task FrenzyStrike(PlayerChoiceContext ctx, CardPlay play)
    {
        // ① 数字先定下来：**就是伤害附加本身**（没强化 = 0）
        int damage = _bonus;

        var combat = Owner.Creature.CombatState;
        if (combat == null)
        {
            Log.Warn("[Orca] 狂躁：不在战斗中，本次不结算", 2);
            return;
        }

        Log.Info($"[Orca] 嗜血龙剑·狂躁（回合结束自动打出）：对**全体敌人**造成 {damage} 点伤害（伤害附加 {_bonus}）", 2);

}
}