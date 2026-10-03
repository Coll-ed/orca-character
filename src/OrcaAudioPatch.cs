using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;

namespace OrcaCharacter;

/// <summary>
///     ★★ 让**我们自己的音频文件**能被游戏播出来 —— 手写一份 BaseLib `ModAudio` 干的事。
///
///     <para>
///     BaseLib 的 wiki（`docs/utilities/mod-audio.html`）原话：
///     <i>"BaseLib will also play sounds when a resource path is provided to the basegame FMOD event player."</i>
///     <code>
///     public override string CharacterTransitionSfx =&gt; "res://Mod/audio/sound.ogg";
///     public override string CharacterSelectSfx  =&gt; "res://Mod/audio/sound.ogg";
///     </code>
///     ⇒ 即：**角色音效字符串可以直接给 `res://` 资源路径**。
///     但那是 BaseLib 的补丁；我们不用第三方库 ⇒ **自己实现同一件事**。
///     </para>
///
///     <para>
///     实现在<b>播放那一层</b>拦截（反编译 `NAudioManager` / `SfxCmd` 实据）：
///     <code>
///     SfxCmd.PlayDeath(Player p)  →  NAudioManager.Instance.PlayOneShot(p.Character.DeathSfx)
///     SfxCmd.Play(sfx, volume)    →  NAudioManager.Instance.PlayOneShot(sfx, volume)
///     </code>
///     ⇒ 只认 `res://` 前缀，其余（`event:/…`）**原样放行走 FMOD**。
///     </para>
///
///     <para>
///     ★ 为什么用 Godot 的 `AudioStreamPlayer` 播而不是 FMOD：**它能设 `PitchScale`** ——
///     这正是用户要的"**把音调拉高点**"（见 <see cref="OrcaAudio.Pitch" />）。
///     反编译里游戏自己也这么干：`NDebugAudioManager.Play` 就是
///     `streamPlayer.PitchScale = GetRandomPitchScale(variance)`。
///     </para>
/// </summary>
[HarmonyPatch(typeof(NAudioManager), "PlayOneShot", new[] { typeof(string), typeof(float) })]
internal static class OrcaResAudioPatch
{
    private static bool Prefix(string path, float volume)
    {
        if (string.IsNullOrEmpty(path)) return true;

        // ① 我们自己的音频文件（res://）⇒ 用 Godot 播放器播（可设 PitchScale）
        if (path.StartsWith("res://", StringComparison.Ordinal))
        {
            try
            {
                OrcaAudio.Play(path, volume);
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 播自定义音效失败（{path}）：{ex.Message}", 2);
            }
            return false;
        }

        // ② 格挡音效（用户 2026-09-17："防御音效变**低沉**一点"）。
        //    原版是**固定事件** `event:/sfx/block_gain`（反编译 CreatureCmd.GainBlock 里硬编码，
        //    所有角色共用、连敌人格挡也走它）⇒ 换成我们变低沉的那版（`orca_block.tres`，×0.85 烘在文件里）。
        //
        //    ★★ 2026-09-22 用户反馈：「**银龙的声音导致了其他角色的技能牌也出现了变调**」
        //    —— 根因就是这里**不判断当前角色**，全局把所有人的格挡音都换成了银龙的。
        //    用户口径：「**单独给银龙奥卡使用**」⇒ 加角色判断，不是奥卡就原版原样放行。
        //    （同一条反馈里的"变调"还有第二个来源：播放时又乘了 ×1.35，
        //      见 OrcaAudio.PitchFor —— 两个都修了。）
        if (path == OrcaAudio.BlockEvent)
        {
            if (!OrcaAudio.LocalPlayerIsOrca()) return true;   // ★ 不是奥卡 ⇒ 原版音效照旧走 FMOD

            try
            {
                OrcaAudio.Play(OrcaAudio.BlockRes, volume);
                return false;
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 替换格挡音效失败（退化为原版）：{ex.Message}", 2);
                return true;
            }
        }

        return true;                           // 其余原版 FMOD 事件 ⇒ 照旧
    }
}

/// <summary>
///     ★ 带**变调**的音频播放（`res://` 资源专用）。
///
///     <para>
///     用户口径 2026-09-16："套用原版战士的吧，你把**音调拉高点**" ——
///     奥卡是银龙少女，音效要比铁甲战士更清亮 ⇒ 用 `PitchScale = 1.35`（约高 5 个半音）。
///     想调就在 <see cref="Pitch" /> 改一个数。
///     </para>
/// </summary>
internal static class OrcaAudio
{
    /// <summary>播放音调倍率（1.0 = 原速；1.35 ≈ 高 5 个半音）。</summary>
    internal const float Pitch = 1.35f;

