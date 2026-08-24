using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 永久 scoped Tweak 的 Steam EXE 專屬靜態補丁容器（ISSUE-049）。
///
/// 第一個 helper 將訓練／研究的 execdelay 依發令物件 owner 分流；多人遊戲、
/// 缺少必要引擎指標或非目標 command 時一律傳回原版值。這仍未接入
/// TrainerModule，也不會出現在 GUI 或寫進真實遊戲。
/// </summary>
public static class ScopedTweakPatch
{
    public const string SectionName = ".cktw";
    public const uint FormatVersion = 1;
    public const uint HeaderSize = 64;
    public const uint FlagSinglePlayerOnly = 1;

    // CVX command scheduler:
    //   EDX = command definition
    //   ESI = issuing object
    //   004FB6A8 mov edx,[ecx+1C]
    //   004FB6AB mov eax,[edx+F4]   ; execdelay
    //   004FB6B1 mov ecx,[008AECC4]
    public const uint CommandDelaySiteVa = 0x004FB6AB;
    public static readonly byte[] CommandDelayOriginal = [0x8B, 0x82, 0xF4, 0x00, 0x00, 0x00];

    // Steam 2004-02-19 engine globals / layouts used by the command helper.
    public const uint GameGlobalVa = 0x008C1C8C;
    public const uint EngineBaseGlobalVa = 0x008AA6C8;
    public const uint SessionOffset = 0x50;
    public const uint MultiplayerMaskOffset = 0x108;
    public const uint LocalPlayerOffset = 0xCD0;
    public const uint ObjectOwnerOffset = 0x6E;
    public const uint CommandTrainFlagOffset = 0xCF;
    public const uint CommandResearchFlagOffset = 0xD0;

    // Settlement income tick (ESI = Settlement*):
    //   00502750 mov ecx,[esi+32] ; gold production
    //   00502753 add esp,4
    //   00502828 mov eax,[esi+36] ; food production
    //   0050282B test eax,eax
    public const uint GoldProductionSiteVa = 0x00502750;
    public const uint FoodProductionSiteVa = 0x00502828;
    public static readonly byte[] GoldProductionOriginal = [0x8B, 0x4E, 0x32, 0x83, 0xC4, 0x04];
    public static readonly byte[] FoodProductionOriginal = [0x8B, 0x46, 0x36, 0x85, 0xC0];
    public const uint SettlementGoldProductionOffset = 0x32;
    public const uint SettlementFoodProductionOffset = 0x36;
    public const uint SettlementOwnerOffset = 0x90;

