using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace OrcaCharacter;

/// <summary>
///     **逆鳞**（3 费 · 攻击 · 稀有 · 己方，虚无 / 消耗，敲后去虚无）—— 奥卡的"拼命"牌。
///
///     <para>效果：把生命压到 1 点，按失去量获得【再生】；补满能量、抽满手牌，并获得一个额外回合。</para>
///
///     <para><b>本文件是「重建源码树」的第 11 个文件</b>。改写前是反编译直出，含
///     <c>(CardModel)(object)this</c> 强转、<c>(IEnumerable&lt;CardKeyword&gt;)(object)</c> /
///     <c>(CardKeyword[])(object)</c> 数组转换、三行 <c>//IL_</c> 残留与数字化枚举。</para>
///
///     <para>★ 枚举已查证：<c>CardKeyword.Exhaust=1</c>（消耗）、<c>CardKeyword.Ethereal=2</c>（虚无）、
///     <c>CardType.Attack=1</c>、<c>CardRarity.Rare=4</c>、<c>TargetType.Self=1</c>。</para>
///
///     <para>★ 单一来源修正：改写前 <c>20</c> 出现在 4 处（常量 / <c>DynamicVar</c> 字面量 /
///     两处算式 / 日志文本），<c>10</c> 出现在 3 处。现全部收敛到具名常量。</para>
/// </summary>
public sealed class OrcaReverseScale : OrcaCard
{
    /// <summary>失去生命 → 【再生】的转化比例（%）。</summary>
    private const int RegenPercent = 20;

    /// <summary>抽到手牌上限的抽牌数（"抽满手牌"）。</summary>
    private const int DrawToFull = 10;

    /// <summary>取不到战斗状态时的能量兜底值（正常路径不会用到）。</summary>
    private const int FallbackMaxEnergy = 3;

    /// <summary>施加的 Power 层数（相关 Power 均不叠层）。</summary>
    private const int Stacks = 1;

    /// <summary>生命被压到的下限（"失去生命到 1 点"）。</summary>
    private const int MinHpAfterPay = 1;

    public override OrcaOrbForm OrbForm => OrcaOrbForm.Dragon;

    /// <summary>消耗 + 虚无（敲后移除虚无）。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        new[] { CardKeyword.Exhaust, CardKeyword.Ethereal };

    /// <summary>卡面 <c>{Regen}</c> 显示的再生值（按"当前能失去多少生命"算，与结算同口径）。</summary>
    private int CurrentRegen
    {
        get
        {
            try
            {
                var creature = Owner?.Creature;
                if (creature == null) return 0;

                var lost = Math.Max(0, creature.CurrentHp - MinHpAfterPay);
                return RegenOf(lost);
            }
            catch
            {
                return 0;   // 百科 / 牌库浏览时还没有 Owner ⇒ 显示 0
            }
        }
    }

    /// <summary>失去 <paramref name="lostHp" /> 点生命可换到的再生量（向下取整）。</summary>
    private static int RegenOf(int lostHp) => (int)Math.Floor(lostHp * (double)RegenPercent / 100.0);

    public OrcaReverseScale()
        : base(3, CardType.Attack, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override void AddExtraArgsToDescription(LocString description)
    {
        description.Add(new DynamicVar("RegenPercent", (decimal)RegenPercent));
        description.Add(new DynamicVar("Regen", (decimal)CurrentRegen));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var me = Owner.Creature;
        var lost = Math.Max(0, me.CurrentHp - MinHpAfterPay);

        if (lost > 0)
        {
            await CreatureCmd.SetCurrentHp(me, MinHpAfterPay);
        }

        var regen = RegenOf(lost);
        if (regen > 0)
        {
            await PowerCmd.Apply<RegenPower>(ctx, me, regen, me, this, false);
        }

        var maxEnergy = Owner.PlayerCombatState?.MaxEnergy ?? FallbackMaxEnergy;
        await PlayerCmd.SetEnergy(maxEnergy, Owner);
        await CardPileCmd.Draw(ctx, DrawToFull, Owner, false);
        await PowerCmd.Apply<OrcaExtraTurnPower>(ctx, me, Stacks, me, this, false);

        OrcaLog.Info($"[Orca] 逆鳞：失去 {lost} 点生命（→{MinHpAfterPay}）⇒ 获得 {regen} 点再生（{RegenPercent}%）；"
                  + $"能量补满至 {maxEnergy}、抽 {DrawToFull} 张、并获得一个额外回合");
    }

    /// <summary>敲后去**虚无**（消耗保留）。</summary>
    protected override void OnUpgrade() => CardCmd.RemoveKeyword(this, new[] { CardKeyword.Ethereal });
}
