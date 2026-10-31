namespace PcTool.Core.Models;

/// <summary>
/// Relative CPU performance. Scores are indexed so a Ryzen 5 5600X = 100 for both single- and multi-thread.
/// </summary>
public sealed class CpuTier
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Vendor { get; init; } = "";
    public string Family { get; init; } = "";
    public string Socket { get; init; } = "";
    public string[] MemoryTypes { get; init; } = [];
    public int Cores { get; init; }
    public int Threads { get; init; }
    public int TdpW { get; init; }
    public double SingleScore { get; init; }
    public double MultiScore { get; init; }
    public int Year { get; init; }
    public bool IsLaptop { get; init; }
    /// <summary>Multiplier for large-cache parts (X3D) whose gaming results beat their raw scores.</summary>
    public double GamingFactor { get; init; } = 1;

    /// <summary>Games lean on single-thread speed, with multi-thread mattering less.</summary>
    public double GamingScore => GamingScoreOf(SingleScore, MultiScore, GamingFactor);

    public static double GamingScoreOf(double single, double multi, double factor = 1) =>
        (single * 0.7 + Math.Min(multi, single * 2.5) * 0.3) * factor;
}

/// <summary>
/// Relative GPU raster performance at 1080p/1440p. Indexed so an RTX 3060 12GB = 100.
/// </summary>
public sealed class GpuTier
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Vendor { get; init; } = "";
    public double Score { get; init; }
    public int VramGb { get; init; }
    public int TdpW { get; init; }
    public bool RayTracing { get; init; }
    public string Upscaling { get; init; } = "";
    /// <summary>Highest Direct3D feature level, e.g. "12_2", "12_1", "11_0".</summary>
    public string DirectX { get; init; } = "12_1";
    public int Year { get; init; }
    public bool IsIntegrated { get; init; }
    public bool IsLaptop { get; init; }
}

public sealed class ChipsetInfo
{
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Socket { get; init; } = "";
    public string[] MemoryTypes { get; init; } = [];
    public int PcieGen { get; init; }
    /// <summary>CPU families that run on this chipset only after a BIOS update.</summary>
    public string[] BiosUpdateFamilies { get; init; } = [];
    /// <summary>CPU families on the same socket that this chipset does not support at all.</summary>
    public string[] UnsupportedFamilies { get; init; } = [];
}
