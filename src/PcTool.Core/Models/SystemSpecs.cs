namespace PcTool.Core.Models;

/// <summary>Details the app cannot detect reliably, entered by the user on the Settings page.</summary>
public sealed class UserSystemOverrides
{
    public string? Chipset { get; set; }
    public int? PsuWatts { get; set; }
    public int? CaseMaxGpuLengthMm { get; set; }
    /// <summary>"ATX", "mATX" or "ITX".</summary>
    public string? CaseFormFactor { get; set; }
    public int? FreeM2Slots { get; set; }
}

/// <summary>
/// The system as the compatibility, games and upgrade engines see it: detected hardware resolved against
/// the tier database, merged with user overrides. Unknown values stay null so rules can report "couldn't verify".
/// </summary>
public sealed class SystemSpecs
{
    public CpuTier? CpuTier { get; init; }
    public GpuTier? GpuTier { get; init; }
    public string CpuName { get; init; } = "";
    public string GpuName { get; init; } = "";

    public double CpuSingleScore { get; init; }
    public double CpuMultiScore { get; init; }
    public double GpuScore { get; init; }
    public double VramGb { get; init; }
    public bool GpuIsIntegrated { get; init; }
    public string GpuDirectX { get; init; } = "12_1";
    public bool GpuRayTracing { get; init; }
    public int CpuTdpW { get; init; }
    public int GpuTdpW { get; init; }

    public string Socket { get; init; } = "";
    public ChipsetInfo? Chipset { get; init; }
    public MemoryType MemoryType { get; init; }
    public int RamGb { get; init; }
    public int RamSlots { get; init; }
    public int RamSlotsUsed { get; init; }
    public int MaxRamGb { get; init; }
    public int? PcieGen => Chipset?.PcieGen;

    public int? PsuWatts { get; init; }
    public int? CaseMaxGpuLengthMm { get; init; }
    public string? CaseFormFactor { get; init; }
    public int? FreeM2Slots { get; init; }

    public StorageKind SystemDriveKind { get; init; }
    public bool HasSsd { get; init; }
    public bool HasNvme { get; init; }
    public double LargestFreeSpaceGb { get; init; }

    /// <summary>True when scores came from the user's own benchmark run because the hardware was not in the tier database.</summary>
    public bool CpuScoreFromBenchmark { get; init; }
    public bool GpuScoreFromBenchmark { get; init; }

    public double CpuGamingScore => Models.CpuTier.GamingScoreOf(CpuSingleScore, CpuMultiScore, CpuTier?.GamingFactor ?? 1);

    public SystemSpecs With(
        CpuTier? cpu = null, GpuTier? gpu = null, int? ramGb = null, MemoryType? memoryType = null,
        string? socket = null, ChipsetInfo? chipset = null, int? psuWatts = null, StorageKind? systemDrive = null,
        int? ramSlots = null, int? maxRamGb = null)
    {
        return new SystemSpecs
        {
            CpuTier = cpu ?? CpuTier,
            GpuTier = gpu ?? GpuTier,
            CpuName = cpu?.Name ?? CpuName,
            GpuName = gpu?.Name ?? GpuName,
            CpuSingleScore = cpu?.SingleScore ?? CpuSingleScore,
            CpuMultiScore = cpu?.MultiScore ?? CpuMultiScore,
            GpuScore = gpu?.Score ?? GpuScore,
            VramGb = gpu?.VramGb ?? VramGb,
            GpuIsIntegrated = gpu?.IsIntegrated ?? GpuIsIntegrated,
            GpuDirectX = gpu?.DirectX ?? GpuDirectX,
            GpuRayTracing = gpu?.RayTracing ?? GpuRayTracing,
            CpuTdpW = cpu?.TdpW ?? CpuTdpW,
            GpuTdpW = gpu?.TdpW ?? GpuTdpW,
            Socket = socket ?? Socket,
            Chipset = chipset ?? Chipset,
            MemoryType = memoryType ?? MemoryType,
            RamGb = ramGb ?? RamGb,
            RamSlots = ramSlots ?? RamSlots,
            RamSlotsUsed = RamSlotsUsed,
            MaxRamGb = maxRamGb ?? MaxRamGb,
            PsuWatts = psuWatts ?? PsuWatts,
            CaseMaxGpuLengthMm = CaseMaxGpuLengthMm,
            CaseFormFactor = CaseFormFactor,
            FreeM2Slots = FreeM2Slots,
            SystemDriveKind = systemDrive ?? SystemDriveKind,
            HasSsd = HasSsd || systemDrive is StorageKind.Nvme or StorageKind.SataSsd,
            HasNvme = HasNvme || systemDrive is StorageKind.Nvme,
            LargestFreeSpaceGb = LargestFreeSpaceGb,
            CpuScoreFromBenchmark = cpu is null && CpuScoreFromBenchmark,
            GpuScoreFromBenchmark = gpu is null && GpuScoreFromBenchmark,
        };
    }
}
