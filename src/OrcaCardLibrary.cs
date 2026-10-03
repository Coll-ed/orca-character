using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;

namespace OrcaCharacter;

/// <summary>
///     ★★ 在**百科（Card Library）里给奥卡注册单独的一行过滤器**。
///
///     <para>
///     做法照抄创意工坊的**观者模组**（`WatcherCardLibraryInjector`，反编译实据
///     `_verify\\decomp-watcher\\`）—— 核心思路是**用已有的过滤器节点 `Duplicate()` 复制一个**，
///     于是完全不需要改原版场景文件：
///     <code>
///     var template = AccessTools.Field(typeof(NCardLibrary), "_necrobinderFilter").GetValue(instance);
///     var filter = (NCardPoolFilter)template.Duplicate(6);
///     filter.Name = "OrcaPool";
///     parent.AddChild(filter); parent.MoveChild(filter, template.GetIndex() + 1);   // 插在模板后面
///     filter.GetNodeOrNull&lt;TextureRect&gt;("Image").Texture = 我们的角色图标;
///     filter.Loc = new LocString("characters", "ORCA.title");
///     poolFilters[filter] = c =&gt; c.Pool is OrcaCardPool;                              // 谓词
///     filter.Connect("Toggled", …UpdateCardPoolFilter…);                            // 接信号
///     cardPoolFilters[orca] = filter;                                              // 绑到角色
///     </code>
///     </para>
///
///     <para>
///     ⚠️ 百科的卡片总列表还**另有一道门槛**：`NCardLibraryGrid._Ready()` 遍历的是
///     <c>ModelDb.AllCards</c> ⇒ 光有这一行过滤器还不够，必须配合
///     <see cref="OrcaAllCardsPatch" />（在 `OrcaPoolRegistry.cs` 里）把卡补进总列表。
///     两道都过，卡才会出现在这一行下面。
///     </para>
/// </summary>
internal static class OrcaCardLibraryInjector
{
    private const string PoolNodeName = "OrcaPool";
    private const string IconPath = "res://images/ui/top_panel/character_icon_orca.png";

    internal static void Inject(NCardLibrary instance)
    {
        try
        {
            var node = instance as Node;
            if (node == null) return;
            if (node.FindChild(PoolNodeName, true, false) != null) return;      // 已注入过，别重复

            // ① 借「死灵师」那条角色过滤器当模板（它是最后一个角色行）
            var template = AccessTools.Field(typeof(NCardLibrary), "_necrobinderFilter")
                ?.GetValue(instance) as NCardPoolFilter;
            if (template == null)
            {
                OrcaLog.Warn("[Orca] 百科注入：拿不到 _necrobinderFilter 模板（版本可能变了）", 2);
                return;
            }

            var poolFilters = AccessTools.Field(typeof(NCardLibrary), "_poolFilters")
                ?.GetValue(instance) as Dictionary<NCardPoolFilter, Func<CardModel, bool>>;
            var cardPoolFilters = AccessTools.Field(typeof(NCardLibrary), "_cardPoolFilters")
                ?.GetValue(instance) as Dictionary<CharacterModel, NCardPoolFilter>;
            if (poolFilters == null || cardPoolFilters == null)
            {
                OrcaLog.Warn("[Orca] 百科注入：拿不到 _poolFilters / _cardPoolFilters", 2);
                return;
            }

            var updateMethod = AccessTools.Method(typeof(NCardLibrary), "UpdateCardPoolFilter");
            var lastHoveredField = AccessTools.Field(typeof(NCardLibrary), "_lastHoveredControl");

            // ② 图标（我们自己包里的顶栏角色小图标）+ 标题（characters 表的角色名）
            Texture2D? icon = null;
            try
            {
                icon = ResourceLoader.Load<Texture2D>(IconPath);
                if (icon == null) OrcaLog.Warn($"[Orca] 百科注入：图标取不到 {IconPath}（该行会没有图标）", 2);
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 百科注入：图标加载异常 {ex.Message}", 2);
            }

            var title = new LocString("characters", "ORCA.title");

            // ③ 复制节点 → 插到模板后面 → 注册谓词与信号
            var filter = CreatePoolFilter(template, PoolNodeName, icon, title);
            RegisterPoolFilter(instance, filter, updateMethod, lastHoveredField, poolFilters,
                c => c.Pool is OrcaCardPool);

            // ④ 绑到奥卡角色（百科的"角色 → 过滤器"映射）
            try
            {
                var orca = ModelDb.GetByIdOrNull<CharacterModel>(ModelDb.GetId(typeof(Orca)));
                if (orca != null) cardPoolFilters[orca] = filter;
                else OrcaLog.Warn("[Orca] 百科注入：取不到 Orca 角色模型（过滤器行仍可用，只是不绑角色）", 2);
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 百科注入：绑定角色失败（不影响过滤器行）：{ex.Message}", 2);
            }

            OrcaLog.Info("[Orca] 百科已注册独立过滤器行「银龙奥卡」（OrcaPool）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 百科过滤器注入失败（不影响游戏）：{ex.Message}", 2);
        }
    }