    /// <summary>
    ///     ★ **哪些资源走原速**（不加运行时变调）—— `res://` 播放时用它挑 PitchScale。
    ///
    ///     <list type="number">
    ///       <item><b>主界面选择音效</b>（板甲 / 婚纱）：用户自备的成品，已经是想要的调子
    ///         （用户口径 2026-09-17：「关于主界面的音效」）⇒ 再套 ×1.35 就变味了。</item>
    ///       <item><b>格挡音</b>：用户口径 2026-09-22「补丁改成播放我们的本地版本，**不要变调**」。
    ///         实据 —— `_verify\\make_ironclad_sfx.py` 里 `orca_block.tres` / `orca_block_alt.tres`
    ///         是拿铁甲战士原声按 **×0.85 降调后烘进文件**的（用户 2026-09-17「防御音效变低沉一点」）。
    ///         旧代码播放时又乘了 ×1.35 ⇒ 净 **×1.1475，比原版还高**，正好把"低沉"抵消掉还倒过来。
    ///         ⇒ 这两个资源必须走原速，降调才真正生效。</item>
    ///     </list>
    /// </summary>
    internal static float PitchFor(string resPath)
    {
        if (resPath.EndsWith("orca_select_plate.tres", StringComparison.Ordinal)
            || resPath.EndsWith("orca_select_wedding.tres", StringComparison.Ordinal)
            || resPath.EndsWith("orca_block.tres", StringComparison.Ordinal)
            || resPath.EndsWith("orca_block_alt.tres", StringComparison.Ordinal))
        {
            return 1.0f;
        }

        return Pitch;
    }

    /// <summary>
    ///     ★★ **现在玩的是不是奥卡** —— 格挡音替换的判断依据（见 <see cref="OrcaResAudioPatch" />）。
    ///
    ///     <para><b>为什么必须在运行时现查，而不是缓存一个 bool</b>：
    ///     `NAudioManager.PlayOneShot` 是**静态上下文**，拿不到 `Player`；反编译实据
    ///     （`CreatureCmd.GainBlock`）显示格挡音是**硬编码**的
    ///     <c>SfxCmd.Play("event:/sfx/block_gain")</c> —— 所有角色、连敌人都共用同一个事件；
    ///     而 `CharacterModel` 的音频槽位只有 select / attack / cast / powerup / death / transition
    ///     六个，**没有"角色格挡音"这一项**，所以没有资源槽位可绑。
    ///     ⇒ 缓存 bool 会在"同一进程里打完奥卡再开别的角色"时**过期**（标志还留着 true）⇒ 又泄漏。
    ///     每次现查就没有过期问题。</para>
    ///
    ///     <para>取值路径（全部有反编译实据）：`RunManager.Instance.DebugOnlyGetState()`
    ///     —— 它就是一个 <c>return State;</c> 的纯取值（只是因为 `RunManager.State` 是 private，
    ///     公开出来的那个名字带了 Debug）→ `RunState.Players`（public，`IReadOnlyList&lt;Player&gt;`）
    ///     → `LocalContext.GetMe(IEnumerable&lt;Player&gt;)` 拿本地玩家 → 看 `Character`。</para>
    ///
    ///     <para>⚠️ 任何一步失败都返回 <c>false</c> ⇒ **退回原版音效**。
    ///     宁可不换，也不误伤别的角色。</para>
    /// </summary>
    internal static bool LocalPlayerIsOrca()
    {
        try
        {
            var players = RunManager.Instance?.DebugOnlyGetState()?.Players;
            return players != null && LocalContext.GetMe(players)?.Character is Orca;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     ★ 奥卡的**选人音效**：直接用**铁甲战士的 FMOD 事件**
    ///     （用户 2026-09-17："主界面的切人音效！那个换成战士的，变个调子"）。
    ///     原版 <c>CharacterModel</c> 的默认实现就是 <c>$"event:/sfx/characters/{id}/{id}_select"</c>。
    /// </summary>
    internal const string SelectEvent = "event:/sfx/characters/ironclad/ironclad_select";

    /// <summary>
    ///     选人音效的变调值 —— 早期方案遗留常量（现在选人音走自备 `.tres`，保留以防回退）。
    /// </summary>
    internal const float SelectPitch = 1.3f;

    /// <summary>原版格挡事件的路径（固定事件，所有角色共用）。</summary>
    internal const string BlockEvent = "event:/sfx/block_gain";

    /// <summary>我们自己的格挡音（铁甲战士原声 ×0.85 变低沉）。</summary>
    internal const string BlockRes = "res://OrcaCharacter/audio/orca_block.tres";

    private static Node? _host;

    internal static void Play(string resPath, float volume)
    {
        var stream = ResourceLoader.Load<AudioStream>(resPath);
        if (stream == null)
        {
            OrcaLog.Warn($"[Orca] 音效资源加载不到：{resPath}", 2);
            return;
        }

        var host = GetHost();
        if (host == null) return;

        var player = new AudioStreamPlayer
        {
            Stream = stream,
            PitchScale = PitchFor(resPath),                       // ★ 拉高音调（主界面成品音效走原速）
            VolumeDb = volume >= 1f ? 0f : Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)),
            Bus = "SFX",
        };

        host.AddChild(player);
        player.Finished += player.QueueFree;                      // 播完自己清理，不留节点
        player.Play();
    }

    /// <summary>拿一个常驻节点当父级（播放器播完即 queue_free）。</summary>
    private static Node? GetHost()
    {
        if (_host != null && GodotObject.IsInstanceValid(_host)) return _host;

        try
        {
            _host = NAudioManager.Instance;
            if (_host != null) return _host;
        }
        catch { /* 单例还没建好时走下面的兜底 */ }

        try
        {
            _host = (Engine.GetMainLoop() as SceneTree)?.Root;
        }
        catch { }

        if (_host == null) OrcaLog.Warn("[Orca] 找不到可挂音频播放器的节点", 2);
        return _host;
    }
}