using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace OrcaCharacter;

/// <summary>
///     ★★★ **本模组自带的皮肤切换面板**（用户口径 2026-10-05：「下一步就是重新做皮肤切换管理器了」）。
///
///     <para><b>为什么要自己做</b>：切换入口原先完全依赖**外部皮肤包**（<c>奥卡皮肤-Orca\CharacterSkinManager</c>，
///     第三方通用管理器）。用户的设计理念一直是「整合到奥卡角色模组本体」——
///     本模组已经有了全部两套骨架（路径见 <see cref="OrcaSkin" /> 的四个访问器），
///     应用侧（战斗 / 商店 / 篝火 / 卡框 / 能量球 / 图标 / 高亮色 / 气泡色）也早已齐全，
///     唯独"点哪儿切"在别人家里 ⇒ 皮肤包没装、或它改了行为，本角色就没法切皮肤。</para>
///
///     <para><b>挂点照外部管理器的实据</b>（反编译 <c>CharacterSkinManager.dll</c>，它在本机实测可用）：
///     <code>
///     [HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]          → 注入面板
///     [HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")] → 通知选中角色 + 刷新面板 + 换选人界面大模型
///     </code></para>
///
///     <para><b>位置照它的实测坐标</b>：锚右下角、保持尺寸，偏移 <c>L-560 T-300 R-270 B-100</c>
///     （它那块面板用 <c>L-560 T-304 R-214 B-76</c>，在选人界面里不挡游戏 UI）。</para>
///
///     <para>⚠️ 面板只在**选中奥卡**时显示（别的角色不该看见奥卡的皮肤开关）。</para>
/// </summary>
internal static class OrcaSkinPanel
{
    /// <summary>面板节点名（<c>EnsureInjected</c> 用它判重，避免重复注入）。</summary>
    private const string PanelNodeName = "OrcaSkinPanel";

    private const string Title = "皮肤";

    // ── 面板几何（具名常量：来源＝外部管理器的实测可用坐标，见类注释）──────────
    private const int PanelOffsetLeft = -560;
    private const int PanelOffsetTop = -300;
    private const int PanelOffsetRight = -270;
    private const int PanelOffsetBottom = -100;

    /// <summary>预览图尺寸（像素）。</summary>
    private const int PreviewWidth = 92;
    private const int PreviewHeight = 128;

    /// <summary>面板里那两个按钮的引用（<c>Refresh</c> 更新按下态用）。</summary>
    private static Button? _plateButton;
    private static Button? _weddingButton;
    private static TextureRect? _preview;

    /// <summary>程序化改按钮状态时的重入闸门（改 ButtonPressed 会发 Toggled，不该被当作用户点击）。</summary>
    private static bool _updating;

    /// <summary>当前面板属于哪个屏幕（点按钮时要回头刷新它）。</summary>
    private static NCharacterSelectScreen? _screen;

