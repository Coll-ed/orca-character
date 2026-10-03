using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using BaseLib.Abstracts;
using BaseLib.Utils.Attributes;

namespace OrcaCharacter;

/// <summary>
///     银龙奥卡（独立角色）。规格见工作区 `规格-银龙奥卡独立角色.md`：
///     初始生命 60、起始遗物「银龙血统」、初始卡组 打击×4 + 防御×4 + 龙族魔典 + 嗜血龙剑。
///     模型 id 由类型名推导（`ModelDb.GetId`）⇒ ORCA；本地化键用 `ORCA.*`。
/// </summary>
// ★ 第 1 步（按官方 wiki 接入 BaseLib 注册体系）：
//   原来 `: CharacterModel`（普通）⇒ 不在 CustomContentDictionary.CustomCharacters 里
//   ⇒ BaseLib 生成百科过滤器时永远不会为奥卡建 ⇒ 原版 NCardLibrary.OnSubmenuOpened 里
//      `_cardPoolFilters[characterModel]`（无保护索引）就会抛 KeyNotFoundException
//      ⇒ 异常被 Godot 的 UI 回调吞掉 ⇒ 界面半初始化 ⇒ 硬卡死、无堆栈
//   换基类即可完成注册（全部成员 virtual 且有默认值）
//   ⚠️ [CustomID("ORCA")] 必须保留：否则 id 会变成 CHARACTER.ORCACHARACTER-ORCA
[CustomID("ORCA")]
public sealed class Orca : CustomCharacterModel
{
    // ── 基础数值 ────────────────────────────────────────────
    public override int StartingHp => 60;
    public override int StartingGold => 99;
    public override CharacterGender Gender => (CharacterGender)1;   // 女

    // ── 三池 ───────────────────────────────────────────────
    public override CardPoolModel CardPool => ModelDb.CardPool<OrcaCardPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<OrcaPotionPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<OrcaRelicPool>();

    // ── 初始卡组 / 起始遗物 ─────────────────────────────────
    public override IEnumerable<CardModel> StartingDeck => new CardModel[]
    {
        ModelDb.Card<OrcaStrike>(), ModelDb.Card<OrcaStrike>(),
        ModelDb.Card<OrcaStrike>(), ModelDb.Card<OrcaStrike>(),
        ModelDb.Card<OrcaDefend>(), ModelDb.Card<OrcaDefend>(),
        ModelDb.Card<OrcaDefend>(), ModelDb.Card<OrcaDefend>(),
        ModelDb.Card<OrcaDragonCodex>(),
        ModelDb.Card<OrcaBloodSword>()
    };

    public override IReadOnlyList<RelicModel> StartingRelics => new[] { ModelDb.Relic<OrcaBloodline>() };

    // ── 演出 ───────────────────────────────────────────────
    public override float AttackAnimDelay => 0.15f;
    public override float CastAnimDelay => 0.25f;

    /// <summary>
    ///     ★ 主界面**选中角色时**的音效（用户 2026-09-17："主界面的切人音效！那个换成战士的，变个调子"）。
    ///
    ///     <para>这里直接指向**铁甲战士的 FMOD 事件**（原版 <c>CharacterModel</c> 的默认实现就是
    ///     <c>$"event:/sfx/characters/{id}/{id}_select"</c>）—— 保证"就是战士那个音"。</para>
    ///
    ///     <para>变调交给 <see cref="OrcaResAudioPatch" />：它在播放这一层给这个事件**塞一个
    ///     <c>pitch</c> 参数**（FMOD 事件若定义了该参数即生效，见 <see cref="OrcaAudio.SelectPitch" />）。
    ///     若该事件没有这个参数 ⇒ 退化为原声（不报错）。</para>
    /// </summary>
    public override string CharacterSelectSfx => "res://OrcaCharacter/audio/orca_select.tres";

    /// <summary>过场音：同样是铁甲战士的过场音，变调后自备。</summary>
    public override string CharacterTransitionSfx => "res://OrcaCharacter/audio/orca_transit.tres";

    public override List<string> GetArchitectAttackVfx() => new()
    {
        "vfx/vfx_attack_slash", "vfx/vfx_bloody_impact"
    };

    /// <summary>
    ///     战斗外观：按当前皮肤（**奥卡皮肤包自带的皮肤管理器**里选的那套）换掉 SpineSprite 的骨架数据。
    ///     每次进战斗先 <see cref="OrcaSkin.Refresh" /> 读一次选择，所以管理器面板里一切换、下场战斗即生效。
    ///     只调游戏自带 API（<c>MegaSprite.SetSkeletonDataRes</c>），不调用任何第三方代码。
    /// </summary>
    public override CreatureAnimator GenerateAnimator(MegaSprite controller, Creature creature)
    {
        try
        {
            OrcaSkin.Refresh();
            var path = OrcaSkin.BattleSkeleton;
            var res = ResourceLoader.Load<Resource>(path);
            if (res != null)
            {
                controller.SetSkeletonDataRes(new MegaSkeletonDataResource(res));
                Log.Info($"[Orca] 战斗外观骨架已套用：{OrcaSkin.Active} → {path}", 2);
            }
            else
            {
                Log.Warn($"[Orca] 战斗骨架加载失败：{path}", 2);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Orca] 套用战斗骨架出错：{ex.Message}", 2);
        }

        return base.GenerateAnimator(controller, creature);
    }

    // ── 配色（银白 + 血红，先用一套；美术定了再调） ──────────
    public override Color NameColor => new("D8DCE6");

    /// <summary>能量数字的描边色：板甲＝冷灰黑 / 婚纱＝暗红（与能量球同主题）。</summary>
    public override Color EnergyLabelOutlineColor => OrcaSkin.IsWedding ? new Color("5C1013FF") : new Color("33363CFF");
    public override Color DialogueColor => new("2A2E38");
    public override Color MapDrawingColor => new("D8DCE6");
    public override Color RemoteTargetingLineColor => new("F0F2F6");
    public override Color RemoteTargetingLineOutline => new("8A2B2BFF");

    // ── 解锁与地图图标 ──────────────────────────────────────
    /// <summary>
    ///     ★ 打完奥卡**不解锁任何角色** —— 我们不在原版那条"通关 → 解锁下一个角色"的链上。
    ///
    ///     <para>⚠️ 原来写的是 <c>ModelDb.Character&lt;Ironclad&gt;()</c>，那会导致
    ///     "通关奥卡 ⇒ 解锁铁甲战士"，属于写错；BaseLib 的 <c>CustomCharacterModel</c>
    ///     默认值同样是 <c>null</c>（见 `Abstracts/CustomCharacterModel.cs`：
    ///     <c>protected override CharacterModel? UnlocksAfterRunAs =&gt; null;</c>）。</para>
    /// </summary>
    protected override CharacterModel? UnlocksAfterRunAs => null;

    /// <summary>
    ///     地图上的角色标记。★ 审查修正：原来指向 <c>images/packed/map/icons/map_marker_ironclad.png</c> ——
    ///     **那条路径不存在**（原版标记真身是 ui_atlas 里的精灵 `images/atlases/ui_atlas.sprites/map/icons/…`），
    ///     所以地图上我们的标记其实是不显示的（潜伏 bug）。
    ///     现在自带一张同规格（49×64，与铁甲战士那张一致）的奥卡标记：银白 + 血红。
    /// </summary>
    protected override string MapMarkerPath => "res://images/packed/map/icons/map_marker_orca.png";
}