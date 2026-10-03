using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;

namespace OrcaCharacter;

/// <summary>银龙奥卡的卡池（照 Watcher 的 CardPoolModel 写法）。</summary>
public sealed class OrcaCardPool : CardPoolModel
{
    public override string Title => "orca";
    public override string EnergyColorName => "orca";

    /// <summary>卡框随**自建皮肤系统**：婚纱 = 白（silver），板甲 = 偏黑灰（gunmetal）。两份材质我们自己带。</summary>
    public override string CardFrameMaterialPath => OrcaSkin.FrameMaterialName;
    public override Godot.Color DeckEntryCardColor => new("D8DCE6");
    public override bool IsColorless => false;

    /// <summary>
    ///     本角色全部卡。★ 稀有度必须**覆盖 Common / Uncommon / Rare 三档**，
    ///     否则卡牌奖励摇到某一档时找不到卡会报
    ///     <c>couldn't generate a valid rarity</c>（实测踩过）：
    ///     打击/防御 = Basic、龙族魔典 = Common、龙鳞 = Uncommon、嗜血龙剑 = Rare。
    /// </summary>
    protected override CardModel[] GenerateAllCards() => new CardModel[]
    {
        ModelDb.Card<OrcaStrike>(),
        ModelDb.Card<OrcaDefend>(),
        ModelDb.Card<OrcaDragonCodex>(),
        ModelDb.Card<OrcaBloodSword>(),

        // ★ 卡牌扩充第一版（用户口径 2026-09-16）
        //   魔典系
        ModelDb.Card<OrcaCodexIgnition>(),
        ModelDb.Card<OrcaCodexEmber>(),
        ModelDb.Card<OrcaEmberChase>(),
        //   魔剑系
        ModelDb.Card<OrcaBloodScabbard>(),
        ModelDb.Card<OrcaCrimsonTemper>(),
        ModelDb.Card<OrcaFrenzySlash>(),
        //   银龙系
        ModelDb.Card<OrcaBloodForge>(),
        ModelDb.Card<OrcaNirvanaGrasp>(),
        ModelDb.Card<OrcaTrample>(),

        //   ★ 先古牌（嗜血魔剑）—— 不靠随机获得，而是由遗物「古老牙齿」把龙剑变化而来。
        //     但**仍必须在这里注册**：否则 ModelDb.Card<OrcaBloodBlade>() 取不到实例，
        //     古老牙齿的映射注入（OrcaArchaicToothPatch）会失败 ⇒ 玩家永远拿不到魔剑。
        ModelDb.Card<OrcaBloodBlade>(),

        // ★★ 卡牌扩充第二版（用户设计见 `奥卡卡包集\\卡牌包2\\新建 文本文档.txt`）
        //   —— 体系标签：「无色」= **不改变能量球形态**（OrcaOrbForm.None），不是原版无色牌。
        //   无色系
        ModelDb.Card<OrcaRollingFlame>(),      // 卷焰斩   1费 攻击 白
        ModelDb.Card<OrcaFireCloak>(),         // 飞火披肩 1费 技能 蓝
        ModelDb.Card<OrcaMoltenBone>(),        // 熔渊枯骨 3费 能力 金
        ModelDb.Card<OrcaDragonDignity>(),     // 龙之威仪 1费 技能 蓝
        ModelDb.Card<OrcaOverlook>(),          // 睥睨     1费 技能 金
        //   银龙系
        ModelDb.Card<OrcaBloodNirvana>(),      // 浴血涅槃 3费 能力 金
        ModelDb.Card<OrcaMeltBlood>(),         // 融血     1费 技能 蓝
        ModelDb.Card<OrcaReverseScale>(),      // 逆鳞     3费 攻击 金（★ 已被随机池排除，见 FilterThroughEpochs）
        ModelDb.Card<OrcaEmberWing>(),         // 烬血之翼 2费 技能 金
        ModelDb.Card<OrcaHomestead>(),         // 栖途     3费 能力 先古
        //   魔典 / 魔剑系
        ModelDb.Card<OrcaRuinBurn>(),          // 毁烧     1费 攻击 白
        ModelDb.Card<OrcaVoidReturn>(),        // 归墟     2费 能力 蓝
    };

