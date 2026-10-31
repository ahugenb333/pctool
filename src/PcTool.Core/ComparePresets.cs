namespace PcTool.Core;

/// <summary>Reference gaming PCs for "compare against" score display. Engines keep canonical tier scores internally.</summary>
public sealed record ComparePreset(string Id, string Label, string CpuTierId, string GpuTierId, string Summary);

public static class ComparePresets
{
    public const string DefaultId = "budget";

    public static ComparePreset Potato { get; } = new(
        "potato", "Potato", "core-i5-8400", "gtx-1060-6gb", "Core i5-8400 + GTX 1060 6GB (1080p low)");

    public static ComparePreset Budget { get; } = new(
        "budget", "Budget", "ryzen-5-5600x", "rtx-3060", "Ryzen 5 5600X + RTX 3060 12GB");

    public static ComparePreset Beast { get; } = new(
        "beast", "Beast", "ryzen-7-7800x3d", "rtx-4080", "Ryzen 7 7800X3D + RTX 4080");

    public static IReadOnlyList<ComparePreset> All { get; } = [Potato, Budget, Beast];

    public static ComparePreset Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Budget;
}
