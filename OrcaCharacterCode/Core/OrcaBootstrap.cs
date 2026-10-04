using System.Reflection;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace OrcaCharacter;

/// <summary>
///     模组入口。配方参照工坊 Watcher v0.9.28：
///     ① 逐类挂 Harmony 补丁（每个类独立 try/catch，统计成功/失败）；
///     ② 用 <c>ModelDb.get_AllCharacters</c> 的 postfix 把本角色补进"所有角色"列表
///        （模型类只要定义在模组程序集里，游戏会自动收进 ModelDb，靠 <c>ModelDb.GetId</c> 反查即可）。
/// </summary>
[ModInitializer("Init")]
public static class OrcaBootstrap
{
    public const string HarmonyId = "colled.sts2.orca_character";

    /// <summary>
    ///     ★ **构建标记**（二分实验专用，见 `docs/卡死二分方案.md`）。
    ///
    ///     <para>每次二分实验**改这一个字符串**，启动时会打进日志 ⇒ 用来确认
    ///     "用户实际跑的确实是这一版"。为什么需要：交接文档 `05-文档\⑤` 的教训表第 6 条记着
    ///     「我曾同时存在 `src` / `src-s2` / `src-fresh`，并**从错误的树编译了一整轮**」——
    ///     卡死这种问题本来就无法从日志判断，再叠上"跑错版本"就彻底无法归因。</para>
    /// </summary>
    public const string BuildTag = "handwritten+merchant-skin+skinpanel";

    private static bool _initialized;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        // ★★ 最先注册模组配置（用户口径 2026-09-23："加一个日志是否开启的功能在设置里面"）——
        //    放在最前面，这样后面的日志就已经受开关控制了。
        //    实据（BaseLib wiki + API dump）：ModConfigRegistry.Register(modId, config)，
        //    之后配置页自动出现在游戏的「模组配置」里（主菜单 + 设置），并自动读写落盘。
        //    ⚠️ 整段 try/catch：配置注册失败绝不能拖垮整个 mod。
        try
        {
            ModConfigRegistry.Register("OrcaCharacter", new OrcaConfig());
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 模组配置注册失败（日志开关不可用，日志按默认全开）：{ex.Message}", 2);
        }

        // ★ 构建标记：二分实验时靠它确认"用户实际跑的是哪一版"（见 BuildTag 的注释）
        OrcaLog.Info($"[Orca] 构建标记 = {BuildTag}", 2);

        // 皮肤：切换入口＝**本模组自带的选人界面皮肤面板**（OrcaSkinPanel，2026-10-05 起整合进本模组，
        // 不再依赖外部皮肤包的管理器）。这里只做初始化（读选择）+ 登记卡框材质等自带素材。
        var modDir = string.Empty;
        try { modDir = Path.GetDirectoryName(typeof(OrcaBootstrap).Assembly.Location) ?? string.Empty; }
        catch { /* 取不到就用空目录，兜底配置读不到、走默认板甲 */ }
        OrcaSkin.Init(modDir);
        RegisterCustomAssets();

        // ★ 把奥卡的时间线 epoch 补进原版的静态表
        //   （EpochModel 的静态构造是**源码生成**的 57 项，不补的话 GetId<Orca5Epoch>() 会抛；
        //    而原版那两个 epoch 检查又对奥卡必抛 —— 见 OrcaEpochs.cs 的长注释）
        OrcaEpochRegistry.Register();

        // ★ A1 诊断：把卡池/角色的注册状态打进日志（只读，不改逻辑 ✓）
        // ★ 修复 D（2026-10-03 · 二分第 2 步）：把注册自检**关掉** ✓ 改动最小（只注释这一行）
        //   缘由：二分已确认"官方 dll 正常、A4版 卡死"⇒ 嫌疑落在两处：A4 掉落入牌组、注册自检。
        //   而 A4 那段只在 BOSS 胜利/欧洛巴斯之触时才跑 ⇒ **不在"打第一张牌"的路径上** ✓；
        //   自检却在**启动期**就跑，且日志实证它会吃到
        //     KeyNotFoundException: The given key 'CARD_POOL.ORCA_CARD_POOL' was not present in the dictionary ✗
        //   ⇒ 极可能就是它触发/加剧了那次半初始化（旧会话原文：「池子没挂进 ModelDb 的全局列表」）
        //   ⇒ 先关掉验一次；若不再卡死，则确证是"查询该键"这个动作本身有问题 ✓
        //   （OrcaRegSelfCheck.cs 文件保留、不删 ✓ 一行即可恢复 ✓）
        // OrcaRegSelfCheck.Run();

