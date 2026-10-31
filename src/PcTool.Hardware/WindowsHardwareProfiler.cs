using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using Hardware.Info;
using PcTool.Core;
using PcTool.Core.Models;
using HiMemoryType = Hardware.Info.MemoryType;
using MemoryType = PcTool.Core.Models.MemoryType;

namespace PcTool.Hardware;

/// <summary>
/// Builds a <see cref="HardwareProfile"/> from Hardware.Info (WMI), DXGI (VRAM) and the Windows Storage WMI provider
/// (SSD/HDD/NVMe), since plain WMI caps VRAM at 4 GB and cannot tell SSDs from HDDs.
/// </summary>
public sealed partial class WindowsHardwareProfiler : IHardwareProfiler
{
    public Task<HardwareProfile> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Capture, cancellationToken);

    private static HardwareProfile Capture()
    {
        var hw = new HardwareInfo(TimeSpan.FromSeconds(15));
        hw.RefreshOperatingSystem();
        hw.RefreshMemoryStatus();
        hw.RefreshCPUList(includePercentProcessorTime: false, 0, includePerformanceCounter: false);
        hw.RefreshMemoryList();
        hw.RefreshVideoControllerList(refreshMonitorList: false);
        hw.RefreshMotherboardList();
        hw.RefreshBIOSList();

        return new HardwareProfile
        {
            Os = new OsInfo
            {
                Name = hw.OperatingSystem.Name?.Trim() ?? "Windows",
                Version = hw.OperatingSystem.VersionString ?? Environment.OSVersion.VersionString,
                Build = Environment.OSVersion.Version.Build,
            },
            Cpu = ReadCpu(hw),
            Gpus = ReadGpus(hw),
            Memory = ReadMemory(hw),
            Storage = ReadStorage(),
            Motherboard = ReadMotherboard(hw),
        };
    }

    private static CpuInfo ReadCpu(HardwareInfo hw)
    {
        var cpus = hw.CpuList;
        if (cpus.Count == 0) return new CpuInfo { Name = "Unknown CPU", Threads = Environment.ProcessorCount };
        var first = cpus[0];
        return new CpuInfo
        {
            Name = CollapseSpaces(first.Name),
            Manufacturer = first.Manufacturer?.Trim() ?? "",
            Cores = (int)cpus.Sum(c => c.NumberOfCores),
            Threads = (int)cpus.Sum(c => c.NumberOfLogicalProcessors),
            BaseClockMhz = (int)first.CurrentClockSpeed,
            MaxClockMhz = (int)first.MaxClockSpeed,
            L2CacheKb = (int)(first.L2CacheSize / 1024),
            L3CacheKb = (int)(first.L3CacheSize / 1024),
            Socket = first.SocketDesignation?.Trim() ?? "",
        };
    }

    private static List<GpuInfo> ReadGpus(HardwareInfo hw)
    {
        var adapters = DxgiAdapters.Enumerate();
        var result = new List<GpuInfo>();

        foreach (var vc in hw.VideoControllerList)
        {
            var name = CollapseSpaces(vc.Name);
            if (name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
                continue;

            var dxgi = adapters.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                       ?? adapters.FirstOrDefault(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                                                      name.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
            var vram = dxgi?.DedicatedVideoMemory ?? vc.AdapterRAM;
            var vendor = dxgi is not null ? VendorFromPciId(dxgi.VendorId) : VendorFromName(name);

            result.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                VramBytes = vram,
                SharedMemoryBytes = dxgi?.SharedSystemMemory ?? 0,
                IsIntegrated = vram < 1UL << 30 || LooksIntegrated(name),
                DriverVersion = vc.DriverVersion ?? "",
                DriverDate = ParseWmiDate(vc.DriverDate),
                CurrentWidth = (int)vc.CurrentHorizontalResolution,
                CurrentHeight = (int)vc.CurrentVerticalResolution,
                RefreshRate = (int)vc.CurrentRefreshRate,
            });
        }

        // WMI can miss adapters (e.g. some hybrid laptops); fall back to whatever DXGI reports.
        foreach (var a in adapters.Where(a => result.All(g => !g.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))))
        {
            result.Add(new GpuInfo
            {
                Name = a.Name,
                Vendor = VendorFromPciId(a.VendorId),
                VramBytes = a.DedicatedVideoMemory,
                SharedMemoryBytes = a.SharedSystemMemory,
                IsIntegrated = a.DedicatedVideoMemory < 1UL << 30 || LooksIntegrated(a.Name),
            });
        }

        return result;
    }

    private static MemoryInfo ReadMemory(HardwareInfo hw)
    {
        var modules = hw.MemoryList.Select(m => new MemoryModule
        {
            BankLabel = m.BankLabel?.Trim() ?? "",
            CapacityBytes = m.Capacity,
            SpeedMtps = (int)m.Speed,
            Manufacturer = m.Manufacturer?.Trim() ?? "",
            PartNumber = m.PartNumber?.Trim() ?? "",
        }).ToList();

        var type = hw.MemoryList.Select(m => MapMemoryType(m.MemoryType)).FirstOrDefault(t => t != MemoryType.Unknown);

        int slots = 0;
        ulong maxCapacity = 0;
        foreach (var array in Query(@"root\cimv2", "SELECT MemoryDevices, MaxCapacityEx, Use FROM Win32_PhysicalMemoryArray"))
        {
            if (Convert.ToInt32(array["Use"] ?? 3) != 3) continue;
            slots += Convert.ToInt32(array["MemoryDevices"] ?? 0);
            maxCapacity += Convert.ToUInt64(array["MaxCapacityEx"] ?? 0UL) * 1024;
        }

        return new MemoryInfo
        {
            TotalBytes = hw.MemoryStatus.TotalPhysical,
            AvailableBytes = hw.MemoryStatus.AvailablePhysical,
            Type = type,
            SpeedMtps = modules.Count > 0 ? modules.Max(m => m.SpeedMtps) : 0,
            SlotCount = Math.Max(slots, modules.Count),
            MaxCapacityBytes = maxCapacity,
            Modules = modules,
        };
    }

    private static List<StorageInfo> ReadStorage()
    {
        var lettersByDisk = new Dictionary<string, List<string>>();
        foreach (var p in Query(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            var letter = p["DriveLetter"] is char c && c != '\0' ? c.ToString() :
                         p["DriveLetter"] is ushort u && u != 0 ? ((char)u).ToString() : null;
            if (letter is null) continue;
            var disk = p["DiskNumber"]?.ToString() ?? "";
            if (!lettersByDisk.TryGetValue(disk, out var list)) lettersByDisk[disk] = list = [];
            list.Add(letter);
        }

        var systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?[..1] ?? "C";
        var result = new List<StorageInfo>();

        foreach (var d in Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk"))
        {
            var id = d["DeviceId"]?.ToString() ?? "";
            var media = Convert.ToInt32(d["MediaType"] ?? 0);
            var bus = Convert.ToInt32(d["BusType"] ?? 0);
            var letters = lettersByDisk.GetValueOrDefault(id) ?? [];

            var kind = bus == 17 ? StorageKind.Nvme
                : media == 4 ? StorageKind.SataSsd
                : media == 3 ? StorageKind.Hdd
                : StorageKind.Unknown;

            result.Add(new StorageInfo
            {
                Model = CollapseSpaces(d["FriendlyName"]?.ToString()),
                SizeBytes = Convert.ToUInt64(d["Size"] ?? 0UL),
                Kind = kind,
                BusType = BusName(bus),
                IsSystemDrive = letters.Contains(systemLetter, StringComparer.OrdinalIgnoreCase),
                Volumes = letters.Select(VolumeFor).Where(v => v is not null).Select(v => v!).ToList(),
            });
        }

        return result.OrderByDescending(s => s.IsSystemDrive).ToList();
    }

    private static VolumeInfo? VolumeFor(string letter)
    {
        try
        {
            var di = new DriveInfo(letter);
            return di.IsReady ? new VolumeInfo { Letter = letter, SizeBytes = (ulong)di.TotalSize, FreeBytes = (ulong)di.AvailableFreeSpace } : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static MotherboardInfo ReadMotherboard(HardwareInfo hw)
    {
        var board = hw.MotherboardList.FirstOrDefault();
        var bios = hw.BiosList.FirstOrDefault();
        var product = CollapseSpaces(board?.Product);
        return new MotherboardInfo
        {
            Manufacturer = CollapseSpaces(board?.Manufacturer),
            Product = product,
            Chipset = ChipsetPattern().Match(product.ToUpperInvariant()) is { Success: true } m ? m.Groups[1].Value : "",
            BiosVendor = bios?.Manufacturer?.Trim() ?? "",
            // Win32_BIOS.Version is often an OEM tag like "_ASUS_ - 1072009"; SMBIOSBIOSVersion is what the vendor publishes.
            BiosVersion = Query(@"root\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS")
                .Select(o => o["SMBIOSBIOSVersion"]?.ToString()?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v))
                ?? bios?.Version?.Trim() ?? "",
            BiosDate = ParseWmiDate(bios?.ReleaseDate),
        };
    }

    private static IEnumerable<ManagementBaseObject> Query(string scope, string wql)
    {
        var items = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            foreach (var o in searcher.Get()) items.Add(o);
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return items;
    }

    internal static DateTime? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length >= 14 && char.IsDigit(value[0]))
        {
            try { return ManagementDateTimeConverter.ToDateTime(value); }
            catch (ArgumentOutOfRangeException) { }
            if (DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        }
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static MemoryType MapMemoryType(HiMemoryType t) => t switch
    {
        HiMemoryType.DDR3 => MemoryType.DDR3,
        HiMemoryType.DDR4 => MemoryType.DDR4,
        HiMemoryType.DDR5 => MemoryType.DDR5,
        HiMemoryType.LPDDR4 => MemoryType.LPDDR4,
        HiMemoryType.LPDDR5 => MemoryType.LPDDR5,
        _ => MemoryType.Unknown,
    };

    private static GpuVendor VendorFromPciId(uint id) => id switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        _ => GpuVendor.Other,
    };

    private static GpuVendor VendorFromName(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia :
        name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd :
        name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel : GpuVendor.Unknown;

    private static bool LooksIntegrated(string name) =>
        IntegratedPattern().IsMatch(name);

    private static string BusName(int bus) => bus switch
    {
        17 => "NVMe",
        11 => "SATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        _ => "Other",
    };

    private static string CollapseSpaces(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : Whitespace().Replace(s.Trim(), " ");

    [GeneratedRegex(@"(?<![A-Z0-9])([ABHQXZ]\d{3}E?)(?![0-9])")]
    private static partial Regex ChipsetPattern();

    [GeneratedRegex(@"UHD Graphics|HD Graphics|Iris|Radeon\(TM\) Graphics|Radeon Graphics|Vega \d+ Graphics|Radeon \d{3}M|Arc\(TM\) Graphics", RegexOptions.IgnoreCase)]
    private static partial Regex IntegratedPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