    /// <summary>复制模板节点成一行的新过滤器（照 Watcher 的 CreatePoolFilter）。</summary>
    private static NCardPoolFilter CreatePoolFilter(NCardPoolFilter template, string name, Texture2D? icon, LocString? loc)
    {
        var filter = (NCardPoolFilter)template.Duplicate(6);
        filter.Name = name;
        FixOwnerRecursive(filter, filter);

        // 图标节点上的 shader material 必须**独立**（否则和模板共享，改一个动两个）
        var image = filter.GetNodeOrNull<Control>("Image");
        if (image?.GetMaterial() is ShaderMaterial mat)
            image.Material = (Material)mat.Duplicate(false);

        var parent = template.GetParent();
        parent.AddChild(filter);                    // ⚠️ 别照抄反编译里的 (InternalMode)0 —— 那个枚举在本版 GodotSharp 里不存在
        parent.MoveChild(filter, template.GetIndex(false) + 1);

        if (filter.GetNodeOrNull<Control>("Image") is TextureRect tex && icon != null)
            tex.Texture = icon;

        if (loc != null) filter.Loc = loc;
        filter.Visible = true;
        return filter;
    }

    /// <summary>把新过滤器登记进百科的两本字典，并接上原版的信号（照 Watcher 的 RegisterPoolFilter）。</summary>
    private static void RegisterPoolFilter(
        NCardLibrary instance,
        NCardPoolFilter filter,
        MethodInfo? updateMethod,
        FieldInfo? lastHoveredField,
        Dictionary<NCardPoolFilter, Func<CardModel, bool>> poolFilters,
        Func<CardModel, bool> predicate)
    {
        poolFilters[filter] = predicate;

        if (updateMethod != null)
        {
            filter.Connect("Toggled",
                Callable.From<NCardPoolFilter>(f => updateMethod.Invoke(instance, new object[] { f })));
        }
        else
        {
            OrcaLog.Warn("[Orca] 百科注入：找不到 UpdateCardPoolFilter（点击该行可能不会刷新网格）", 2);
        }

        if (lastHoveredField != null)
        {
            filter.Connect(Control.SignalName.FocusEntered,
                Callable.From(() => lastHoveredField.SetValue(instance, filter)));
        }
    }

    /// <summary>复制出来的节点其子节点 Owner 还是模板 ⇒ 递归改回自己（否则 Godot 会报 owner 警告）。</summary>
    private static void FixOwnerRecursive(Node root, Node owner)
    {
        foreach (Node child in root.GetChildren(false))
        {
            child.Owner = owner;
            FixOwnerRecursive(child, owner);
        }
    }
}

/// <summary>
///     ★ 百科界面就绪后注入奥卡那一行。
///     （观者模组还额外挂了一个 `SceneTree.NodeAdded` 兜底给 Android 用；我们只做 PC，不需要。）
/// </summary>
[HarmonyPatch(typeof(NCardLibrary), "_Ready")]
internal static class OrcaCardLibraryPatch
{
    private static void Postfix(NCardLibrary __instance) => OrcaCardLibraryInjector.Inject(__instance);
}