    // Settlement population ticks (ECX = Settlement*).
    public const uint PopulationGrowthAmountSiteVa = 0x005026B6;
    public const uint PopulationGrowthIntervalSiteVa = 0x005026C7;
    public const uint PopulationLossPercentSiteVa = 0x005026EF;
    public const uint PopulationLossIntervalSiteVa = 0x00502716;
    public static readonly byte[] PopulationGrowthAmountOriginal = [0x8B, 0x35, 0x20, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationGrowthIntervalOriginal = [0x8B, 0x15, 0x24, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationLossPercentOriginal = [0x0F, 0xAF, 0x15, 0x18, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationLossIntervalOriginal = [0x8B, 0x15, 0x1C, 0x28, 0x73, 0x00];
    public const uint PopulationGrowthAmountGlobalVa = 0x00732820;
    public const uint PopulationGrowthIntervalGlobalVa = 0x00732824;
    public const uint PopulationLossPercentGlobalVa = 0x00732818;
    public const uint PopulationLossIntervalGlobalVa = 0x0073281C;

    // Settlement constructor fallback for class settlement_gold. This instruction is
    // reached only when the map/save did not supply an explicit current-gold override.
    public const uint InitialGoldSiteVa = 0x0050132E;
    public static readonly byte[] InitialGoldOriginal = [0x8B, 0x8D, 0xEC, 0x03, 0x00, 0x00];
    public const uint SettlementClassInitialGoldOffset = 0x3EC;
    public const uint PlayerStructSize = 0x254;
    public const uint PlayerArrayOffset = 0xCD4;

    private const uint Magic = 0x57544B43; // "CKTW" little-endian
    private const uint CommandHelperOffset = HeaderSize;
    private const uint GoldHelperOffset = 384;
    private const uint FoodHelperOffset = 640;
    private const uint PopulationGrowthAmountHelperOffset = 896;
    private const uint PopulationGrowthIntervalHelperOffset = 1152;
    private const uint PopulationLossPercentHelperOffset = 1408;
    private const uint PopulationLossIntervalHelperOffset = 1664;
    private const uint InitialGoldHelperOffset = 1920;
    private const uint ConfigOffset = 2176;
    private const uint ConfigCount = 48;
    private const uint HookCount = 8;

    public sealed record CommandSettings(
        uint SelfTrainSpeedQ16,
        uint EnemyTrainSpeedQ16,
        uint SelfResearchSpeedQ16,
        uint EnemyResearchSpeedQ16,
        uint SelfWagonBuildMilliseconds,
        uint EnemyWagonBuildMilliseconds)
    {
        public static CommandSettings Vanilla { get; } = new(
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            7000, 7000);
    }

    public sealed record ProductionSettings(
        uint SelfTownhallGold,
        uint SelfVillageGold,
        uint EnemyTownhallGold,
        uint EnemyVillageGold,
        uint SelfTownhallFood,
        uint SelfVillageFood,
        uint EnemyTownhallFood,
        uint EnemyVillageFood)
    {
        public static ProductionSettings Vanilla { get; } = new(
            24, 0, 24, 0,
            0, 20, 0, 20);
    }

    public sealed record PopulationSettings(
        uint SelfTownhallGrowthAmount,
        uint SelfVillageGrowthAmount,
        uint EnemyTownhallGrowthAmount,
        uint EnemyVillageGrowthAmount,
        uint SelfTownhallGrowthInterval,
        uint SelfVillageGrowthInterval,
        uint EnemyTownhallGrowthInterval,
        uint EnemyVillageGrowthInterval,
        uint SelfTownhallLossPercent,
        uint SelfVillageLossPercent,
        uint EnemyTownhallLossPercent,
        uint EnemyVillageLossPercent,
        uint SelfTownhallLossInterval,
        uint SelfVillageLossInterval,
        uint EnemyTownhallLossInterval,
        uint EnemyVillageLossInterval)
    {
        public static PopulationSettings Vanilla { get; } = new(
            1, 1, 1, 1,
            20_000, 20_000, 20_000, 20_000,
            10, 10, 10, 10,
            4_000, 4_000, 4_000, 4_000);
    }

    public sealed record CapacitySettings(
        bool Enabled,
        uint SelfTownhallMaxGold,
        uint SelfVillageMaxGold,
        uint EnemyTownhallMaxGold,
        uint EnemyVillageMaxGold,
        uint SelfTownhallMaxFood,
        uint SelfVillageMaxFood,
        uint EnemyTownhallMaxFood,
        uint EnemyVillageMaxFood,
        uint SelfTownhallMaxPopulation,
        uint SelfVillageMaxPopulation,
        uint EnemyTownhallMaxPopulation,
        uint EnemyVillageMaxPopulation)
    {
        public static CapacitySettings Disabled { get; } = new(
            false,
            100_000, 5_000, 100_000, 5_000,
            100_000, 5_000, 100_000, 5_000,
            100, 20, 100, 20);
    }

    public sealed record InitialGoldSettings(
        bool Enabled,
        uint SelfTownhall,
        uint SelfVillage,
        uint EnemyTownhall,
        uint EnemyVillage)
    {
        public static InitialGoldSettings Disabled { get; } = new(false, 2_500, 0, 2_500, 0);
    }

    public sealed record PatchInfo(
        int OriginalFileLength,
        uint SectionVa,
        uint HelperVa,
        uint HelperSize,
        uint GoldHelperVa,
        uint GoldHelperSize,
        uint FoodHelperVa,
        uint FoodHelperSize,
        uint PopulationGrowthAmountHelperVa,
        uint PopulationGrowthAmountHelperSize,
        uint PopulationGrowthIntervalHelperVa,
        uint PopulationGrowthIntervalHelperSize,
        uint PopulationLossPercentHelperVa,
        uint PopulationLossPercentHelperSize,
        uint PopulationLossIntervalHelperVa,
        uint PopulationLossIntervalHelperSize,
        uint InitialGoldHelperVa,
        uint InitialGoldHelperSize,
        uint Flags,
        uint Hooks,
        CommandSettings Settings,
        ProductionSettings Production,
        PopulationSettings Population,
        CapacitySettings Capacity,
        InitialGoldSettings InitialGold);

    public static bool IsOriginal(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            return pe.FindSection(SectionName) < 0 &&
                   pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length)
                     .AsSpan().SequenceEqual(CommandDelayOriginal) &&
                   pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
                     .AsSpan().SequenceEqual(GoldProductionOriginal) &&
                   pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
                     .AsSpan().SequenceEqual(FoodProductionOriginal) &&
                   pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationGrowthAmountOriginal) &&
                   pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationGrowthIntervalOriginal) &&
                   pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationLossPercentOriginal) &&
                   pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationLossIntervalOriginal) &&
                   pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length)
                     .AsSpan().SequenceEqual(InitialGoldOriginal);
        }
        catch { return false; }
    }

    public static bool IsApplied(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            PatchInfo info = ReadInfo(pe);
            return HasKnownCommandHook(pe, info);
        }
        catch { return false; }
    }

    public static byte[] Apply(
        byte[] exeBytes,
        CommandSettings? settings = null,
        ProductionSettings? production = null,
        PopulationSettings? population = null,
        CapacitySettings? capacity = null,
        InitialGoldSettings? initialGold = null)
    {
        var pe = PeFile.Parse(exeBytes);
        int sectionIndex = pe.FindSection(SectionName);

        if (sectionIndex >= 0)
        {
            PatchInfo existing = ReadInfo(pe);
            if (!HasKnownCommandHook(pe, existing))
                throw new InvalidOperationException(".cktw 存在，但 command-delay hook/helper 不是本工具產生的已知狀態。");

            CommandSettings effectiveCommand = settings is null ? existing.Settings : ValidateSettings(settings);
            ProductionSettings updatedProduction = production ?? existing.Production;
            PopulationSettings updatedPopulation = population is null
                ? existing.Population
                : ValidatePopulationSettings(population);
            CapacitySettings updatedCapacity = capacity is null
                ? existing.Capacity
                : ValidateCapacitySettings(capacity);
            InitialGoldSettings updatedInitialGold = initialGold is null
                ? existing.InitialGold
                : ValidateInitialGoldSettings(initialGold);
            if (effectiveCommand != existing.Settings || updatedProduction != existing.Production ||
                updatedPopulation != existing.Population || updatedCapacity != existing.Capacity ||
                updatedInitialGold != existing.InitialGold)
                WriteSettings(pe, existing.SectionVa, effectiveCommand, updatedProduction,
                    updatedPopulation, updatedCapacity, updatedInitialGold);

            return pe.ToBytes();
        }

        byte[] current = pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length);
        if (!current.AsSpan().SequenceEqual(CommandDelayOriginal))
            throw new InvalidOperationException("command-delay 原始指令不符，拒絕建立 .cktw。");
        if (!pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
               .AsSpan().SequenceEqual(GoldProductionOriginal))
            throw new InvalidOperationException("gold-production 原始指令不符，拒絕建立 .cktw。");
        if (!pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
               .AsSpan().SequenceEqual(FoodProductionOriginal))
            throw new InvalidOperationException("food-production 原始指令不符，拒絕建立 .cktw。");
        ValidateOriginalSite(pe, PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal, "population-growth-amount");
        ValidateOriginalSite(pe, PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal, "population-growth-interval");
        ValidateOriginalSite(pe, PopulationLossPercentSiteVa, PopulationLossPercentOriginal, "population-loss-percent");
        ValidateOriginalSite(pe, PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal, "population-loss-interval");
        ValidateOriginalSite(pe, InitialGoldSiteVa, InitialGoldOriginal, "initial-gold");

        CommandSettings effective = ValidateSettings(settings ?? CommandSettings.Vanilla);
        ProductionSettings effectiveProduction = production ?? ProductionSettings.Vanilla;
        PopulationSettings effectivePopulation = ValidatePopulationSettings(population ?? PopulationSettings.Vanilla);
        CapacitySettings effectiveCapacity = ValidateCapacitySettings(capacity ?? CapacitySettings.Disabled);
        InitialGoldSettings effectiveInitialGold = ValidateInitialGoldSettings(
            initialGold ?? InitialGoldSettings.Disabled);
        byte[] payload = BuildPayload(exeBytes.Length, effective, effectiveProduction,
            effectivePopulation, effectiveCapacity, effectiveInitialGold);
        PeSection section = pe.AddSection(
            SectionName,
            (uint)payload.Length,
            PeFile.ImageScnCntCode | PeFile.ImageScnCntInitializedData |
            PeFile.ImageScnMemExecute | PeFile.ImageScnMemRead,
            payload);

        uint sectionVa = checked((uint)pe.ImageBase + section.VirtualAddress);
        uint helperVa = sectionVa + CommandHelperOffset;
        byte[] helper = BuildCommandHelper(sectionVa + ConfigOffset);
        byte[] goldHelper = BuildGoldProductionHelper(sectionVa + ConfigOffset);
        byte[] foodHelper = BuildFoodProductionHelper(sectionVa + ConfigOffset);
        byte[] growthAmountHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi);
        byte[] growthIntervalHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] lossPercentHelper = BuildPopulationLossPercentHelper(sectionVa + ConfigOffset);
        byte[] lossIntervalHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] initialGoldHelper = BuildInitialGoldHelper(sectionVa + ConfigOffset);
        if (helper.Length > GoldHelperOffset - CommandHelperOffset ||
            goldHelper.Length > FoodHelperOffset - GoldHelperOffset ||
            foodHelper.Length > PopulationGrowthAmountHelperOffset - FoodHelperOffset ||
            growthAmountHelper.Length > PopulationGrowthIntervalHelperOffset - PopulationGrowthAmountHelperOffset ||
            growthIntervalHelper.Length > PopulationLossPercentHelperOffset - PopulationGrowthIntervalHelperOffset ||
            lossPercentHelper.Length > PopulationLossIntervalHelperOffset - PopulationLossPercentHelperOffset ||
            lossIntervalHelper.Length > InitialGoldHelperOffset - PopulationLossIntervalHelperOffset ||
            initialGoldHelper.Length > ConfigOffset - InitialGoldHelperOffset)
            throw new InvalidOperationException(".cktw helper 超出保留空間。");

        pe.WriteUInt32AtVa(sectionVa + 40, checked((uint)helper.Length));
        pe.WriteUInt32AtVa(sectionVa + 44, GoldHelperOffset);
        pe.WriteUInt32AtVa(sectionVa + 48, checked((uint)goldHelper.Length));
        pe.WriteUInt32AtVa(sectionVa + 52, FoodHelperOffset);
        pe.WriteUInt32AtVa(sectionVa + 56, checked((uint)foodHelper.Length));
        pe.WriteBytesAtVa(helperVa, helper);
        pe.WriteBytesAtVa(sectionVa + GoldHelperOffset, goldHelper);
        pe.WriteBytesAtVa(sectionVa + FoodHelperOffset, foodHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationGrowthAmountHelperOffset, growthAmountHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationGrowthIntervalHelperOffset, growthIntervalHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationLossPercentHelperOffset, lossPercentHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationLossIntervalHelperOffset, lossIntervalHelper);
        pe.WriteBytesAtVa(sectionVa + InitialGoldHelperOffset, initialGoldHelper);
        pe.WriteBytesAtVa(CommandDelaySiteVa, BuildCommandHook(helperVa));
        pe.WriteBytesAtVa(GoldProductionSiteVa,
            BuildRelativeCall(GoldProductionSiteVa, sectionVa + GoldHelperOffset, GoldProductionOriginal.Length));
        pe.WriteBytesAtVa(FoodProductionSiteVa,
            BuildRelativeCall(FoodProductionSiteVa, sectionVa + FoodHelperOffset, FoodProductionOriginal.Length));
        pe.WriteBytesAtVa(PopulationGrowthAmountSiteVa,
            BuildRelativeCall(PopulationGrowthAmountSiteVa,
                sectionVa + PopulationGrowthAmountHelperOffset, PopulationGrowthAmountOriginal.Length));
        pe.WriteBytesAtVa(PopulationGrowthIntervalSiteVa,
            BuildRelativeCall(PopulationGrowthIntervalSiteVa,
                sectionVa + PopulationGrowthIntervalHelperOffset, PopulationGrowthIntervalOriginal.Length));
        pe.WriteBytesAtVa(PopulationLossPercentSiteVa,
            BuildRelativeCall(PopulationLossPercentSiteVa,
                sectionVa + PopulationLossPercentHelperOffset, PopulationLossPercentOriginal.Length));
        pe.WriteBytesAtVa(PopulationLossIntervalSiteVa,
            BuildRelativeCall(PopulationLossIntervalSiteVa,
                sectionVa + PopulationLossIntervalHelperOffset, PopulationLossIntervalOriginal.Length));
        pe.WriteBytesAtVa(InitialGoldSiteVa,
            BuildRelativeCall(InitialGoldSiteVa, sectionVa + InitialGoldHelperOffset, InitialGoldOriginal.Length));
        return pe.ToBytes();
    }

    public static byte[] Reverse(byte[] exeBytes)
    {
        var pe = PeFile.Parse(exeBytes);
        if (pe.FindSection(SectionName) < 0)
        {
            if (!pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length)
                   .AsSpan().SequenceEqual(CommandDelayOriginal) ||
                !pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
                   .AsSpan().SequenceEqual(GoldProductionOriginal) ||
                !pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
                   .AsSpan().SequenceEqual(FoodProductionOriginal) ||
                !pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationGrowthAmountOriginal) ||
                !pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationGrowthIntervalOriginal) ||
                !pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationLossPercentOriginal) ||
                !pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationLossIntervalOriginal) ||
                !pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length)
                   .AsSpan().SequenceEqual(InitialGoldOriginal))
                throw new InvalidOperationException("找不到 .cktw，但 scoped hook 指令也不是原版；拒絕猜測還原。");
            return pe.ToBytes();
        }

        PatchInfo info = ReadInfo(pe);
        if (!HasKnownCommandHook(pe, info))
            throw new InvalidOperationException(".cktw command-delay hook/helper 已遭修改；拒絕猜測還原。");

        pe.WriteBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal);
        pe.WriteBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal);
        pe.WriteBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal);
        pe.WriteBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal);
        pe.WriteBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal);
        pe.WriteBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal);
        pe.WriteBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal);
        pe.WriteBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal);
        pe.RemoveSection(SectionName, info.OriginalFileLength);
        return pe.ToBytes();
    }

    public static PatchInfo ReadInfo(byte[] exeBytes) => ReadInfo(PeFile.Parse(exeBytes));

    private static PatchInfo ReadInfo(PeFile pe)
    {
        int index = pe.FindSection(SectionName);
        if (index < 0) throw new InvalidOperationException("找不到 .cktw section。");

        PeSection section = pe.Sections[index];
        int raw = pe.RvaToFileOffset(section.VirtualAddress);
        if (section.SizeOfRawData < HeaderSize || pe.ReadUInt32(raw) != Magic)
            throw new InvalidOperationException(".cktw header magic 不符。");
        if (pe.ReadUInt32(raw + 4) != FormatVersion)
            throw new InvalidOperationException(".cktw 格式版本不支援。");
        if (pe.ReadUInt32(raw + 12) != HeaderSize || pe.ReadUInt32(raw + 20) != CommandHelperOffset)
            throw new InvalidOperationException(".cktw header layout 不符。");
        if (pe.ReadUInt32(raw + 24) != ConfigOffset || pe.ReadUInt32(raw + 28) != ConfigCount)
            throw new InvalidOperationException(".cktw command 設定表 layout 不符。");
        if (pe.ReadUInt32(raw + 32) != HookCount)
            throw new InvalidOperationException(".cktw hook manifest 數量不符。");

        uint helperSize = pe.ReadUInt32(raw + 40);
        uint goldHelperOffset = pe.ReadUInt32(raw + 44);
        uint goldHelperSize = pe.ReadUInt32(raw + 48);
        uint foodHelperOffset = pe.ReadUInt32(raw + 52);
        uint foodHelperSize = pe.ReadUInt32(raw + 56);
        if (helperSize == 0 || helperSize > GoldHelperOffset - CommandHelperOffset ||
            goldHelperOffset != GoldHelperOffset || goldHelperSize == 0 ||
            goldHelperSize > FoodHelperOffset - GoldHelperOffset ||
            foodHelperOffset != FoodHelperOffset || foodHelperSize == 0 ||
            foodHelperSize > PopulationGrowthAmountHelperOffset - FoodHelperOffset)
            throw new InvalidOperationException(".cktw command helper 長度不合法。");

        uint payloadSize = pe.ReadUInt32(raw + 16);
        if (payloadSize > section.SizeOfRawData || payloadSize < ConfigOffset + ConfigCount * 4)
            throw new InvalidOperationException(".cktw payload 長度不合法。");

        int originalLength = checked((int)pe.ReadUInt32(raw + 8));
        uint flags = pe.ReadUInt32(raw + 36);
        int cfg = raw + (int)ConfigOffset;
        var settings = new CommandSettings(
            pe.ReadUInt32(cfg), pe.ReadUInt32(cfg + 4),
            pe.ReadUInt32(cfg + 8), pe.ReadUInt32(cfg + 12),
            pe.ReadUInt32(cfg + 16), pe.ReadUInt32(cfg + 20));
        var production = new ProductionSettings(
            pe.ReadUInt32(cfg + 24), pe.ReadUInt32(cfg + 28),
            pe.ReadUInt32(cfg + 32), pe.ReadUInt32(cfg + 36),
            pe.ReadUInt32(cfg + 40), pe.ReadUInt32(cfg + 44),
            pe.ReadUInt32(cfg + 48), pe.ReadUInt32(cfg + 52));
        var population = ValidatePopulationSettings(new PopulationSettings(
            pe.ReadUInt32(cfg + 56), pe.ReadUInt32(cfg + 60),
            pe.ReadUInt32(cfg + 64), pe.ReadUInt32(cfg + 68),
            pe.ReadUInt32(cfg + 72), pe.ReadUInt32(cfg + 76),
            pe.ReadUInt32(cfg + 80), pe.ReadUInt32(cfg + 84),
            pe.ReadUInt32(cfg + 88), pe.ReadUInt32(cfg + 92),
            pe.ReadUInt32(cfg + 96), pe.ReadUInt32(cfg + 100),
            pe.ReadUInt32(cfg + 104), pe.ReadUInt32(cfg + 108),
            pe.ReadUInt32(cfg + 112), pe.ReadUInt32(cfg + 116)));
        var capacity = ValidateCapacitySettings(new CapacitySettings(
            pe.ReadUInt32(cfg + 120) != 0,
            pe.ReadUInt32(cfg + 124), pe.ReadUInt32(cfg + 128),
            pe.ReadUInt32(cfg + 132), pe.ReadUInt32(cfg + 136),
            pe.ReadUInt32(cfg + 140), pe.ReadUInt32(cfg + 144),
            pe.ReadUInt32(cfg + 148), pe.ReadUInt32(cfg + 152),
            pe.ReadUInt32(cfg + 156), pe.ReadUInt32(cfg + 160),
            pe.ReadUInt32(cfg + 164), pe.ReadUInt32(cfg + 168)));
        var initialGold = ValidateInitialGoldSettings(new InitialGoldSettings(
            pe.ReadUInt32(cfg + 172) != 0,
            pe.ReadUInt32(cfg + 176), pe.ReadUInt32(cfg + 180),
            pe.ReadUInt32(cfg + 184), pe.ReadUInt32(cfg + 188)));

        uint sectionVa = checked((uint)pe.ImageBase + section.VirtualAddress);
        uint configVa = sectionVa + ConfigOffset;
        uint growthAmountSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi).Length);
        uint growthIntervalSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx).Length);
        uint lossPercentSize = checked((uint)BuildPopulationLossPercentHelper(configVa).Length);
        uint lossIntervalSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx).Length);
        uint initialGoldSize = checked((uint)BuildInitialGoldHelper(configVa).Length);
        return new PatchInfo(originalLength, sectionVa, sectionVa + CommandHelperOffset,
            helperSize, sectionVa + goldHelperOffset, goldHelperSize,
            sectionVa + foodHelperOffset, foodHelperSize,
            sectionVa + PopulationGrowthAmountHelperOffset, growthAmountSize,
            sectionVa + PopulationGrowthIntervalHelperOffset, growthIntervalSize,
            sectionVa + PopulationLossPercentHelperOffset, lossPercentSize,
            sectionVa + PopulationLossIntervalHelperOffset, lossIntervalSize,
            sectionVa + InitialGoldHelperOffset, initialGoldSize,
            flags, HookCount, settings, production, population, capacity, initialGold);
    }

    private static byte[] BuildPayload(
        int originalFileLength,
        CommandSettings settings,
        ProductionSettings production,
        PopulationSettings population,
        CapacitySettings capacity,
        InitialGoldSettings initialGold)
    {
        int payloadSize = checked((int)(ConfigOffset + ConfigCount * 4));
        byte[] payload = new byte[payloadSize];

        Write(payload, 0, Magic);
        Write(payload, 4, FormatVersion);
        Write(payload, 8, checked((uint)originalFileLength));
        Write(payload, 12, HeaderSize);
        Write(payload, 16, (uint)payload.Length);
        Write(payload, 20, CommandHelperOffset);
        Write(payload, 24, ConfigOffset);
        Write(payload, 28, ConfigCount);
        Write(payload, 32, HookCount);
        Write(payload, 36, FlagSinglePlayerOnly);

        int cfg = (int)ConfigOffset;
        Write(payload, cfg, settings.SelfTrainSpeedQ16);
        Write(payload, cfg + 4, settings.EnemyTrainSpeedQ16);
        Write(payload, cfg + 8, settings.SelfResearchSpeedQ16);
        Write(payload, cfg + 12, settings.EnemyResearchSpeedQ16);
        Write(payload, cfg + 16, settings.SelfWagonBuildMilliseconds);
        Write(payload, cfg + 20, settings.EnemyWagonBuildMilliseconds);
        Write(payload, cfg + 24, production.SelfTownhallGold);
        Write(payload, cfg + 28, production.SelfVillageGold);
        Write(payload, cfg + 32, production.EnemyTownhallGold);
        Write(payload, cfg + 36, production.EnemyVillageGold);
        Write(payload, cfg + 40, production.SelfTownhallFood);
        Write(payload, cfg + 44, production.SelfVillageFood);
        Write(payload, cfg + 48, production.EnemyTownhallFood);
        Write(payload, cfg + 52, production.EnemyVillageFood);
        Write(payload, cfg + 56, population.SelfTownhallGrowthAmount);
        Write(payload, cfg + 60, population.SelfVillageGrowthAmount);
        Write(payload, cfg + 64, population.EnemyTownhallGrowthAmount);
        Write(payload, cfg + 68, population.EnemyVillageGrowthAmount);
        Write(payload, cfg + 72, population.SelfTownhallGrowthInterval);
        Write(payload, cfg + 76, population.SelfVillageGrowthInterval);
        Write(payload, cfg + 80, population.EnemyTownhallGrowthInterval);
        Write(payload, cfg + 84, population.EnemyVillageGrowthInterval);
        Write(payload, cfg + 88, population.SelfTownhallLossPercent);
        Write(payload, cfg + 92, population.SelfVillageLossPercent);
        Write(payload, cfg + 96, population.EnemyTownhallLossPercent);
        Write(payload, cfg + 100, population.EnemyVillageLossPercent);
        Write(payload, cfg + 104, population.SelfTownhallLossInterval);
        Write(payload, cfg + 108, population.SelfVillageLossInterval);
        Write(payload, cfg + 112, population.EnemyTownhallLossInterval);
        Write(payload, cfg + 116, population.EnemyVillageLossInterval);
        Write(payload, cfg + 120, capacity.Enabled ? 1u : 0u);
        Write(payload, cfg + 124, capacity.SelfTownhallMaxGold);
        Write(payload, cfg + 128, capacity.SelfVillageMaxGold);
        Write(payload, cfg + 132, capacity.EnemyTownhallMaxGold);
        Write(payload, cfg + 136, capacity.EnemyVillageMaxGold);
        Write(payload, cfg + 140, capacity.SelfTownhallMaxFood);
        Write(payload, cfg + 144, capacity.SelfVillageMaxFood);
        Write(payload, cfg + 148, capacity.EnemyTownhallMaxFood);
        Write(payload, cfg + 152, capacity.EnemyVillageMaxFood);
        Write(payload, cfg + 156, capacity.SelfTownhallMaxPopulation);
        Write(payload, cfg + 160, capacity.SelfVillageMaxPopulation);
        Write(payload, cfg + 164, capacity.EnemyTownhallMaxPopulation);
        Write(payload, cfg + 168, capacity.EnemyVillageMaxPopulation);
        Write(payload, cfg + 172, initialGold.Enabled ? 1u : 0u);
        Write(payload, cfg + 176, initialGold.SelfTownhall);
        Write(payload, cfg + 180, initialGold.SelfVillage);
        Write(payload, cfg + 184, initialGold.EnemyTownhall);
        Write(payload, cfg + 188, initialGold.EnemyVillage);
        return payload;
    }

    private static bool HasKnownCommandHook(PeFile pe, PatchInfo info)
    {
        byte[] expectedHook = BuildCommandHook(info.HelperVa);
        if (!pe.ReadBytesAtVa(CommandDelaySiteVa, expectedHook.Length).AsSpan().SequenceEqual(expectedHook))
            return false;

        byte[] expectedHelper = BuildCommandHelper(info.SectionVa + ConfigOffset);
        byte[] expectedGoldHelper = BuildGoldProductionHelper(info.SectionVa + ConfigOffset);
        byte[] expectedFoodHelper = BuildFoodProductionHelper(info.SectionVa + ConfigOffset);
        byte[] expectedGrowthAmountHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi);
        byte[] expectedGrowthIntervalHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] expectedLossPercentHelper = BuildPopulationLossPercentHelper(info.SectionVa + ConfigOffset);
        byte[] expectedLossIntervalHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] expectedInitialGoldHelper = BuildInitialGoldHelper(info.SectionVa + ConfigOffset);
        return info.HelperSize == expectedHelper.Length &&
               pe.ReadBytesAtVa(info.HelperVa, expectedHelper.Length).AsSpan().SequenceEqual(expectedHelper) &&
               info.GoldHelperSize == expectedGoldHelper.Length &&
               pe.ReadBytesAtVa(info.GoldHelperVa, expectedGoldHelper.Length).AsSpan().SequenceEqual(expectedGoldHelper) &&
               info.FoodHelperSize == expectedFoodHelper.Length &&
               pe.ReadBytesAtVa(info.FoodHelperVa, expectedFoodHelper.Length).AsSpan().SequenceEqual(expectedFoodHelper) &&
               info.PopulationGrowthAmountHelperSize == expectedGrowthAmountHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationGrowthAmountHelperVa, expectedGrowthAmountHelper.Length)
                 .AsSpan().SequenceEqual(expectedGrowthAmountHelper) &&
               info.PopulationGrowthIntervalHelperSize == expectedGrowthIntervalHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationGrowthIntervalHelperVa, expectedGrowthIntervalHelper.Length)
                 .AsSpan().SequenceEqual(expectedGrowthIntervalHelper) &&
               info.PopulationLossPercentHelperSize == expectedLossPercentHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationLossPercentHelperVa, expectedLossPercentHelper.Length)
                 .AsSpan().SequenceEqual(expectedLossPercentHelper) &&
               info.PopulationLossIntervalHelperSize == expectedLossIntervalHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationLossIntervalHelperVa, expectedLossIntervalHelper.Length)
                 .AsSpan().SequenceEqual(expectedLossIntervalHelper) &&
               info.InitialGoldHelperSize == expectedInitialGoldHelper.Length &&
               pe.ReadBytesAtVa(info.InitialGoldHelperVa, expectedInitialGoldHelper.Length)
                 .AsSpan().SequenceEqual(expectedInitialGoldHelper) &&
               pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(GoldProductionSiteVa, info.GoldHelperVa, GoldProductionOriginal.Length)) &&
               pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(FoodProductionSiteVa, info.FoodHelperVa, FoodProductionOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationGrowthAmountSiteVa,
                     info.PopulationGrowthAmountHelperVa, PopulationGrowthAmountOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationGrowthIntervalSiteVa,
                     info.PopulationGrowthIntervalHelperVa, PopulationGrowthIntervalOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationLossPercentSiteVa,
                     info.PopulationLossPercentHelperVa, PopulationLossPercentOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationLossIntervalSiteVa,
                     info.PopulationLossIntervalHelperVa, PopulationLossIntervalOriginal.Length)) &&
               pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(InitialGoldSiteVa,
                     info.InitialGoldHelperVa, InitialGoldOriginal.Length));
    }

    private static CommandSettings ValidateSettings(CommandSettings settings)
    {
        if (settings.SelfTrainSpeedQ16 == 0 || settings.EnemyTrainSpeedQ16 == 0 ||
            settings.SelfResearchSpeedQ16 == 0 || settings.EnemyResearchSpeedQ16 == 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "command 速度 Q16.16 必須大於 0。");
        return settings;
    }

    private static PopulationSettings ValidatePopulationSettings(PopulationSettings population)
    {
        uint[] growthAmounts =
        [
            population.SelfTownhallGrowthAmount, population.SelfVillageGrowthAmount,
            population.EnemyTownhallGrowthAmount, population.EnemyVillageGrowthAmount
        ];
        uint[] growthIntervals =
        [
            population.SelfTownhallGrowthInterval, population.SelfVillageGrowthInterval,
            population.EnemyTownhallGrowthInterval, population.EnemyVillageGrowthInterval
        ];
        uint[] lossPercents =
        [
            population.SelfTownhallLossPercent, population.SelfVillageLossPercent,
            population.EnemyTownhallLossPercent, population.EnemyVillageLossPercent
        ];
        uint[] lossIntervals =
        [
            population.SelfTownhallLossInterval, population.SelfVillageLossInterval,
            population.EnemyTownhallLossInterval, population.EnemyVillageLossInterval
        ];

        if (growthAmounts.Any(value => value > 10_000) ||
            growthIntervals.Any(value => value is < 100 or > 10_000_000) ||
            lossPercents.Any(value => value > 100) ||
            lossIntervals.Any(value => value is < 100 or > 2_000_000_000))
            throw new ArgumentOutOfRangeException(nameof(population), "人口 scoped 設定超出安全範圍。");

        return population;
    }

    private static CapacitySettings ValidateCapacitySettings(CapacitySettings capacity)
    {
        uint[] resourceCaps =
        [
            capacity.SelfTownhallMaxGold, capacity.SelfVillageMaxGold,
            capacity.EnemyTownhallMaxGold, capacity.EnemyVillageMaxGold,
            capacity.SelfTownhallMaxFood, capacity.SelfVillageMaxFood,
            capacity.EnemyTownhallMaxFood, capacity.EnemyVillageMaxFood
        ];
        uint[] populationCaps =
        [
            capacity.SelfTownhallMaxPopulation, capacity.SelfVillageMaxPopulation,
            capacity.EnemyTownhallMaxPopulation, capacity.EnemyVillageMaxPopulation
        ];

        if (resourceCaps.Any(value => value > 100_000_000) ||
            populationCaps.Any(value => value is < 1 or > 100_000))
            throw new ArgumentOutOfRangeException(nameof(capacity), "聚落容量 scoped 設定超出安全範圍。");

        return capacity;
    }

    private static InitialGoldSettings ValidateInitialGoldSettings(InitialGoldSettings initialGold)
    {
        if (new[]
            {
                initialGold.SelfTownhall, initialGold.SelfVillage,
                initialGold.EnemyTownhall, initialGold.EnemyVillage
            }.Any(value => value > 100_000_000))
            throw new ArgumentOutOfRangeException(nameof(initialGold), "聚落初始金錢 scoped 設定超出安全範圍。");
        return initialGold;
    }

    private static void ValidateOriginalSite(PeFile pe, uint siteVa, byte[] original, string name)
    {
        if (!pe.ReadBytesAtVa(siteVa, original.Length).AsSpan().SequenceEqual(original))
            throw new InvalidOperationException($"{name} 原始指令不符，拒絕建立 .cktw。");
    }

    private static void WriteSettings(
        PeFile pe,
        uint sectionVa,
        CommandSettings settings,
        ProductionSettings production,
        PopulationSettings population,
        CapacitySettings capacity,
        InitialGoldSettings initialGold)
    {
        ulong cfg = sectionVa + ConfigOffset;
        pe.WriteUInt32AtVa(cfg, settings.SelfTrainSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 4, settings.EnemyTrainSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 8, settings.SelfResearchSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 12, settings.EnemyResearchSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 16, settings.SelfWagonBuildMilliseconds);
        pe.WriteUInt32AtVa(cfg + 20, settings.EnemyWagonBuildMilliseconds);
        pe.WriteUInt32AtVa(cfg + 24, production.SelfTownhallGold);
        pe.WriteUInt32AtVa(cfg + 28, production.SelfVillageGold);
        pe.WriteUInt32AtVa(cfg + 32, production.EnemyTownhallGold);
        pe.WriteUInt32AtVa(cfg + 36, production.EnemyVillageGold);
        pe.WriteUInt32AtVa(cfg + 40, production.SelfTownhallFood);
        pe.WriteUInt32AtVa(cfg + 44, production.SelfVillageFood);
        pe.WriteUInt32AtVa(cfg + 48, production.EnemyTownhallFood);
        pe.WriteUInt32AtVa(cfg + 52, production.EnemyVillageFood);
        pe.WriteUInt32AtVa(cfg + 56, population.SelfTownhallGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 60, population.SelfVillageGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 64, population.EnemyTownhallGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 68, population.EnemyVillageGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 72, population.SelfTownhallGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 76, population.SelfVillageGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 80, population.EnemyTownhallGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 84, population.EnemyVillageGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 88, population.SelfTownhallLossPercent);
        pe.WriteUInt32AtVa(cfg + 92, population.SelfVillageLossPercent);
        pe.WriteUInt32AtVa(cfg + 96, population.EnemyTownhallLossPercent);
        pe.WriteUInt32AtVa(cfg + 100, population.EnemyVillageLossPercent);
        pe.WriteUInt32AtVa(cfg + 104, population.SelfTownhallLossInterval);
        pe.WriteUInt32AtVa(cfg + 108, population.SelfVillageLossInterval);
        pe.WriteUInt32AtVa(cfg + 112, population.EnemyTownhallLossInterval);
        pe.WriteUInt32AtVa(cfg + 116, population.EnemyVillageLossInterval);
        pe.WriteUInt32AtVa(cfg + 120, capacity.Enabled ? 1u : 0u);
        pe.WriteUInt32AtVa(cfg + 124, capacity.SelfTownhallMaxGold);
        pe.WriteUInt32AtVa(cfg + 128, capacity.SelfVillageMaxGold);
        pe.WriteUInt32AtVa(cfg + 132, capacity.EnemyTownhallMaxGold);
        pe.WriteUInt32AtVa(cfg + 136, capacity.EnemyVillageMaxGold);
        pe.WriteUInt32AtVa(cfg + 140, capacity.SelfTownhallMaxFood);
        pe.WriteUInt32AtVa(cfg + 144, capacity.SelfVillageMaxFood);
        pe.WriteUInt32AtVa(cfg + 148, capacity.EnemyTownhallMaxFood);
        pe.WriteUInt32AtVa(cfg + 152, capacity.EnemyVillageMaxFood);
        pe.WriteUInt32AtVa(cfg + 156, capacity.SelfTownhallMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 160, capacity.SelfVillageMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 164, capacity.EnemyTownhallMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 168, capacity.EnemyVillageMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 172, initialGold.Enabled ? 1u : 0u);
        pe.WriteUInt32AtVa(cfg + 176, initialGold.SelfTownhall);
        pe.WriteUInt32AtVa(cfg + 180, initialGold.SelfVillage);
        pe.WriteUInt32AtVa(cfg + 184, initialGold.EnemyTownhall);
        pe.WriteUInt32AtVa(cfg + 188, initialGold.EnemyVillage);
    }

    private static byte[] BuildCommandHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // The replaced MOV only changes EAX. Preserve all other registers and EFLAGS.
        x86.Emit(0x9C, 0x51, 0x52, 0x53, 0x56, 0x57, 0x55); // pushfd; push ecx/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x82, 0xF4, 0x00, 0x00, 0x00);       // mov eax,[edx+F4]
        x86.Emit(0x85, 0xF6);                               // test esi,esi
        x86.Jump(0x84, "done");                            // jz done

        x86.EmitAbsoluteLoadEcx(GameGlobalVa);               // ecx = game
        x86.Emit(0x85, 0xC9);                               // test ecx,ecx
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x49, (byte)SessionOffset);           // mov ecx,[ecx+50]
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x80, 0xB9);                               // cmp byte [ecx+108],0
        x86.EmitUInt32(MultiplayerMaskOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "done");                            // jne done: all scoped tweaks disabled

        x86.EmitAbsoluteLoadEcx(EngineBaseGlobalVa);         // ecx = engine base
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x89);                               // mov ecx,[ecx+CD0]
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");

        x86.Emit(0x33, 0xDB);                               // xor ebx,ebx (self=0)
        x86.Emit(0x39, 0x8E);                               // cmp [esi+6E],ecx
        x86.EmitUInt32(ObjectOwnerOffset);
        x86.Emit(0x0F, 0x95, 0xC3);                         // setne bl (enemy=1)

        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+CF],0
        x86.EmitUInt32(CommandTrainFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "train");
        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+D0],0
        x86.EmitUInt32(CommandResearchFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "research");
        x86.Jump("done");

        x86.Label("train");
        x86.Emit(0x85, 0xDB);                               // test ebx,ebx
        x86.Jump(0x85, "enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa);
        x86.Jump("scale");
        x86.Label("enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa + 4);
        x86.Jump("scale");

        x86.Label("research");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 8);
        x86.Jump("scale");
        x86.Label("enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 12);

        x86.Label("scale");
        x86.Emit(0x85, 0xC0);                               // original zero remains zero
        x86.Jump(0x84, "done");
        x86.Emit(0x85, 0xC9);                               // corrupt zero setting fails closed
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);                                     // mov edi,65536
        x86.EmitUInt32(1u << 16);
        x86.Emit(0xF7, 0xE7);                               // mul edi -> edx:eax
        x86.Emit(0x3B, 0xD1);                               // cmp edx,ecx
        x86.Jump(0x83, "clamp");                           // quotient would overflow uint
        x86.Emit(0xF7, 0xF1);                               // div ecx
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x85, "done");
        x86.Emit(0x40);                                     // non-zero command delay stays >= 1
        x86.Jump("done");
        x86.Label("clamp");
        x86.Emit(0x83, 0xC8, 0xFF);                         // or eax,-1

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59, 0x9D, 0xC3);
        return x86.Build();
    }

    private static byte[] BuildGoldProductionHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original six bytes produce ECX and remove one old cdecl argument.
        // Preserve EAX because the caller copies it to EBX immediately after this hook.
        x86.Emit(0x50, 0x52, 0x53, 0x56, 0x57, 0x55);       // push eax/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x4E, (byte)SettlementGoldProductionOffset); // vanilla default

        x86.EmitAbsoluteLoadEdx(GameGlobalVa);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x52, (byte)SessionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x80, 0xBA);
        x86.EmitUInt32(MultiplayerMaskOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "done");

        x86.EmitAbsoluteLoadEdx(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x92);                               // mov edx,[edx+CD0]
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x33, 0xDB);                               // self=0, enemy=1
        x86.Emit(0x39, 0x96);                               // cmp [esi+90],edx
        x86.EmitUInt32(SettlementOwnerOffset);
        x86.Emit(0x0F, 0x95, 0xC3);

        // Vanilla storage remains untouched: non-zero gold marks Townhall;
        // otherwise non-zero food marks Village. Neither means an unsupported settlement.
        x86.Emit(0x8B, 0x56, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x56, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);                                     // edi=1 -> Village
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xFF);                               // edi=0 -> Townhall

        x86.Label("scope");
        x86.Emit(0x8D, 0x1C, 0x5F);                         // ebx = owner*2 + type
        x86.EmitIndexedLoadEcxByEbx(configVa + 24);         // gold production

        x86.Emit(0x83, 0x3D);                               // cmp dword [capacityEnabled],0
        x86.EmitUInt32(configVa + 120);
        x86.Emit(0x00);
        x86.Jump(0x84, "done");
        x86.EmitIndexedLoadEdxByEbx(configVa + 124);
        x86.Emit(0x89, 0x50, 0x0C);                         // resource.max_gold
        x86.EmitIndexedLoadEdxByEbx(configVa + 140);
        x86.Emit(0x89, 0x50, 0x10);                         // resource.max_food
        x86.EmitIndexedLoadEdxByEbx(configVa + 156);
        x86.Emit(0x89, 0x56, 0x3A);                         // central building max_population

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x58);       // pop ebp/edi/esi/ebx/edx/eax
        x86.Emit(0xC2, 0x04, 0x00);                         // ret 4: replaces add esp,4
        return x86.Build();
    }

    private static byte[] BuildFoodProductionHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original five bytes produce EAX and set ZF/SF/PF via TEST EAX,EAX.
        x86.Emit(0x51, 0x52, 0x53, 0x56, 0x57, 0x55);       // push ecx/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x46, (byte)SettlementFoodProductionOffset); // vanilla default

        x86.EmitAbsoluteLoadEcx(GameGlobalVa);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x49, (byte)SessionOffset);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x80, 0xB9);
        x86.EmitUInt32(MultiplayerMaskOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "done");

        x86.EmitAbsoluteLoadEcx(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x89);
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x33, 0xDB);
        x86.Emit(0x39, 0x8E);                               // cmp [esi+90],ecx
        x86.EmitUInt32(SettlementOwnerOffset);
        x86.Emit(0x0F, 0x95, 0xC3);

        x86.Emit(0x8B, 0x56, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x56, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xFF);

        x86.Label("scope");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy");
        x86.Emit(0x85, 0xFF);
        x86.Jump(0x85, "self_village");
        x86.EmitAbsoluteLoadEax(configVa + 40);
        x86.Jump("done");
        x86.Label("self_village");
        x86.EmitAbsoluteLoadEax(configVa + 44);
        x86.Jump("done");
        x86.Label("enemy");
        x86.Emit(0x85, 0xFF);
        x86.Jump(0x85, "enemy_village");
        x86.EmitAbsoluteLoadEax(configVa + 48);
        x86.Jump("done");
        x86.Label("enemy_village");
        x86.EmitAbsoluteLoadEax(configVa + 52);

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59);       // pop ebp/edi/esi/ebx/edx/ecx
        x86.Emit(0x85, 0xC0, 0xC3);                         // test eax,eax; ret
        return x86.Build();
    }

    private static byte[] BuildInitialGoldHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Replaces MOV ECX,[EBP+3EC] only on the constructor's -1 fallback path.
        // Caller [ESP+18] is the owner slot; CALL plus four pushes move it to [ESP+2C].
        x86.Emit(0x9C, 0x50, 0x52, 0x53);                  // pushfd; push eax/edx/ebx
        x86.Emit(0x8B, 0x8D);                              // vanilla class settlement_gold
        x86.EmitUInt32(SettlementClassInitialGoldOffset);
        x86.Emit(0x83, 0x3D);                              // cmp dword [enabled],0
        x86.EmitUInt32(configVa + 172);
        x86.Emit(0x00);
        x86.Jump(0x84, "done");

        x86.EmitAbsoluteLoadEax(GameGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x40, (byte)SessionOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "done");
        x86.Emit(0x80, 0xB8);
        x86.EmitUInt32(MultiplayerMaskOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "done");

        x86.EmitAbsoluteLoadEax(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x90);                              // edx = local player pointer
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x5C, 0x24, 0x2C);                  // ebx = constructor owner slot
        x86.Emit(0x83, 0xFB, 0x10);
        x86.Jump(0x83, "done");                           // invalid/neutral slot: vanilla
        x86.Emit(0x69, 0xDB);                              // imul ebx,ebx,0x254
        x86.EmitUInt32(PlayerStructSize);
        x86.Emit(0x8D, 0x9C, 0x18);                        // owner ptr = base + index + CD4
        x86.EmitUInt32(PlayerArrayOffset);
        x86.Emit(0x33, 0xC0);                              // self=0, enemy=1
        x86.Emit(0x3B, 0xDA);                              // cmp ebx,edx
        x86.Emit(0x0F, 0x95, 0xC0);                        // setne al

        x86.Emit(0x33, 0xDB);                              // Townhall type=0
        x86.Emit(0x83, 0x7E, (byte)SettlementGoldProductionOffset, 0x00);
        x86.Jump(0x85, "scope");
        x86.Emit(0x83, 0x7E, (byte)SettlementFoodProductionOffset, 0x00);
        x86.Jump(0x84, "done");
        x86.Emit(0x43);                                    // Village type=1

        x86.Label("scope");
        x86.Emit(0x8D, 0x04, 0x43);                        // eax = owner*2 + type
        x86.EmitIndexedLoadEcxByEax(configVa + 176);

        x86.Label("done");
        x86.Emit(0x5B, 0x5A, 0x58, 0x9D, 0xC3);            // restore; ret
        return x86.Build();
    }

    private enum PopulationLoadTarget
    {
        Esi,
        Edx
    }

    private static byte[] BuildPopulationLoadHelper(
        uint configVa,
        uint configFieldOffset,
        uint vanillaGlobalVa,
        PopulationLoadTarget target)
    {
        var x86 = new X86Builder();

        // The replaced absolute MOV does not alter EFLAGS. EAX/EBX/EBP are the only
        // scratch registers used by the scope selector; ECX remains Settlement*.
        x86.Emit(0x9C, 0x50, 0x53, 0x55);                  // pushfd; push eax/ebx/ebp
        Action<uint> emitLoad;
        switch (target)
        {
            case PopulationLoadTarget.Esi:
                x86.EmitAbsoluteLoadEsi(vanillaGlobalVa);
                emitLoad = x86.EmitAbsoluteLoadEsi;
                break;
            case PopulationLoadTarget.Edx:
                x86.EmitAbsoluteLoadEdx(vanillaGlobalVa);
                emitLoad = x86.EmitAbsoluteLoadEdx;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }

        EmitSettlementScopeSelection(x86, configVa + configFieldOffset, "done", emitLoad);

        x86.Label("done");
        x86.Emit(0x5D, 0x5B, 0x58, 0x9D, 0xC3);            // pop ebp/ebx/eax; popfd; ret
        return x86.Build();
    }

    private static byte[] BuildPopulationLossPercentHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original instruction is IMUL EDX,[PopulationDecreasePercent]. Keep EDX as
        // input/output and perform one final IMUL after selecting the scoped multiplier.
        x86.Emit(0x50, 0x53, 0x56, 0x57, 0x55);            // push eax/ebx/esi/edi/ebp
        x86.EmitAbsoluteLoadEdi(PopulationLossPercentGlobalVa);
        EmitSettlementScopeSelection(x86, configVa + 88, "multiply", x86.EmitAbsoluteLoadEdi);

        x86.Label("multiply");
        x86.Emit(0x0F, 0xAF, 0xD7);                        // imul edx,edi
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x58, 0xC3);      // restore; ret (POPs keep IMUL flags)
        return x86.Build();
    }

    private static void EmitSettlementScopeSelection(
        X86Builder x86,
        uint firstConfigVa,
        string fallbackLabel,
        Action<uint> emitLoad)
    {
        x86.EmitAbsoluteLoadEax(GameGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0x8B, 0x40, (byte)SessionOffset);          // mov eax,[eax+50]
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0x80, 0xB8);                              // cmp byte [eax+108],0
        x86.EmitUInt32(MultiplayerMaskOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, fallbackLabel);

        x86.EmitAbsoluteLoadEax(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0x8B, 0x80);                              // mov eax,[eax+CD0]
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0x33, 0xDB);                              // self=0, enemy=1
        x86.Emit(0x39, 0x81);                              // cmp [ecx+90],eax
        x86.EmitUInt32(SettlementOwnerOffset);
        x86.Emit(0x0F, 0x95, 0xC3);

        x86.Emit(0x8B, 0x41, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x41, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0xBD);                                    // ebp=1 -> Village
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xED);                              // ebp=0 -> Townhall

        x86.Label("scope");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy");
        x86.Emit(0x85, 0xED);
        x86.Jump(0x85, "self_village");
        emitLoad(firstConfigVa);
        x86.Jump(fallbackLabel);
        x86.Label("self_village");
        emitLoad(firstConfigVa + 4);
        x86.Jump(fallbackLabel);
        x86.Label("enemy");
        x86.Emit(0x85, 0xED);
        x86.Jump(0x85, "enemy_village");
        emitLoad(firstConfigVa + 8);
        x86.Jump(fallbackLabel);
        x86.Label("enemy_village");
        emitLoad(firstConfigVa + 12);
    }

    private static byte[] BuildCommandHook(uint helperVa)
    {
        return BuildRelativeCall(CommandDelaySiteVa, helperVa, CommandDelayOriginal.Length);
    }

    private static byte[] BuildRelativeCall(uint siteVa, uint helperVa, int overwrittenLength)
    {
        if (overwrittenLength < 5)
            throw new ArgumentOutOfRangeException(nameof(overwrittenLength));
        int displacement = checked((int)helperVa - (int)(siteVa + 5));
        byte[] hook = Enumerable.Repeat((byte)0x90, overwrittenLength).ToArray();
        hook[0] = 0xE8;
        BitConverter.TryWriteBytes(hook.AsSpan(1, 4), displacement);
        return hook;
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);

    private sealed class X86Builder
    {
        private readonly List<byte> _bytes = [];
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<(int DisplacementOffset, string Label)> _fixups = [];

        public void Emit(params byte[] bytes) => _bytes.AddRange(bytes);

        public void EmitUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BitConverter.TryWriteBytes(bytes, value);
            foreach (byte valueByte in bytes) _bytes.Add(valueByte);
        }

        public void EmitAbsoluteLoadEcx(uint address)
        {
            Emit(0x8B, 0x0D);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEdx(uint address)
        {
            Emit(0x8B, 0x15);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEax(uint address)
        {
            Emit(0xA1);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEsi(uint address)
        {
            Emit(0x8B, 0x35);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEdi(uint address)
        {
            Emit(0x8B, 0x3D);
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEbx(uint address)
        {
            Emit(0x8B, 0x0C, 0x9D);                        // mov ecx,[ebx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEdxByEbx(uint address)
        {
            Emit(0x8B, 0x14, 0x9D);                        // mov edx,[ebx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEax(uint address)
        {
            Emit(0x8B, 0x0C, 0x85);                        // mov ecx,[eax*4+disp32]
            EmitUInt32(address);
        }

        public void Label(string name)
        {
            if (!_labels.TryAdd(name, _bytes.Count))
                throw new InvalidOperationException($"重複的 x86 label：{name}");
        }

        public void Jump(string label)
        {
            Emit(0xE9);
            AddFixup(label);
        }

        public void Jump(byte condition, string label)
        {
            Emit(0x0F, condition);
            AddFixup(label);
        }

        public byte[] Build()
        {
            byte[] result = _bytes.ToArray();
            foreach ((int displacementOffset, string label) in _fixups)
            {
                if (!_labels.TryGetValue(label, out int target))
                    throw new InvalidOperationException($"找不到 x86 label：{label}");
                int displacement = checked(target - (displacementOffset + 4));
                BitConverter.TryWriteBytes(result.AsSpan(displacementOffset, 4), displacement);
            }
            return result;
        }

        private void AddFixup(string label)
        {
            int displacementOffset = _bytes.Count;
            _bytes.AddRange([0, 0, 0, 0]);
            _fixups.Add((displacementOffset, label));
        }
    }
}
