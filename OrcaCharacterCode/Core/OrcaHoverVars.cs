using System;
using MegaCrit.Sts2.Core.Localization;

namespace OrcaCharacter;

/// <summary>
///     ★★ <b>能力浮窗 与 关键词浮窗 共用的占位符注入</b>（唯一实现点）。
///
///     <para><b>为什么需要它</b>——同一个 <c>powers/ORCA_&lt;X&gt;_POWER.description</c> 会有**两条**渲染路径，
///     而两条**都不会**自动带上我们的变量：
///     <list type="number">
///       <item><b>buff 图标浮窗</b>：<c>PowerModel.HoverTips</c> 只调
///         <c>AddDumbVariablesToDescription</c>（Amount / singleStarIcon / energyPrefix），
///         <c>AddExtraArgsToDescription</c> 是 <c>CardModel</c> 独有的钩子
///         ⇒ 只能在 <c>PowerModel.Description</c> 覆写里注入（A7 的老结论）；</item>
///       <item><b>关键词浮窗</b>：<see cref="OrcaCardKeyword.Resolve" /> 拿 <c>LocString.GetFormattedText()</c>
///         直接格式化，**根本不会经过** <c>PowerModel</c> ⇒ 2026-10-07 实机日志实锤：
///         <c>No source extension could handle the selector named "RegenPerTrigger"</c>
///         ⇒ 玩家看到字面量 <c>{RegenPerTrigger}</c> ✗。</item>
///     </list>
///     ⇒ 两条路径都调本类，数值一律取自模型里的**具名常量**（单一来源，不给文案写死数字）。</para>
///
///     <para>★ <b>只在原文确实含该占位符时注入</b>（<see cref="Put" />）：万一将来文案把某个数字写成了
///     固定值，这里就不会往字典里塞一个同名字段去打架（与归墟的守卫同一套做法）。</para>
/// </summary>
internal static class OrcaHoverVars
{
    /// <summary>
    ///     把 <paramref name="name" /> 注入 <paramref name="loc" />（**仅当原文含该占位符**）。
    ///
    ///     <para>边界一律显式：读原文/注入失败 ⇒ 记 Warn（不静默），因为后果是"玩家可能看到
    ///     <c>{xxx}</c>"——照旧要让根因可查；调用方（关键词链）还有一道"仍带占位符就退兜底串"的安全网。</para>
    /// </summary>
    internal static void Put(LocString loc, string name, decimal value)
    {
        try
        {
            if (loc.GetRawText().Contains("{" + name + "}", StringComparison.Ordinal))
            {
                loc.Add(name, value);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 浮窗占位符 {{{name}}} 注入失败（该处可能裸露占位符）：{ex.Message}");
        }
    }

    /// <summary>【栖途】浮窗要的两个数字：每次触发给几层【再生】、每层换几点生命上限。</summary>
    internal static void Homestead(LocString loc)
    {
        Put(loc, "RegenPerTrigger", OrcaHomesteadPower.RegenPerTrigger);
        Put(loc, "MaxHpPerRegen", OrcaHomesteadPower.MaxHpPerRegen);
    }

    /// <summary>
    ///     【浴血涅槃】浮窗要的两个数字：未敲 / 敲后的回复百分点。
    ///
    ///     <para>★ 这里刻意**不用实例比例**（<c>HealRatio</c>）：关键词浮窗手上没有 Power 实例，
    ///     若文案写 <c>{HealPercent}</c>，那条路径只能瞎猜。改成"两个百分点都写出来"后，
    ///     两条路径渲染的是**同一句话**、且都正确；玩家自己那张牌的实际数值由**卡面**给
    ///     （卡面走 <c>CardModel.AddExtraArgsToDescription</c>，能按 <c>IsUpgraded</c> 现算 ✓）。</para>
    /// </summary>
    internal static void BloodNirvana(LocString loc)
    {
        Put(loc, "BasePercent", OrcaBloodNirvanaPower.BasePercent);
        Put(loc, "UpgradedPercent", OrcaBloodNirvanaPower.UpgradedPercent);
    }
}
