using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;

namespace OrcaCharacter;

/// <summary>
///     卡牌能量小图标（这一版是**照文档改的**，不是猜的）。
///
///     社区教程《Adding Characters》里，卡池模型要有两个能量图标（原话）：
///       · <c>TextEnergyIconPath</c> —— 描述里用的能量图标，**24×24**
///       · <c>BigEnergyIconPath</c>  —— 提示框与**卡牌角落**用的能量图标，**74×74**
///     （BaseLib 的 <c>CustomCardPoolModel</c> 把这两个做成可覆写属性。）
///
///     vanilla 的 <c>CardPoolModel</c> 没有这两个可覆写点，只有
///     <c>EnergyIconPath = EnergyIconHelper.GetPath(EnergyColorName)</c> —— 而那条路会去
///     <c>ui_atlas</c> 的**原版预生成切片表**里找 <c>card/energy_&lt;prefix&gt;</c>，
///     我们自己丢 .tres 只会得到 <c>Missing sprite 'card/energy_orca' in ui_atlas</c>（实测日志）。
///     所以在不用 BaseLib 的前提下，等价做法就是把这条取值**改道到我们自己的普通贴图**，
///     并且按文档给足尺寸：角落 74×74、描述 24×24。
/// </summary>
[HarmonyPatch(typeof(EnergyIconHelper), nameof(EnergyIconHelper.GetPath), new[] { typeof(string) })]
internal static class OrcaEnergyIconPatch
{
    /// <summary>卡牌角落 / 提示框用（文档要求 74×74）；两套皮肤各一张静态图。</summary>
    private const string PlateIconPath = "res://images/ui/card/energy_orca.png";            // 板甲 = 灰黑
    private const string WeddingIconPath = "res://images/ui/card/energy_orca_wedding.png";  // 婚纱 = 白红

    private static bool Prefix(string prefix, ref string __result)
    {
        if (!string.Equals(prefix, "orca", StringComparison.OrdinalIgnoreCase)) return true;
        __result = OrcaSkin.IsWedding ? WeddingIconPath : PlateIconPath;
        return false;
    }
}