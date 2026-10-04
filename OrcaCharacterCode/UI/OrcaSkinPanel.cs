using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **模组内置的皮肤面板**（移植自 <c>CharacterSkinManager</c>，用户口径 2026-10-04：
///     「把它移植整合到这个模组里面来，优先读自带的配置」）。
///
///     <para><b>为什么自己建 UI 而不是依赖外部 mod</b>：外部那个 <c>CharacterSkinManager</c>
///     会被游戏的模组设置禁用（godot.log 实据：
///     <c>Skipping loading mod CharacterSkinManager, it is set to disabled in settings</c>），
///     一旦禁用面板就没了。整合进本体后，本模组不依赖任何第三方也能切换板甲/婚纱。</para>
///
///     <para><b>移植源的实现要点</b>（反编译 <c>CharacterSkinManager.dll</c> 实据）：
///     <list type="bullet">
///       <item>挂点 = <c>[HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]</c>；</item>
///       <item>父节点 = <c>NCharacterSelectScreen</c> 的私有 <c>Control _bgContainer</c>；</item>
///       <item>幂等 = <c>GetNodeOrNull(面板名) == null</c> 才建；</item>
///       <item>锚点 = <c>SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, KeepSize)</c> + 负偏移。</item>
///     </list></para>
///
///     <para><b>不新增任何 pck 资源</b>：面板全部用 Godot 的 <c>Control</c>/<c>Button</c> 运行时构造。
///     这一点对本工程很重要 —— 我们的 pck 是「官方基底 + 增量」合并出来的，
///     每加一个资源都要重走合并流程；纯代码建 UI 则只改 dll。</para>
///
///     <para><b>写入</b>：按钮回调调 <see cref="OrcaSkin.SaveActive" />，写模组目录下的
///     <c>orca_skin.json</c>（自有配置，读取优先级最高）；<c>Set()</c> 内部会就地刷新
///     已在屏上的角色小图与立绘，所以点完立刻能看到变化。</para>
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]
internal static class OrcaSkinPanelPatch
{
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        try
        {
            OrcaSkinPanel.EnsureInjected(__instance);
        }
        catch (Exception ex)
        {
            // 面板建不出来不该影响选人界面 —— 但绝不静默：留日志
            OrcaLog.Warn($"[Orca] 皮肤面板注入失败（不影响选人与游戏）：{ex.Message}", 2);
        }
    }
}

/// <summary>皮肤面板的构造与刷新。纯代码建 UI，见 <see cref="OrcaSkinPanelPatch" /> 的说明。</summary>
internal static class OrcaSkinPanel
{
    /// <summary>面板根节点名（幂等检查用）。</summary>
    private const string PanelNodeName = "OrcaSkinPanel";

    /// <summary>面板尺寸与位置（相对右下角的负偏移，照移植源的做法）。单位 = 像素。</summary>
    private const float PanelWidth = 250f;
    private const float PanelHeight = 96f;
    private const float MarginRight = 24f;
    private const float MarginBottom = 190f;   // 往上让开底部那排角色小图

    private const int TitleFontSize = 16;
    private const int ButtonFontSize = 16;

    /// <summary>两个可选皮肤（与 <see cref="OrcaSkin" /> 的常量同源，不另写字面量）。</summary>
    private static readonly (string Id, string Label)[] Options =
    {
        (OrcaSkin.Plate, "板甲"),
        (OrcaSkin.Wedding, "婚纱"),
    };

    /// <summary>幂等地把面板挂到选人界面上（同名节点已存在就直接返回）。</summary>
    internal static void EnsureInjected(Node screen)
    {
        if (screen == null) return;
        if (screen.GetNodeOrNull(PanelNodeName) != null) return;   // 已注入过

        var panel = new PanelContainer { Name = PanelNodeName };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.KeepSize, 0);
        panel.OffsetLeft = -(PanelWidth + MarginRight);
        panel.OffsetTop = -(PanelHeight + MarginBottom);
        panel.OffsetRight = -MarginRight;
        panel.OffsetBottom = -MarginBottom;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);

        var title = new Label { Text = "银龙奥卡 · 皮肤" };
        title.AddThemeFontSizeOverride("font_size", TitleFontSize);
        box.AddChild(title);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var (id, label) in Options)
        {
            var button = new Button { Text = label };
            button.AddThemeFontSizeOverride("font_size", ButtonFontSize);
            button.CustomMinimumSize = new Vector2(100f, 32f);

            // 闭包捕获：id 是 foreach 变量，C# 5+ 每轮独立，安全
            button.Pressed += () => OnOptionPressed(id, label);
            row.AddChild(button);
        }
        box.AddChild(row);

        panel.AddChild(box);
        screen.AddChild(panel);

        OrcaLog.Info($"[Orca] 皮肤面板已注入选人界面（当前皮肤={OrcaSkin.Active}，来源={OrcaSkin.Source}）", 2);
    }

    /// <summary>按钮回调：写自有配置 + 记日志。失败时明确报错，不静默。</summary>
    private static void OnOptionPressed(string skinId, string label)
    {
        try
        {
            if (OrcaSkin.SaveActive(skinId))
            {
                OrcaLog.Info($"[Orca] 皮肤面板：已切换到【{label}】（写入 {OrcaSkin.Source}）", 2);
            }
            else
            {
                OrcaLog.Warn($"[Orca] 皮肤面板：切换到【{label}】失败（详见上面的写入失败原因）", 2);
            }
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 皮肤面板：切换【{label}】异常：{ex.Message}", 2);
        }
    }
}
