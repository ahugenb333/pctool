using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Games;

/// <summary>
/// Scores a system against each game's minimum and recommended specs. CPU comparisons use the gaming score
/// (single-thread weighted); GPU comparisons use the raster index. Both come from the same tier database.
/// </summary>
public sealed class GameAdvisor(IGameCatalog catalog, IPerformanceTierSource tiers)
{
    /// <summary>A part within 8% of the listed requirement counts as meeting it; publisher specs are not that precise.</summary>
    private const double Tolerance = 0.92;

    public IReadOnlyList<GameEntry> Games => catalog.Games;

    public IReadOnlyList<string> Genres =>
        catalog.Games.SelectMany(g => g.Genres).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

    public IReadOnlyList<GameVerdict> EvaluateAll(SystemSpecs specs) =>
        catalog.Games.Select(g => Evaluate(g, specs)).ToList();

    public IReadOnlyList<GameVerdict> Search(string query, SystemSpecs specs) =>
        catalog.Games
            .Where(g => string.IsNullOrWhiteSpace(query) || g.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(g => Evaluate(g, specs))
            .ToList();

    public IReadOnlyList<GameVerdict> RunsWell(SystemSpecs specs, string? genre = null) =>
        EvaluateAll(specs)
            .Where(v => v.Level >= GameVerdictLevel.Recommended)
            .Where(v => string.IsNullOrEmpty(genre) || v.Game.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Level)
            .ThenByDescending(v => v.Game.Year)
            .ToList();

    public GameEntry? FindByExecutable(string exeName) =>
        catalog.Games.FirstOrDefault(g => g.Executables.Any(e => e.Equals(exeName, StringComparison.OrdinalIgnoreCase)));

    public GameVerdict Evaluate(GameEntry game, SystemSpecs s)
    {
        var notes = new List<string>();

        var cpu = s.CpuGamingScore;
        var cpuMin = Ratio(cpu, tiers.GetCpu(game.Minimum.Cpu)?.GamingScore);
        var cpuRec = Ratio(cpu, tiers.GetCpu(game.Recommended.Cpu)?.GamingScore);
        var gpuMin = Ratio(s.GpuScore, tiers.GetGpu(game.Minimum.Gpu)?.Score);
        var gpuRec = Ratio(s.GpuScore, tiers.GetGpu(game.Recommended.Gpu)?.Score);
        var ramMin = Ratio(s.RamGb, game.Minimum.RamGb);
        var ramRec = Ratio(s.RamGb, game.Recommended.RamGb);

        // Integrated GPUs borrow system RAM, so a dedicated-VRAM requirement does not apply to them directly.
        var vramMin = s.GpuIsIntegrated ? 1 : Ratio(s.VramGb, game.Minimum.VramGb);
        var vramRec = s.GpuIsIntegrated ? 1 : Ratio(s.VramGb, game.Recommended.VramGb);

        var rtOk = !game.RayTracingRequired || s.GpuRayTracing;
        if (!rtOk) notes.Add("Requires a ray-tracing capable GPU (RTX 20-series, RX 6000-series, Arc or newer).");

        var meetsMin = cpuMin >= Tolerance && gpuMin >= Tolerance && ramMin >= 1 && vramMin >= Tolerance && rtOk;
        var meetsRec = meetsMin && cpuRec >= Tolerance && gpuRec >= Tolerance && ramRec >= 1 && vramRec >= Tolerance;

        var level = !meetsMin ? GameVerdictLevel.BelowMinimum
            : !meetsRec ? GameVerdictLevel.Minimum
            : gpuRec >= 1.6 && cpuRec >= 1.15 && vramRec >= 1 ? GameVerdictLevel.RecommendedPlus
            : GameVerdictLevel.Recommended;

        if (game.SsdRequired && !s.HasSsd)
        {
            notes.Add("Needs an SSD; expect long loads and stutter from a hard drive.");
            if (level > GameVerdictLevel.Minimum) level = GameVerdictLevel.Minimum;
        }

        if (game.Recommended.DirectX >= 12 && s.GpuDirectX.StartsWith("11", StringComparison.Ordinal))
            notes.Add("Your GPU only supports Direct3D feature level 11; this DirectX 12 game may not start.");

        if (s.LargestFreeSpaceGb > 0 && s.LargestFreeSpaceGb < game.Recommended.StorageGb)
            notes.Add($"Needs {game.Recommended.StorageGb} GB free; your largest drive has {s.LargestFreeSpaceGb:F0} GB free.");

        var bottleneck = Bottleneck(game, cpuRec, gpuRec, ramRec, vramRec, s);
        var settings = SuggestSettings(level, gpuMin, gpuRec, cpuRec, s, notes);

        return new GameVerdict(game, level, bottleneck, settings, notes, Math.Round(gpuRec, 2), Math.Round(cpuRec, 2));
    }

    private static double Ratio(double have, double? need) =>
        need is null or <= 0 ? 10 : have / need.Value;

    private static string Bottleneck(GameEntry game, double cpu, double gpu, double ram, double vram, SystemSpecs s)
    {
        var limits = new List<(double Ratio, string Label)>
        {
            (gpu, "GPU limited"),
            (cpu, "CPU limited"),
            (ram, $"Needs {game.Recommended.RamGb} GB RAM"),
        };
        if (!s.GpuIsIntegrated && game.Recommended.VramGb > 0) limits.Add((vram, $"Needs {game.Recommended.VramGb} GB VRAM"));

        var worst = limits.MinBy(l => l.Ratio);
        if (worst.Ratio < Tolerance) return worst.Label;
        if (Math.Min(cpu, gpu) >= 1.5) return "Plenty of headroom";
        return gpu <= cpu ? "Balanced - GPU sets the frame-rate ceiling" : "Balanced - CPU sets the frame-rate ceiling";
    }

    private static string SuggestSettings(GameVerdictLevel level, double gpuMin, double gpuRec, double cpuRec, SystemSpecs s, List<string> notes)
    {
        string preset;
        if (level == GameVerdictLevel.BelowMinimum)
        {
            preset = gpuMin >= 0.7 ? "720p Low - expect stutter" : "Not playable";
        }
        else
        {
            preset = gpuRec switch
            {
                < 0.6 => "1080p Low",
                < 0.85 => "1080p Medium",
                < 1.25 => "1080p High",
                < 1.7 => "1440p High",
                < 2.4 => "1440p Ultra",
                _ => "4K High",
            };
        }

        if (level != GameVerdictLevel.BelowMinimum && cpuRec < Tolerance)
            notes.Add("Your CPU is below the recommended spec; frame rates may dip in busy scenes regardless of settings.");

        var upscaler = s.GpuTier?.Upscaling;
        if (level != GameVerdictLevel.BelowMinimum && gpuRec < 1.25 && !string.IsNullOrEmpty(upscaler))
            notes.Add($"Turn on {upscaler} (quality mode) for extra frames at this preset.");

        return preset;
    }
}
