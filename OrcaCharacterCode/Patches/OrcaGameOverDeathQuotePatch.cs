using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;

namespace OrcaCharacter;

/// <summary>
///     ★★ <b>结束画面替换（挂点 B）</b> —— 把 <c>NGameOverScreen</c> 那句死亡台词换成我们自己的。
///
///     <para><b>引擎事实（反编译实据，ilspycmd 8.2 + sts2.dll）</b>：
///     <list type="bullet">
///       <item><c>NGameOverScreen._deathQuote = GetNode&lt;MegaRichTextLabel&gt;("%DeathQuoteLabel")</c>
///         （<c>_Ready</c>，约 L469）；</item>
///       <item>写入点共**两处**：<c>InitializeBannerAndQuote</c> 里 <c>_deathQuote.Text = 随机 QUOTES</c>（约 L526）、
///         <c>AnimateInQuote</c> 里 <c>_deathQuote.Text = _encounterQuote</c>（约 L542）；
///         ——**没有第三处**，且 <c>AnimateInQuote</c> 排在后面。</item>
///     </list></para>
///
///     <para><b>为什么"挂结束画面"最终落在 <c>MegaRichTextLabel.Text</c> 的 setter 上</b>
///     （候选方案与取舍）：
///     <list type="number">
///       <item><b>补 <c>AnimateInQuote</c> 的 async 状态机 <c>&lt;AnimateInQuote&gt;d__45.MoveNext</c></b>
///         —— 时机最准，但 Roslyn 生成的 <c>d__NN</c> 编号**是编译器产物、随游戏更新会变**
///         （本机实测：<c>AnimateIn</c> 是 <c>d__68</c> 而 <c>AnimateInQuote</c> 是 <c>d__45</c>，编号不连续 ⇒
///         没有任何可推导规律，只能写死名字）⇒ 维护性差，**否掉**。</item>
///       <item><b>补 <c>InitializeBannerAndQuote</c></b> —— 太早：它是异步动画的**起点**，
///         我们在那里改的字会被 <c>AnimateInQuote</c> 覆盖回去 ⇒ **否掉**。</item>
///       <item><b>补 <c>MegaRichTextLabel.Text</c> 的 setter</b>（本方案）—— 它是两处写入的**唯一公共汇聚点**，
///         且是 Godot 贴图/文本的公开属性、不依赖任何编译器生成的名字 ⇒ 游戏更新不会静默失效。
///         代价：setter 是热路径 ⇒ 必须先做**最便宜**的判据（节点名 + 是否在 <c>NGameOverScreen</c> 树内），
///         命中才走状态判定与取文案 ✓。</item>
///     </list></para>
///
///     <para>⚠️ 只读判断 + 换文本；不碰引擎原文的生成、不改数值、不拦流程 ✓。</para>
/// </summary>
[HarmonyPatch(typeof(MegaRichTextLabel), "set_Text")]
internal static class OrcaGameOverDeathQuotePatch
{
    /// <summary>结束画面里那行死亡台词的节点名（引擎 <c>%DeathQuoteLabel</c> 的唯一名）。</summary>
    private const string DeathQuoteNodeName = "DeathQuoteLabel";

    /// <summary>
    ///     向上找宿主时最多走几层。具名常量，理由：Godot 场景层级不可预知，
    ///     但结束画面的标签到 <c>NGameOverScreen</c> 只有几层；给一个上限即避免
    ///     万一遇到自引用/异常 parent 链时空转（3 位以上数字 ⇒ 按纪律 #3 具名）。
    /// </summary>
    private const int MaxAncestorDepth = 12;

    /// <summary>本帧/本次写入是否来自结束画面（由前缀置位，后置据此决定要不要替换）。</summary>
    [ThreadStatic]
    private static MegaRichTextLabel? _labelInsideGameOverScreen;

    /// <summary>「首次命中」那条证据日志是否已经打过（只打一次，不刷屏）。</summary>
    private static bool _hitLogged;

