using System.Text.RegularExpressions;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Data;

/// <summary>
/// Resolves a detected <see cref="HardwareProfile"/> into <see cref="SystemSpecs"/>: tier lookup first, the user's own
/// benchmark scores when the part is not in the tier database, and a rough clock-based estimate as a last resort.
/// </summary>
public sealed partial class SystemSpecsBuilder(IPerformanceTierSource tiers, IChipsetCatalog chipsets)
{
    public SystemSpecs Build(HardwareProfile profile, UserSystemOverrides? overrides = null, BenchmarkRun? benchmark = null)
    {
        overrides ??= new UserSystemOverrides();

        var cpuTier = tiers.FindCpu(profile.Cpu.Name);
        var gpu = profile.PrimaryGpu;
        var gpuTier = gpu is null ? null : tiers.FindGpu(gpu.Name);

        var (single, multi, cpuFromBench) = CpuScores(profile.Cpu, cpuTier, benchmark);
        var (gpuScore, gpuFromBench) = GpuScore(gpu, gpuTier, benchmark);

        var chipset = chipsets.Find(overrides.Chipset)
                      ?? chipsets.Find(profile.Motherboard.Chipset)
                      ?? chipsets.Find(chipsets.DetectFromBoardName(profile.Motherboard.Product));

        var detectedSocket = SocketPattern().IsMatch(profile.Cpu.Socket) ? ChipsetCatalog.NormalizeSocket(profile.Cpu.Socket) : "";
        var socket = cpuTier?.Socket is { Length: > 0 } s && s != "BGA" ? s
            : detectedSocket.Length > 0 ? detectedSocket
            : chipset?.Socket ?? "";

        var installedBytes = profile.Memory.Modules.Aggregate(0UL, (sum, m) => sum + m.CapacityBytes);
        var ramGb = (int)Math.Round((installedBytes > 0 ? installedBytes : profile.Memory.TotalBytes) / 1024d / 1024 / 1024);

        var memoryType = profile.Memory.Type;
        if (memoryType == MemoryType.Unknown && cpuTier?.MemoryTypes.Length == 1)
            Enum.TryParse(cpuTier.MemoryTypes[0], out memoryType);

        var systemDrive = profile.SystemDrive;
        var vramGb = gpu is not null && !gpu.IsIntegrated ? Math.Round(gpu.VramGb, 1) : gpuTier?.VramGb ?? 0;

        return new SystemSpecs
        {
            CpuTier = cpuTier,
            GpuTier = gpuTier,
            CpuName = profile.Cpu.Name,
            GpuName = gpu?.Name ?? "No GPU detected",
            CpuSingleScore = single,
            CpuMultiScore = multi,
            CpuScoreFromBenchmark = cpuFromBench,
            GpuScore = gpuScore,
            GpuScoreFromBenchmark = gpuFromBench,
            VramGb = vramGb,
            GpuIsIntegrated = gpu?.IsIntegrated ?? true,
            GpuDirectX = gpuTier?.DirectX ?? "12_1",
            GpuRayTracing = gpuTier?.RayTracing ?? false,
            CpuTdpW = cpuTier?.TdpW ?? 95,
            GpuTdpW = gpuTier?.TdpW ?? (gpu?.IsIntegrated == false ? 200 : 0),
            Socket = socket,
            Chipset = chipset,
            MemoryType = memoryType,
            RamGb = ramGb,
            RamSlots = profile.Memory.SlotCount,
            RamSlotsUsed = profile.Memory.UsedSlots,
            MaxRamGb = (int)(profile.Memory.MaxCapacityBytes / 1024 / 1024 / 1024),
            PsuWatts = overrides.PsuWatts,
            CaseMaxGpuLengthMm = overrides.CaseMaxGpuLengthMm,
            CaseFormFactor = overrides.CaseFormFactor,
            FreeM2Slots = overrides.FreeM2Slots,
            SystemDriveKind = systemDrive?.Kind ?? StorageKind.Unknown,
            HasSsd = profile.Storage.Any(d => d.Kind is StorageKind.Nvme or StorageKind.SataSsd),
            HasNvme = profile.Storage.Any(d => d.Kind == StorageKind.Nvme),
            LargestFreeSpaceGb = profile.Storage.SelectMany(d => d.Volumes).Select(v => v.FreeBytes / 1e9).DefaultIfEmpty(0).Max(),
        };
    }

    private static (double Single, double Multi, bool FromBench) CpuScores(CpuInfo cpu, CpuTier? tier, BenchmarkRun? bench)
    {
        if (tier is not null) return (tier.SingleScore, tier.MultiScore, false);

        if (bench?.Get(BenchmarkKind.CpuSingleThread)?.Score is double s && bench.Get(BenchmarkKind.CpuMultiThread)?.Score is double m)
            return (s, m, true);

        // Very rough: scale a mid-2010s core by clock, then by thread count.
        var single = Math.Clamp(cpu.MaxClockMhz / 4600d * 75, 20, 120);
        var multi = single * Math.Max(cpu.Threads, 2) / 12d;
        return (single, multi, false);
    }

    private static (double Score, bool FromBench) GpuScore(GpuInfo? gpu, GpuTier? tier, BenchmarkRun? bench)
    {
        if (tier is not null) return (tier.Score, false);
        if (bench?.Get(BenchmarkKind.Gpu3D)?.Score is double g) return (g, true);
        if (gpu is null) return (0, false);
        return gpu.IsIntegrated ? (8, false) : (Math.Clamp(gpu.VramGb * 12, 20, 150), false);
    }

    [GeneratedRegex(@"^(AM\d\+?|LGA\s?\d{3,4}|FM\d\+?|SOCKET\s?AM\d)", RegexOptions.IgnoreCase)]
    private static partial Regex SocketPattern();
}
