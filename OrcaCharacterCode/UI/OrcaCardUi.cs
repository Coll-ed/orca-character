using System;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace OrcaCharacter;

/// <summary>
///     ★★ 手动重绘一张卡在桌面上的**卡面文字**
///     —— 修的就是用户报的「**打出后扣血但是不强化**」。
///
///     <para>
///     真因：数值其实加了（实机日志实锤 <c>伤害附加 +6（累计 6）</c>、<c>+5（累计 11）</c>），
///     但**卡面不会自己重算**。反编译 <c>NCard</c>：
///     </para>
///     <list type="bullet">
///       <item>说明标签是**烘死**的：<c>UpdateVisuals</c> 第 888 行 <c>Model.GetDescriptionForPile(...)</c>
///         → 892 行 <c>_descriptionLabel.SetTextAutoSize(...)</c>，只有这个流程会重写它；</item>
///       <item><c>NCard</c> 只订阅了 <c>AfflictionChanged</c> / <c>EnchantmentChanged</c>
///         ⇒ 我们自己的 <c>_bonus</c> 变化它**一无所知**。</item>
///     </list>
///
///     <para>
///     官方 wiki（《Modding Basics》）的原则是 <i>"Always try to use commands over directly manipulating data"</i>、
///     <i>"check the code the base game uses… and adapt that code"</i> ⇒ 这里就照原版自己的做法：
///     <c>NCard.FindOnTable(card)</c>（原版 <c>CardModel</c> 里也是这么找桌面卡节点的）
///     + 公开的 <c>NCard.UpdateVisuals(PileType, CardPreviewMode)</c>。
///     </para>
///
///     <para>
///     全部包在 try/catch 里：卡可能不在桌面上（牌库/百科/正在飞），拿不到节点就安静跳过，
///     **绝不让一次重绘失败影响卡牌结算**。
///     </para>
/// </summary>
internal static class OrcaCardUi
{
    /// <summary>重绘这张卡在桌面上的显示（取不到节点就跳过）。</summary>
    internal static void Refresh(CardModel? card)
    {
        if (card == null) return;

        try
        {
            var node = NCard.FindOnTable(card);
            if (node == null) return;

            // 用卡当前所在牌堆（拿不到就按"在手牌里"算，这是最常见的显示场景）
            var pile = card.Pile?.Type ?? PileType.Hand;
            node.UpdateVisuals(pile, CardPreviewMode.Normal);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 卡面重绘失败（不影响结算）：{ex.Message}", 2);
        }
    }
}