using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace OrcaCharacter;

/// <summary>
///     ★ 婚纱「黑纱」透明度的**运行时**调节（不碰素材、不重新打包）。
///
///     <para><b>为什么能这么做</b>：Spine 每个**槽位(slot)**都带颜色，绘制时按
///     <c>顶点色 × 贴图</c> 相乘。把槽位色设成 <c>(k, k, k, k)</c>，就等于把该件的
///     **RGB 与 α 同乘 k** —— 与"改贴图时的预乘同比缩放"数学上完全等价（只缩 α 会破坏
///     预乘不变量、边缘发白，这一点美术侧也踩过）。</para>
///
///     <para><b>怎么认出那块纱</b>：不写死槽位名（.skel 里槽位名与图片件名不一定一致），
///     而是遍历 <c>skeleton.get_slots()</c>，用 <c>slot.get_attachment().get_name()</c>
///     与目标件名比对 —— 件名就是 .atlas 里的 region 名，是这个仓库里的既有事实。</para>
///
///     <para>原生方法名来自 <c>libspine_godot</c> 二进制里的实据：
///     <c>SpineSkeleton.get_slots()</c> / <c>SpineSlot.get_attachment()</c> /
///     <c>SpineAttachment.get_name()</c> / <c>SpineSlot.set_color(Color)</c> /
///     <c>SpineSprite.get_skeleton()</c>（最后一条也由引擎绑定 <c>MegaSprite.GetSkeleton</c> 印证）。</para>
/// </summary>
internal static class OrcaVeilTuner
{
    /// <summary>
    ///     要调透明度的目标名。
    ///
    ///     <para>★ 实测（诊断输出，日志实据）：这套骨架里**槽位名与 .atlas 的 region 名同名**
    ///     （如 <c>WD_Dress_Middle</c> / <c>WD_Dress_Back</c> 都原样出现），而 <c>get_attachment()</c>
    ///     在"刚设完骨架数据"这一刻**全是空**（皮肤件还没挂上）⇒ 只认 attachment 名会一个都匹配不到。
    ///     所以按**槽位名**匹配，attachment 名作为兜底（同一份表两处都认，不复制第二份）。</para>
    /// </summary>
    private static readonly string[] TargetNames =
    {
        "WD_Skirt_inner",     // 战斗
        "WD_Dress_Middle",    // 选人/主界面
        "B-Voile",            // 商店
    };

    /// <summary>Godot 的用户数据虚拟路径前缀（引擎协议常量，只能照写）。</summary>
    private const string UserScheme = "user://";   // hardcode-ok: Godot 引擎定义的虚拟路径前缀，字符串由引擎规定，只能照写

    /// <summary>倍数落盘文件名（放在用户目录 ⇒ 随模组持久化，不进仓库、不进 pck）。</summary>
    private const string StoreFileName = "orca_veil.txt";

    /// <summary>
    ///     可调范围与步长。
    ///     <para>★ 下限 = 0（用户口径："最低可以到 0%"）⇒ 允许把纱调到完全不可见。
    ///     初版设的是 0.20（怕用户误以为贴身的件丢了），按用户口径放开。</para>
    /// </summary>
    private const float MinMultiplier = 0f;
    private const float MaxMultiplier = 1.00f;
    private const float StepMultiplier = 0.05f;
    private const float DefaultMultiplier = 1.00f;

    /// <summary>百分比显示用的比例（×100 ⇒ 显示成整数百分比）。</summary>
    private const float PercentScale = 100f;

    /// <summary>诊断输出里最多列多少条「槽位=件名」（有界：不让一次日志刷爆）。</summary>
    private const int DumpLimit = 40;

    /// <summary>「一个都没匹配上」的诊断是否已经打过（只打一次，避免每次调值都刷屏）。</summary>
    private static bool _dumpedSlots;

    private static float _multiplier = DefaultMultiplier;
    private static bool _loaded;