    /// <summary>注入面板（幂等：已经注入过就直接返回）。</summary>
    internal static void EnsureInjected(NCharacterSelectScreen screen)
    {
        _screen = screen;

        if (screen.GetNodeOrNull<PanelContainer>(PanelNodeName) != null) return;

        try
        {
            var panel = new PanelContainer { Name = PanelNodeName, Visible = false };

            // 锚右下角 + 保持尺寸（照外部管理器的实测坐标）
            panel.SetAnchorsAndOffsetsPreset(
                Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.KeepSize, 0);
            panel.OffsetLeft = PanelOffsetLeft;
            panel.OffsetTop = PanelOffsetTop;
            panel.OffsetRight = PanelOffsetRight;
            panel.OffsetBottom = PanelOffsetBottom;

            var column = new VBoxContainer();
            panel.AddChild(column);

            column.AddChild(new Label { Text = Title });

            var row = new HBoxContainer();
            column.AddChild(row);

            _preview = new TextureRect
            {
                CustomMinimumSize = new Vector2(PreviewWidth, PreviewHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            row.AddChild(_preview);

            var buttons = new VBoxContainer();
            row.AddChild(buttons);

            // 两个按钮同一 ButtonGroup ⇒ 互斥；选中态由 Refresh 设，用户点击才走 Pressed
            var group = new ButtonGroup();
            _plateButton = MakeSkinButton("板甲", OrcaSkin.Plate, group);
            _weddingButton = MakeSkinButton("婚纱", OrcaSkin.Wedding, group);
            buttons.AddChild(_plateButton);
            buttons.AddChild(_weddingButton);

            screen.AddChild(panel);
            OrcaLog.Info("[Orca] 皮肤面板已注入选人界面（选中奥卡时显示）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 皮肤面板注入失败（选人界面照常用，只是没有切换入口）：{ex.Message}", 2);
        }
    }

    private static Button MakeSkinButton(string text, string skin, ButtonGroup group)
    {
        var button = new Button { Text = text, ToggleMode = true, ButtonGroup = group };
        button.Pressed += () => OnSkinPressed(skin);
        return button;
    }

    /// <summary>按当前选中的角色刷新面板：**只有奥卡**才显示；并同步预览图与按钮按下态。</summary>
    internal static void Refresh(NCharacterSelectScreen screen, CharacterModel? character)
    {
        var panel = screen.GetNodeOrNull<PanelContainer>(PanelNodeName);
        if (panel == null) return;

        var isOrca = character is Orca;
        panel.Visible = isOrca;
        if (!isOrca) return;

        SyncState();
    }

    /// <summary>
    ///     只同步"当前选的是哪套"（预览图 + 按钮按下态），不动可见性。
    ///
    ///     <para>给选人界面那个 1 秒轮询用：玩家也可能在**外部管理器**的面板里切皮肤，
    ///     那时本面板的高亮会滞后 ⇒ 轮询里一起同步。</para>
    /// </summary>
    internal static void SyncState()
    {
        _updating = true;
        try
        {
            OrcaSkin.Refresh();                       // 立刻重读（不吃 1.5s 节流）
            var wedding = OrcaSkin.IsWedding;

            if (_preview != null)
            {
                var path = wedding
                    ? OrcaCharacterIcon.WeddingSelectIllustPath
                    : OrcaCharacterIcon.PlateSelectIllustPath;
                _preview.Texture = ResourceLoader.Load<Texture2D>(path);
            }

            if (_plateButton != null) _plateButton.ButtonPressed = !wedding;
            if (_weddingButton != null) _weddingButton.ButtonPressed = wedding;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 皮肤面板刷新出错：{ex.Message}", 2);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>用户点了某个皮肤：双写选择 → 就地刷新已在屏上的角色图 → 换选人界面的大模型。</summary>
    private static void OnSkinPressed(string skin)
    {
        if (_updating) return;

        try
        {
            OrcaSkin.SetSkin(skin);                    // 内含 RefreshLiveIcons（小图 + 立绘就地刷新）

            var screen = _screen;
            if (screen != null) OrcaSceneSkin.ApplyToCharacterSelect(screen);

            SyncState();                               // 预览图 + 按钮高亮跟上
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 切换皮肤出错：{ex.Message}", 2);
        }
    }
}

/// <summary>
///     选中角色时刷新皮肤面板 + 把选人界面那块**大人物模型**换成当前皮肤。
///
///     <para>挂点与签名照外部管理器（<c>CharacterSkinCharacterSelectSwitchPatches</c>）的实据：
///     <c>NCharacterSelectScreen.SelectCharacter(NCharacterSelectButton, CharacterModel)</c> 的 postfix，
///     按名字绑定 <c>characterModel</c>。</para>
///
///     <para>⚠️ 大模型是 <c>SelectCharacter</c> **内部**才挂到 <c>_bgContainer</c> 上的，
///     而骨架是异步加载的 ⇒ 这里**立刻试一次 + 下一帧再试一次**（未就绪时 <see cref="OrcaSceneSkin" />
///     会记日志跳过，不会 fail-fast）。</para>
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class OrcaSkinPanelSelectPatch
{
    private static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        try
        {
            OrcaSkinPanel.EnsureInjected(__instance);
            OrcaSkinPanel.Refresh(__instance, characterModel);

            if (characterModel is not Orca) return;

            OrcaSceneSkin.ApplyToCharacterSelect(__instance);
            Callable.From(() => OrcaSceneSkin.ApplyToCharacterSelect(__instance)).CallDeferred();
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 选人界面套皮肤失败（不影响选人）：{ex.Message}", 2);
        }
    }
}
