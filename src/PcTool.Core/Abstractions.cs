using PcTool.Core.Models;

namespace PcTool.Core;

public interface IHardwareProfiler
{
    Task<HardwareProfile> CaptureAsync(CancellationToken cancellationToken = default);
}

/// <summary>Live sensor readings. Most temperature and clock sensors need the app to run elevated.</summary>
public interface ISensorMonitor : IDisposable
{
    bool IsElevated { get; }
    IReadOnlyList<SensorReading> Read();
}

public interface IPerformanceTierSource
{
    IReadOnlyList<CpuTier> Cpus { get; }
    IReadOnlyList<GpuTier> Gpus { get; }
    CpuTier? GetCpu(string id);
    GpuTier? GetGpu(string id);
    /// <summary>Matches a detected or catalog name such as "AMD Ryzen 7 5800X 8-Core Processor".</summary>
    CpuTier? FindCpu(string name);
    GpuTier? FindGpu(string name);
}

public interface IChipsetCatalog
{
    IReadOnlyList<ChipsetInfo> All { get; }
    ChipsetInfo? Find(string? name);
    /// <summary>Pulls a chipset out of a board name like "ROG STRIX B550-F GAMING".</summary>
    string? DetectFromBoardName(string? product);
}

public interface IGameCatalog
{
    IReadOnlyList<GameEntry> Games { get; }
    GameEntry? Get(string id);
}

public interface IPartsCatalog
{
    IReadOnlyList<Part> Parts { get; }
    IReadOnlyList<DataAttribution> Attributions { get; }
    Part? Get(string id);
    IEnumerable<T> OfType<T>() where T : Part => Parts.OfType<T>();
}

public interface IPriceSource
{
    string Description { get; }
    decimal? GetPriceUsd(string partId);
}
