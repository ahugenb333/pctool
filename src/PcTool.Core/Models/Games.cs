namespace PcTool.Core.Models;

public sealed class GameEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int? SteamAppId { get; init; }
    public int Year { get; init; }
    public string[] Genres { get; init; } = [];
    public GameRequirements Minimum { get; init; } = new();
    public GameRequirements Recommended { get; init; } = new();
    public bool SsdRequired { get; init; }
    public bool RayTracingRequired { get; init; }
    /// <summary>Executable names (without path) used to match crash reports, e.g. "Cyberpunk2077.exe".</summary>
    public string[] Executables { get; init; } = [];
}

public sealed class GameRequirements
{
    /// <summary>Id into the CPU tier list.</summary>
    public string Cpu { get; init; } = "";
    /// <summary>Id into the GPU tier list.</summary>
    public string Gpu { get; init; } = "";
    public int RamGb { get; init; }
    public int VramGb { get; init; }
    public int StorageGb { get; init; }
    public int DirectX { get; init; } = 11;
}

public enum GameVerdictLevel { BelowMinimum, Minimum, Recommended, RecommendedPlus }

public sealed record GameVerdict(
    GameEntry Game,
    GameVerdictLevel Level,
    string Bottleneck,
    string SuggestedSettings,
    IReadOnlyList<string> Notes,
    double GpuRatio,
    double CpuRatio);
