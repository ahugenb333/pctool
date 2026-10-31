using PcTool.Core.Models;
using PcTool.Data;

namespace PcTool.Logic.Tests;

/// <summary>Builds <see cref="SystemSpecs"/> from tier ids so tests read like "a 5600X + RTX 3060 on B450".</summary>
internal static class TestSystems
{
    public static EmbeddedCatalogs Catalogs => EmbeddedCatalogs.Load();

    public static SystemSpecs Desktop(string cpuId, string gpuId, string? chipset = null, int ramGb = 16,
        MemoryType mem = MemoryType.DDR4, int? psuWatts = null, int ramSlots = 4, int ramSlotsUsed = 2,
        StorageKind drive = StorageKind.Nvme, string? caseFf = null, int? caseGpuMm = null, int? freeM2 = null)
    {
        var cpu = Catalogs.Tiers.GetCpu(cpuId) ?? throw new ArgumentException($"no cpu tier {cpuId}");
        var gpu = Catalogs.Tiers.GetGpu(gpuId) ?? throw new ArgumentException($"no gpu tier {gpuId}");
        return new SystemSpecs
        {
            CpuTier = cpu, GpuTier = gpu, CpuName = cpu.Name, GpuName = gpu.Name,
            CpuSingleScore = cpu.SingleScore, CpuMultiScore = cpu.MultiScore, CpuTdpW = cpu.TdpW,
            GpuScore = gpu.Score, VramGb = gpu.VramGb, GpuIsIntegrated = gpu.IsIntegrated, GpuDirectX = gpu.DirectX,
            GpuRayTracing = gpu.RayTracing, GpuTdpW = gpu.TdpW,
            Socket = cpu.Socket, Chipset = Catalogs.Chipsets.Find(chipset), MemoryType = mem,
            RamGb = ramGb, RamSlots = ramSlots, RamSlotsUsed = ramSlotsUsed, MaxRamGb = 128,
            PsuWatts = psuWatts, CaseFormFactor = caseFf, CaseMaxGpuLengthMm = caseGpuMm, FreeM2Slots = freeM2,
            SystemDriveKind = drive, HasSsd = drive != StorageKind.Hdd, HasNvme = drive == StorageKind.Nvme,
            LargestFreeSpaceGb = 500,
        };
    }
}
