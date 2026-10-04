using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace OrcaCharacter;

/// <summary>
///     银龙奥卡**所有卡牌的基类**（打台词、卡面路径、魔典标记、回抽牌堆）。
///
///     <para><b>本文件是「重建源码树」的第 1 个文件</b>（用户口径 2026-10-04：
///     「重建源码树：将反编译里面的代码，按照我们的工程去自己建立」）。
///     改写前它是反编译器的直接输出，含 <c>(CardModel)(object)this</c> 强转、
///     <c>//IL_0002: Unknown result type</c> 残留、以及被反编译成魔法数的枚举
///     （<c>(PileType)1</c> / <c>(CardPilePosition)2</c>）。</para>
///
///     <para>★ <b>一处重要的判断更正</b>：反编译输出里那个 <c>&lt;&gt;n__0</c> 转发方法
///     **不是反编译残留** —— 反编译【官方基准 dll】可见官方同样有它：
///     <code>
///     [CompilerGenerated] [DebuggerHidden]
///     private Task &lt;&gt;n__0(PlayerChoiceContext ctx, CardPlay play)
///         =&gt; ((AbstractModel)this).AfterCardPlayed(ctx, play);
///     </code>
///     它是 Roslyn 对「async 方法里调用 <c>base.</c>」生成的**必需**转发器
///     （async 状态机内不能直接写 base 调用）。所以本文件手写为
///     <c>await base.AfterCardPlayed(...)</c>，编译后**同样**会生成那个转发器 —— 语义完全一致。
///     ⇒ 也因此，<c>OrcaCard</c> 被排除为「打出卡牌即卡死」的嫌疑对象：官方是同一形状。</para>
/// </summary>
public abstract class OrcaCard : CardModel
{
    /// <summary>模型 id 前缀（<c>ORCA_STRIKE</c> ⇒ 卡面资源名 <c>strike</c>）。</summary>
    private const string IdPrefix = "ORCA_";

    /// <summary>卡面资源目录（PortraitPath / BetaPortraitPath / PortraitPngPath 共用，单一来源）。</summary>
    private const string PortraitDir = "res://images/packed/card_portraits/orca/";

    /// <summary>格挡变量的键名（判定"这张牌是否给格挡" ⇒ 决定台词说哪一句）。</summary>
    private const string BlockVarKey = "Block";

    /// <summary>「本次是被【龙族魔典】免费打出」的一次性标记。</summary>
    private bool _viaCodex;

    public override CardPoolModel Pool => ModelDb.CardPool<OrcaCardPool>();

    /// <summary>这张牌属于哪个能量球形态（None = 不改变形态）。</summary>
    public virtual OrcaOrbForm OrbForm => OrcaOrbForm.None;

    /// <summary>id 去掉 <see cref="IdPrefix" /> 后小写 ⇒ 卡面资源名。</summary>
    private string AssetEntry
    {
        get
        {
            var entry = Id.Entry;
            return entry.StartsWith(IdPrefix, StringComparison.Ordinal)
                ? entry.Substring(IdPrefix.Length).ToLowerInvariant()
                : entry.ToLowerInvariant();
        }
    }

    public override string PortraitPath => PortraitDir + AssetEntry + ".png";

    public override string BetaPortraitPath => PortraitPath;

    protected override string PortraitPngPath => PortraitPath;

    public override IEnumerable<string> AllPortraitPaths => new[] { PortraitPath };

    // ── 「龙族魔典免费打出」标记（打出时由 OrcaDragonCodex 打标、OnPlay 里消费）──

    internal void MarkViaCodex() => _viaCodex = true;

    internal void ClearViaCodex() => _viaCodex = false;

    internal bool ConsumeViaCodex()
    {
        var viaCodex = _viaCodex;
        _viaCodex = false;
        return viaCodex;
    }

    /// <summary>
    ///     每张牌打出后的统一收尾：先说台词，再转交基类实现。
    ///
    ///     <para>台词分两句：按**牌型**说一句（给格挡的技能牌走另一句），再按**牌 id** 说专属台词。
    ///     <c>DynamicVars</c> 在少数时机不可用，此时按"不给格挡"处理（与原实现一致：吞掉该异常）。</para>
    /// </summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card == this)
        {
            var creature = Owner?.Creature;
            if (creature != null)
            {
                var grantsBlock = false;
                try
                {
                    grantsBlock = DynamicVars.ContainsKey(BlockVarKey);
                }
                catch
                {
                    // 原实现即如此：DynamicVars 取不到时按"不给格挡"说台词，不影响出牌。
                }

                OrcaSpeech.SayCardType(creature, (int)Type, grantsBlock);
            }

            OrcaSpeech.SayCardLine(Owner?.Creature, Id.Entry);
        }

        // ★ 手写 base 调用（Roslyn 会为它生成编译器转发器，与官方 dll 形状一致）
        await base.AfterCardPlayed(choiceContext, cardPlay);
    }

    /// <summary>把这张牌放回**抽牌堆第一位**（狂躁"本回合没打出则回顶"、逆鳞等用到）。</summary>
    internal async Task ReturnToDrawPileTop(string why)
    {
        try
        {
            var owner = Owner;
            if (owner == null) return;

            await CardPileCmd.Add(this, PileType.Draw.GetPile(owner), CardPilePosition.Top, null, false);
            OrcaLog.Info($"[Orca] {why}：{Id.Entry} 回到抽牌堆**第一位**");
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] {why}：回抽牌堆失败（{Id.Entry}）：{ex.Message}");
        }
    }

    protected OrcaCard(int cost, CardType type, CardRarity rarity, TargetType target, bool showInLibrary = true)
        : base(cost, type, rarity, target, showInLibrary)
    {
    }
}
