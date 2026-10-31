using System.Text.Json.Serialization;

namespace PcTool.Core.Models;

public sealed class HardwareProfile
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public OsInfo Os { get; init; } = new();
    public CpuInfo Cpu { get; init; } = new();
    public List<GpuInfo> Gpus { get; init; } = [];
    public MemoryInfo Memory { get; init; } = new();
    public List<StorageInfo> Storage { get; init; } = [];
    public MotherboardInfo Motherboard { get; init; } = new();

    /// <summary>The GPU games will run on: the discrete adapter with the most VRAM, else the first adapter.</summary>
    [JsonIgnore]
    public GpuInfo? PrimaryGpu =>
        Gpus.Where(g => !g.IsIntegrated).OrderByDescending(g => g.VramBytes).FirstOrDefault()
        ?? Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();

    [JsonIgnore]
    public StorageInfo? SystemDrive => Storage.FirstOrDefault(s => s.IsSystemDrive) ?? Storage.FirstOrDefault();
}

public sealed class OsInfo
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public int Build { get; init; }
    public bool Is64Bit { get; init; } = Environment.Is64BitOperatingSystem;
}

public sealed class CpuInfo
{
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public int BaseClockMhz { get; init; }
    public int MaxClockMhz { get; init; }
    public int L2CacheKb { get; init; }
    public int L3CacheKb { get; init; }
    public string Socket { get; init; } = "";
}

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Other }

public sealed class GpuInfo
{
    public string Name { get; init; } = "";
    public GpuVendor Vendor { get; init; }
    public ulong VramBytes { get; init; }
    public ulong SharedMemoryBytes { get; init; }
    public bool IsIntegrated { get; init; }
    public string DriverVersion { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public int CurrentWidth { get; init; }
    public int CurrentHeight { get; init; }
    public int RefreshRate { get; init; }

    /// <summary>Marketed size: DXGI reports an 8 GB card as ~7.8 GiB, so round up when within a quarter GiB.</summary>
    [JsonIgnore]
    public double VramGb => VramBytes == 0 ? 0 : Math.Max(Math.Ceiling(VramBytes / 1073741824d - 0.25), 0.5);
}

public enum MemoryType { Unknown, DDR3, DDR4, DDR5, LPDDR4, LPDDR5 }

public sealed class MemoryInfo
{
    public ulong TotalBytes { get; init; }
    public ulong AvailableBytes { get; init; }
    public MemoryType Type { get; init; }
    public int SpeedMtps { get; init; }
    public int SlotCount { get; init; }
    public ulong MaxCapacityBytes { get; init; }
    public List<MemoryModule> Modules { get; init; } = [];

    [JsonIgnore]
    public double TotalGb => TotalBytes / 1024d / 1024 / 1024;
    [JsonIgnore]
    public int UsedSlots => Modules.Count;
}

public sealed class MemoryModule
{
    public string BankLabel { get; init; } = "";
    public ulong CapacityBytes { get; init; }
    public int SpeedMtps { get; init; }
    public string Manufacturer { get; init; } = "";
    public string PartNumber { get; init; } = "";
}

public enum StorageKind { Unknown, Hdd, SataSsd, Nvme }

public sealed class StorageInfo
{
    public string Model { get; init; } = "";
    public ulong SizeBytes { get; init; }
    public StorageKind Kind { get; init; }
    public string BusType { get; init; } = "";
    public bool IsSystemDrive { get; init; }
    public List<VolumeInfo> Volumes { get; init; } = [];

    [JsonIgnore]
    public double SizeGb => SizeBytes / 1e9;
}

public sealed class VolumeInfo
{
    public string Letter { get; init; } = "";
    public ulong SizeBytes { get; init; }
    public ulong FreeBytes { get; init; }
}

public sealed class MotherboardInfo
{
    public string Manufacturer { get; init; } = "";
    public string Product { get; init; } = "";
    /// <summary>Chipset parsed from the product name; empty when the board name does not reveal it (common on prebuilts).</summary>
    public string Chipset { get; init; } = "";
    public string BiosVendor { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public DateTime? BiosDate { get; init; }
}
