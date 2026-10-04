using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     ★ **龙族魔典**（1 费 · 技能 · 白卡 · 己方）—— 银龙奥卡的起始牌之一。
///
///     <para>作用：从手牌里选一张牌，**不付任何费用**把它打出一次；
///     若选中的牌带【魔剑】标签（<see cref="OrcaOrbForm.Sword" />），额外获得 1 层吸血。
///     持有【回响】（<see cref="OrcaCodexEchoPower" />）时多打一次，并消耗 1 层回响。</para>
///
///     <para><b>本文件是「重建源码树」的第 3 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)this</c> / <c>(AbstractModel)(object)</c> 强转、两处 <c>//IL_</c> 残留、
///     一处 <c>DefaultInterpolatedStringHandler</c> 机器码（本该是普通插值），
///     以及被反编译成数字的枚举。</para>
///
///     <para>★ 另外修掉两个**反编译造成的假变量**：
///     <c>CardPlay play2 = play;</c>（无意义复制）；
///     以及 <c>left2</c> 被复用于**两个不同含义**（回响剩余层数 / 循环下标）—— 手写源码里分开命名。</para>
///
///     <para>★ 全部数值已查证：<c>TargetType.AnyEnemy=2</c> / <c>AnyAlly=6</c> /
///     <c>AutoPlayType.Default=1</c> / <c>CardKeyword.Unplayable=4</c> / <c>Retain=5</c>
///     （枚举定义逐条比对过，且与各自分支的语义吻合）。</para>
/// </summary>
public sealed class OrcaDragonCodex : OrcaCard
{
    /// <summary>选牌提示语的本地化表与键（取不到时退回原版提示）。</summary>
    private const string LocTable = "cards";
    private const string LocKeyPick = "ORCA_DRAGON_CODEX.pick";

    /// <summary>免费打出的费用值。</summary>
    private const int FreeCost = 0;

    /// <summary>选中【魔剑】标签时给予的吸血层数。</summary>
    private const int LifestealStacks = 1;

    /// <summary>回响每层多打出的次数。</summary>
    private const int ExtraPlayPerEcho = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Codex;

    /// <summary>
    ///     选牌提示语（<c>ORCA_DRAGON_CODEX.pick</c>）。
    ///     ⚠️ 这里用原始 <see cref="LocString" /> + <c>Add</c>，与卡面描述的注入是两条路：
    ///     描述走 <c>AddExtraArgsToDescription</c>，这里是选择界面自己的提示。
    ///     取不到就退回原版"升级选择"提示，保证界面不会空着。
    /// </summary>
    private static LocString CodexPrompt
    {
        get
        {
            try
            {
                var prompt = new LocString(LocTable, LocKeyPick);
                prompt.Add("Amount", 1m);
                return prompt;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn("[Orca] 魔典选择提示语取不到，退回原版提示：" + ex.Message);
                return CardSelectorPrefs.UpgradeSelectionPrompt;
            }
        }
    }

    public OrcaDragonCodex()
        : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var prefs = new CardSelectorPrefs(CodexPrompt, 1)
        {
            Cancelable = true,
            PretendCardsCanBePlayed = true,
        };

        // 手牌里选一张：排除自己、排除打不出的牌（Unplayable）
        var selected = await CardSelectCmd.FromHand(
            ctx,
            Owner,
            prefs,
            c => c != play.Card && !c.Keywords.Contains(CardKeyword.Unplayable),
            this);

        var card = selected?.FirstOrDefault();
        if (card == null)
        {
            OrcaLog.Info("[Orca] 龙族魔典：没有选中任何牌（手牌为空或玩家取消）");
            return;
        }

        // 选中【魔剑】⇒ 额外 1 层吸血
        if (card is OrcaCard { OrbForm: OrcaOrbForm.Sword })
        {
            var creature = Owner.Creature;
            try
            {
                await PowerCmd.Apply<OrcaLifestealPower>(ctx, creature, LifestealStacks, creature, this, false);
                OrcaLog.Info($"[Orca] 龙族魔典：选中 {card.Id.Entry} 带【魔剑】标签 → 吸血 +{LifestealStacks} 层");
            }
            catch (Exception ex)
            {
                OrcaLog.Warn("[Orca] 龙族魔典：叠吸血失败（不影响打出）：" + ex.Message);
            }
        }

        card.EnergyCost.SetUntilPlayed(FreeCost, false);
        var target = ResolveTarget(card);

        // 回响：每层多打一次（消费 1 层）
        var extraPlays = 0;
        try
        {
            var echo = Owner.Creature.GetPower<OrcaCodexEchoPower>();
            if (echo != null && echo.Amount > 0)
            {
                var echoLeft = echo.Amount - 1;
                extraPlays = ExtraPlayPerEcho;
                await PowerCmd.Decrement(echo);
                OrcaLog.Info($"[Orca] 龙族魔典：消费【回响】1 层 → 本次额外打出 {ExtraPlayPerEcho} 次（剩余 {echoLeft} 层）");
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn("[Orca] 龙族魔典：读【回响】失败（按 1 次打）：" + ex.Message);
            extraPlays = 0;
        }

        var times = 1 + extraPlays;
        for (var i = 0; i < times; i++)
        {
            if (card is OrcaCard orcaCard) orcaCard.MarkViaCodex();

            OrcaLog.Info($"[Orca] 龙族魔典：正常打出 {card.Id.Entry}（第 {i + 1}/{times} 次，费用已改 {FreeCost}、目标={target?.Name ?? "自动"}）");

            try
            {
                await CardCmd.AutoPlay(ctx, card, target, AutoPlayType.Default, false, false);
            }
            finally
            {
                if (card is OrcaCard played) played.ClearViaCodex();
            }
        }
    }

    /// <summary>
    ///     AutoPlay 需要一个目标：<see cref="TargetType.AnyEnemy" /> ⇒ 第一个活着的敌人；
    ///     <see cref="TargetType.AnyAlly" /> ⇒ 第一个活着的**队友**（不含自己）；其余 ⇒ null（交给 AutoPlay 自决）。
    /// </summary>
    private Creature? ResolveTarget(CardModel card)
    {
        try
        {
            var combat = Owner.Creature.CombatState;
            if (combat == null) return null;

            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    foreach (var enemy in combat.Enemies)
                    {
                        if (!enemy.IsDead) return enemy;
                    }
                    return null;

                case TargetType.AnyAlly:
                    foreach (var ally in combat.Allies)
                    {
                        if (ally != null && ally.IsAlive && ally.IsPlayer && ally != Owner.Creature) return ally;
                    }
                    return null;

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn("[Orca] 魔典解析目标失败（交给 AutoPlay 随机）：" + ex.Message);
            return null;
        }
    }

    /// <summary>敲后带**保留**（用户口径：魔典敲后留手）。</summary>
    protected override void OnUpgrade() => CardCmd.ApplyKeyword(this, new[] { CardKeyword.Retain });
}