    private static void Prefix(object __instance)
    {
        try
        {
            _labelInsideGameOverScreen = null;

            if (__instance is not MegaRichTextLabel label) return;

            // ★ 最便宜的判据放最前：名字不对就直接退出（绝大多数 setter 调用在这里被挡掉）
            if (label.Name != DeathQuoteNodeName) return;

            if (!IsInsideGameOverScreen(label)) return;

            // 命中即标记：后置据此决定要不要替换（此处不取文案 —— 后置里读到的才是引擎写完之后的值）
            _labelInsideGameOverScreen = label;
        }
        catch (Exception ex)
        {
            _labelInsideGameOverScreen = null;
            OrcaLog.Warn($"[Orca] 死亡台词：结束画面标签判定出错（保留引擎原文）：{ex.Message}", 2);
        }
    }

    private static void Postfix()
    {
        var label = _labelInsideGameOverScreen;
        _labelInsideGameOverScreen = null;
        if (label == null) return;

        try
        {
            // ★ 引擎在**胜利**分支把文案**清空**（InitializeBannerAndQuote 里 `= string.Empty`）——
            //   清空动作一律放行：绝不拿自定义文案去填一个"本该没有台词"的场合
            //   （这也顺手挡住了"上一局的记录被用在本局胜利画面"这类误替换）。
            if (string.IsNullOrWhiteSpace(label.Text)) return;

            var text = OrcaDeathQuotes.TryBuild(label.GetInstanceId());
            if (string.IsNullOrWhiteSpace(text))
            {
                // 不是我们认得的死亡情况 / 配置为空 ⇒ 引擎原文原样留着（TryBuild 内部已记日志）
                return;
            }

            // 用与引擎**同一条**路（MegaRichTextLabel.Text ⇒ SetTextAutoSize ⇒ 自动缩放字号），
            // 而不是直接写 base.Text —— 否则长文案不会重算字号（见该类的方法注释）。
            label.Text = text;
            OrcaLog.Info($"[Orca] 死亡台词：结束画面文案已替换为「{text}」", 2);

            // ★ 一次性证据日志：本补丁的入口判据是**节点名**，若哪天官方改了场景里的名字，
            //   它会静默地永不命中（最难查的一类故障）。所以第一次真正替换成功时，
            //   把**实际命中的节点路径**打进日志 —— 这条日志一旦出现过，
    //   就证明"名字判据 + 祖先判定"在当前引擎版本上确实成立 ✓。
            if (!_hitLogged)
            {
                _hitLogged = true;
                OrcaLog.Info($"[Orca] 死亡台词：首次命中 —— 节点路径 {label.GetPath()}", 2);
            }
        }
        catch (Exception ex)
        {
            // 边界显式：替换失败就保留引擎原文，绝不把结束画面搞崩。
            OrcaLog.Warn($"[Orca] 死亡台词：替换结束画面文案出错（保留引擎原文）：{ex.Message}", 2);
        }
    }

    /// <summary>这个标签是不是挂在 <c>NGameOverScreen</c> 底下（向上找宿主节点）。</summary>
    private static bool IsInsideGameOverScreen(Node node)
    {
        var current = node.GetParent();
        for (var depth = 0; current != null && depth < MaxAncestorDepth; depth++)
        {
            if (current is NGameOverScreen) return true;
            current = current.GetParent();
        }

        return false;
    }
}

/// <summary>
///     ★ 每次结束画面**新建**时清空死亡情况记录（见 <see cref="OrcaDeathQuotes.Reset" />），
///     免得上一局的"同归于尽"泄漏到本局（那会让非焚烧死亡也套上这条文案）。
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), "_Ready")]
internal static class OrcaGameOverScreenResetPatch
{
    private static void Postfix()
    {
        try
        {
            OrcaDeathQuotes.Reset();
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 死亡台词：清空记录出错（本次不替换文案）：{ex.Message}", 2);
        }
    }
}