    /// <summary>已经被套过皮的 SpineSprite（改倍数时要立刻重刷在场的所有小人）。</summary>
    private static readonly List<GodotObject> Tracked = new();

    /// <summary>当前倍数（0.20 ~ 1.00）。</summary>
    internal static float Multiplier
    {
        get { EnsureLoaded(); return _multiplier; }
    }

    /// <summary>百分比显示，如 <c>85%</c>。</summary>
    internal static string Display => string.Format(CultureInfo.InvariantCulture, "{0:0}%",
                                                    _multiplier * PercentScale);

    private static string StorePath => ProjectSettings.GlobalizePath(UserScheme + StoreFileName);

    /// <summary>读盘一次（取不到就用默认值，并把原因记进日志，不静默）。</summary>
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var path = StorePath;
            if (!File.Exists(path))
            {
                OrcaLog.Info($"[Orca] 黑纱透明度：无存档，用默认 {Display}（面板上可调）", 2);
                return;
            }

            var text = File.ReadAllText(path).Trim();
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                OrcaLog.Warn($"[Orca] 黑纱透明度存档内容无法解析（{text}）⇒ 用默认 {Display}", 2);
                return;
            }

            _multiplier = Clamp(v);
            OrcaLog.Info($"[Orca] 黑纱透明度：读档 {Display}", 2);
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 黑纱透明度存档读取失败（用默认 {Display}）：{ex.Message}", 2);
        }
    }

    private static float Clamp(float v) => Math.Clamp(v, MinMultiplier, MaxMultiplier);

    /// <summary>按 <paramref name="steps" /> 步调整（正=更实、负=更透），落盘并立刻重刷在场小人。</summary>
    internal static float Step(int steps)
    {
        EnsureLoaded();
        _multiplier = Clamp(_multiplier + steps * StepMultiplier);
        Save();
        var n = ReapplyAll();
        OrcaLog.Info($"[Orca] 黑纱透明度 → {Display}（本次重刷 {n} 个 SpineSprite）", 2);
        return _multiplier;
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(StorePath, _multiplier.ToString("0.##", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 黑纱透明度存档写入失败（本次仍生效，重启会丢）：{ex.Message}", 2);
        }
    }

    /// <summary>
    ///     把当前倍数应用到这棵子树里**所有** SpineSprite（含自己）。
    ///
    ///     <para>★ 为什么面板调值必须走这里，而不是只刷"套过皮的那些"：
    ///     皮肤面板里的**预览小人**是自己造 <c>SpineSprite</c> 再 <c>SetSkeletonDataRes</c> 的，
    ///     **不经过 <see cref="OrcaSceneSkin.Apply" />** ⇒ 从来不在追踪表里。
    ///     用户实测「人物不会跟着变」的根因就在这：按箭头时重刷了 0 个（日志可见）。</para>
    /// </summary>
    internal static int ApplyToTree(Node? root)
    {
        EnsureLoaded();
        if (root == null || !GodotObject.IsInstanceValid(root)) return 0;

        var n = 0;
        foreach (var sprite in OrcaSceneSkin.FindSpineSprites(root))
        {
            Track(sprite);
            n += ApplyTo(sprite, "面板调值");
        }
        return n;
    }

    /// <summary>记住这个 SpineSprite 并立刻套上当前倍数（套皮后调用）。</summary>
    internal static int TrackAndApply(Node spineSprite)
    {
        EnsureLoaded();
        Track(spineSprite);
        return ApplyTo(spineSprite, "套皮后");
    }

    private static void Track(Node spineSprite)
    {
        foreach (var tracked in Tracked)
        {
            if (ReferenceEquals(tracked, spineSprite)) return;      // 已在表里
        }
        Tracked.Add(spineSprite);
    }

    /// <summary>把当前倍数应用到所有还在场的 SpineSprite 上（改完值立刻可见）。</summary>
    private static int ReapplyAll()
    {
        // 先剪掉已被引擎释放的节点（Godot 节点由场景树持有，这里只留引用、不阻止回收）
        Tracked.RemoveAll(obj => !GodotObject.IsInstanceValid(obj));

        var n = 0;
        foreach (var obj in Tracked)
        {
            if (obj is Node node) n += ApplyTo(node, "改值后");
        }
        return n;
    }

    /// <summary>
    ///     把当前倍数写进该骨架里所有目标槽位；返回命中的槽位数。
    ///     <para>命中 0 是**正常情况**：板甲皮肤没有这几个件。</para>
    /// </summary>
    private static int ApplyTo(Node spineSprite, string why)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(spineSprite)) return 0;

            var mega = new MegaSprite(spineSprite);
            var skeleton = mega.GetSkeleton();
            if (skeleton == null) return 0;

            var slotsVariant = skeleton.BoundObject.Call("get_slots");
            if (slotsVariant.VariantType != Variant.Type.Array) return 0;

            var k = _multiplier;
            var hit = 0;
            var scanned = 0;
            var dump = new List<string>();
            foreach (var item in slotsVariant.AsGodotArray())
            {
                var slot = item.AsGodotObject();
                if (slot == null) continue;
                scanned++;

                var slotName = SlotLabel(slot);

                var attachmentVariant = slot.Call("get_attachment");
                var attachmentName = string.Empty;
                if (attachmentVariant.VariantType == Variant.Type.Object)
                {
                    var attachment = attachmentVariant.AsGodotObject();
                    if (attachment != null) attachmentName = attachment.Call("get_name").AsString();
                }

                if (dump.Count < DumpLimit)
                {
                    dump.Add($"{slotName}={(attachmentName.Length == 0 ? "<无件>" : attachmentName)}");
                }

                // ★ 主判据 = 槽位名（实测与 region 名同名）；attachment 名作兜底。
                if (!IsTarget(slotName) && !IsTarget(attachmentName)) continue;

                // (k,k,k,k) = RGB 与 α 同乘 k ⇒ 与贴图级预乘同比缩放等价
                slot.Call("set_color", new Color(k, k, k, k));
                hit++;
            }

            if (hit > 0)
            {
                OrcaLog.Info($"[Orca] 黑纱透明度 {Display} 已应用到 {hit} 个槽位（{why}）", 2);
            }
            else if (!_dumpedSlots)
            {
                // ★ 只在第一次"一个都没匹配上"时摊开真实「槽位=件名」：
                //   否则"命中 0"永远说不清是件名不同、还是件根本没挂在该皮肤上（本次就卡在这里）。
                _dumpedSlots = true;
                OrcaLog.Info($"[Orca] 黑纱透明度：扫了 {scanned} 个槽位，没有目标件。"
                             + $"前 {dump.Count} 条「槽位=件名」：{string.Join(" | ", dump)}", 2);
            }
            return hit;
        }
        catch (Exception ex)
        {
            OrcaLog.Warn($"[Orca] 黑纱透明度应用失败（{why}，不影响画面其余部分）：{ex.Message}", 2);
            return 0;
        }
    }

    private static bool IsTarget(string name)
    {
        foreach (var t in TargetNames)
        {
            if (string.Equals(t, name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    ///     槽位名（<c>slot.get_data().get_name()</c>；取不到就退回问号，不抛）。
    ///     <para>只用于诊断输出——匹配件名走的是 attachment 名，不是槽位名。</para>
    /// </summary>
    private static string SlotLabel(GodotObject slot)
    {
        try
        {
            var dataVariant = slot.Call("get_data");
            if (dataVariant.VariantType != Variant.Type.Object) return "?";
            var data = dataVariant.AsGodotObject();
            if (data == null) return "?";
            return data.Call("get_name").AsString();
        }
        catch (Exception)
        {
            return "?";   // 诊断用：取不到槽位名不该影响主流程，但也只在诊断里出现
        }
    }
}