        var harmony = new Harmony(HarmonyId);
        int ok = 0, fail = 0;

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0) continue;
            try
            {
                new PatchClassProcessor(harmony, type).Patch();
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Log.Error($"[Orca] Harmony patch failed for {type.Name}: {ex.InnerException?.Message ?? ex.Message}", 2);
            }
        }

        OrcaLog.Info($"[Orca] Harmony 补丁: {ok} 成功 / {fail} 失败", 2);
        OrcaLog.Info($"[Orca] 角色 id = {ModelDb.GetId(typeof(Orca))}", 2);
        OrcaLog.Info("[Orca] 银龙奥卡初始化完成。", 2);
    }

    /// <summary>
    ///     把我们自带的材质登记进游戏的**资源预载缓存**（<c>PreloadManager.Cache</c>）：
    ///     卡框走 <c>CardPoolModel.FrameMaterial → AssetCache.GetMaterial(path)</c>、能量球换色我们自己挂；
    ///     缓存里没有的素材游戏会照常按需加载、但会刷 <c>Asset not cached:</c> 警告，主动登记即干净。
    ///     （只用游戏自带的公开 API：<c>ContainsKey</c> / <c>SetAsset</c>。）
    /// </summary>
    private static void RegisterCustomAssets()
    {
        foreach (var path in OrcaSkin.CustomAssetPaths)
        {
            try
            {
                var cache = PreloadManager.Cache;
                if (cache.ContainsKey(path))
                {
                    OrcaLog.Info($"[Orca] 材质已在预载缓存：{path}", 2);
                    continue;
                }

                var res = ResourceLoader.Load<Resource>(path);
                if (res == null)
                {
                    OrcaLog.Warn($"[Orca] 素材加载失败（相关配色会退回默认）：{path}", 2);
                    continue;
                }

                cache.SetAsset(path, res);
                OrcaLog.Info($"[Orca] 材质已登记进预载缓存：{path}", 2);
            }
            catch (Exception ex)
            {
                OrcaLog.Warn($"[Orca] 登记材质出错（{path}）：{ex.Message}", 2);
            }
        }

        // ★ 说话气泡（NSpeechBubbleVfx）用的贴图 —— 原版 VFX 类自带 `AssetPaths` 公开属性，
        //   但那是"随战斗资源按需收集"的路径；我们开战前主动登记一次，
        //   保证奥卡第一次冒台词气泡时贴图已经在缓存里（否则会刷 `Asset not cached:` 甚至空白气泡）。
        try
        {
            var bubbleCache = PreloadManager.Cache;
            var n = 0;
            foreach (var path in NSpeechBubbleVfx.AssetPaths)
            {
                if (string.IsNullOrEmpty(path) || bubbleCache.ContainsKey(path)) continue;
                var res = ResourceLoader.Load<Resource>(path);
                if (res == null) continue;
                bubbleCache.SetAsset(path, res);
                n++;
            }

            OrcaLog.Info($"[Orca] 说话气泡素材已登记 {n} 项（气泡颜色：板甲灰黑 / 婚礼红，见 Orca.SpeechBubbleColor）", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 登记说话气泡素材出错（不影响战斗）：{ex.Message}", 2);
        }
    }
}

/// <summary>把银龙奥卡补进 ModelDb 的角色列表（否则选人界面看不到）。</summary>
[HarmonyPatch(typeof(ModelDb), "get_AllCharacters")]
internal static class ModelDbAllCharactersPatch
{
    private static void Postfix(ref IEnumerable<CharacterModel> __result)
    {
        try
        {
            var orca = ModelDb.GetByIdOrNull<CharacterModel>(ModelDb.GetId(typeof(Orca)));
            if (orca == null)
            {
                OrcaLog.Warn("[Orca] ModelDb 里找不到 Orca，选人界面不会出现", 2);
                return;
            }

            __result = __result.Concat(new[] { orca }).Distinct().ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"[Orca] 注册角色列表失败: {ex.Message}", 2);
        }
    }
}