    /// <summary>
    ///     ★★ 「魔典 / 龙剑不进随机池」的开关。
    ///
    ///     <para>
    ///     用户口径 2026-09-16：「<i>魔典与龙剑是唯一卡牌，<b>不会出现在卡牌奖励与商店里</b></i>」。
    ///     </para>
    ///
    ///     <para>
    ///     ⚠️⚠️ <b>默认 false，故意不启用</b> —— <b>打开前必须先补卡，否则商店会直接抛异常</b>：
    ///     <list type="number">
    ///       <item>商店固定要 5 张牌，类型是 <c>{Attack, Attack, Skill, Skill, Power}</c>
    ///         （<c>MerchantInventory.cs:15</c> 的 <c>_coloredCardTypes</c>），候选来自
    ///         <c>CardPool.GetUnlockedCards(...)</c>（<c>MerchantInventory.cs:101</c>）；</item>
    ///       <item><c>CardFactory.CreateForMerchant</c> 先
    ///         <c>Where(c =&gt; c.Rarity != CardRarity.Basic)</c>（<c>CardFactory.cs:47</c>）再按类型找；
    ///         找不到就 <c>rarity2 == CardRarity.None</c> ⇒ 抛
    ///         <c>InvalidOperationException("Can't generate valid rarity for merchant card type Attack")</c>
    ///         （2026-09-16 实机日志实锤过一次）。</item>
    ///     </list>
    ///     本池排除这两张后，池内仍有 **2 张攻击牌 + 2 张能力牌**（见交接文档 §21.3），
    ///     所以那个开关现在**具备打开条件**。
    ///     ⇒ <b>Attack 与 Power 两类同时取空</b>。
    ///     </para>
    ///
    ///     <para>
    ///     ⇒ <b>启用前提</b>：池子里至少有 <b>1 张非 Basic 的 Attack + 1 张非 Basic 的 Power</b>。
    ///     卡牌奖励那一侧即使只有 1 张可随机卡也不会崩：<c>CardFactory.RollForRarity</c> 内部走
    ///     <c>GetNextAllowedRarity</c>（沿稀有度向上循环兜底）。
    ///     卡面素材见工作区 `卡牌美术清单-可用卡面.md`（`奥卡卡图` 包共 54 张：Attack 18 / Skill 20 / Power 16）。
    ///     </para>
    ///
    ///     <para>
    ///     ⚠️ 用 <c>static readonly</c> 而**不是** <c>const</c>：<c>const false</c> 会被编译期折叠，
    ///     后面的过滤分支会变成"无法访问的代码"（CS0162），本项目要求零警告。
    ///     </para>
    /// </summary>
    ///     ★ 2026-09-16 **已启用**（用户实测反馈："嗜血龙剑出现在商店了"）：
    ///     此前一直是 false 是因为**池子太薄**（排除后只剩龙鳞一张技能牌，商店必崩）；
    ///     卡牌扩充补齐了 **2 张攻击牌（践踏/逐焰）+ 2 张能力牌（焚文/红莲淬）** 之后，
    ///     `verify_pool_filter.py` 的安全断言已置位 ⇒ 现在可以安全开启。
    ///     ⚠️ 用 <c>static readonly</c> 而**不是** <c>const</c>：<c>const false</c> 会被编译期折叠，
    ///     后面的过滤分支会变成"无法访问的代码"（CS0162），本项目要求零警告。
    /// </summary>
    private static readonly bool ExcludeSignatureCardsFromRandomPool = true;

    /// <summary>
    ///     ★ 随机池过滤点 —— 奖励与商店**唯一**的共同候选来源。
    ///
    ///     <para>
    ///     反编译 <c>CardPoolModel.GetUnlockedCards()</c>（<c>CardPoolModel.cs:101-122</c>）：
    ///     <c>FilterThroughEpochs(...)</c> → 再按多人约束过滤。卡牌奖励与商人货架都从
    ///     <c>GetUnlockedCards</c> 取候选 ⇒ 这里是唯一能同时作用于两者的点。
    ///     </para>
    ///
    ///     <para>
    ///     ⚠️ 别指望 <c>CardModel.CanBeGeneratedInCombat</c> / <c>CanBeGeneratedByModifiers</c>
    ///     （<c>CardModel.cs:643/649</c>）：反编译确认它们只被
    ///     <c>CardFactory.FilterForCombat</c>（<c>CardFactory.cs:161</c>）与变形（<c>:203</c>）读取，
    ///     <b>奖励与商店都不看它们</b>。
    ///     </para>
    /// </summary>
    protected override IEnumerable<CardModel> FilterThroughEpochs(UnlockState unlockState, IEnumerable<CardModel> cards)
    {
        List<CardModel> list = base.FilterThroughEpochs(unlockState, cards).ToList();

        // 默认不启用（开关与"必须先补卡"的原因见 ExcludeSignatureCardsFromRandomPool 的长注释）
        if (!ExcludeSignatureCardsFromRandomPool) return list;

        // ★ 排除清单（都不该出现在奖励/商店里）：
        //   · OrcaBloodSword / OrcaDragonCodex —— 起始专属卡（用户口径：唯一卡牌）
        //   · OrcaReverseScale —— 第二版设计原文写明"**不可被获得**也不在初始卡组"
        int removed = list.RemoveAll(c => c is OrcaBloodSword or OrcaDragonCodex or OrcaReverseScale or OrcaOverlook);
        OrcaLog.Info($"[Orca] 随机池过滤：排除起始专属/不可获得卡 {removed} 张 → 剩 {list.Count} 张"
                 + $"（类型分布 {string.Join("、", list.GroupBy(c => c.Type).Select(g => $"{g.Key}×{g.Count()}"))}）", 2);
        return list;
    }
}

/// <summary>遗物池。</summary>
public sealed class OrcaRelicPool : RelicPoolModel
{
    public override string EnergyColorName => "orca";

    /// <summary>阶段 1：池子里先只放起始遗物「银龙血统」。</summary>
    protected override RelicModel[] GenerateAllRelics() => new RelicModel[]
    {
        ModelDb.Relic<OrcaBloodline>()
    };
}

/// <summary>药水池。</summary>
public sealed class OrcaPotionPool : PotionPoolModel
{
    public override string EnergyColorName => "orca";

    /// <summary>阶段 1：先给空池（后续再补奥卡专属药水）。</summary>
    protected override PotionModel[] GenerateAllPotions() => Array.Empty<PotionModel>();
}