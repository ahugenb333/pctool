using PcTool.Core.Models;

namespace PcTool.Core;

/// <summary>Rescales canonical tier/benchmark scores so the chosen preset scores 100 on CPU/GPU metrics.</summary>
public sealed class CompareScale
{
    public ComparePreset Preset { get; init; } = ComparePresets.Budget;
    public double CpuSingleRef { get; init; } = 100;
    public double CpuMultiRef { get; init; } = 100;
    public double CpuGamingRef { get; init; } = 100;
    public double GpuRef { get; init; } = 100;
    public string CpuName { get; init; } = "";
    public string GpuName { get; init; } = "";
    public int PresetGpuVramGb { get; init; }

    public string IndexCaption =>
        $"Scores compare your PC to the {Preset.Label} preset ({Preset.Summary}). That reference scores 100 on CPU and GPU rows.";

    public double IndexCpuSingle(double canonical) => Scale(canonical, CpuSingleRef);
    public double IndexCpuMulti(double canonical) => Scale(canonical, CpuMultiRef);
    public double IndexCpuGaming(double canonical) => Scale(canonical, CpuGamingRef);
    public double IndexGpu(double canonical) => Scale(canonical, GpuRef);

    public static CompareScale FromPreset(ComparePreset preset, IPerformanceTierSource tiers)
    {
        var cpu = tiers.GetCpu(preset.CpuTierId);
        var gpu = tiers.GetGpu(preset.GpuTierId);
        if (cpu is null || gpu is null)
            return new CompareScale { Preset = preset };

        return new CompareScale
        {
            Preset = preset,
            CpuSingleRef = cpu.SingleScore,
            CpuMultiRef = cpu.MultiScore,
            CpuGamingRef = cpu.GamingScore,
            GpuRef = gpu.Score,
            CpuName = cpu.Name,
            GpuName = gpu.Name,
            PresetGpuVramGb = gpu.VramGb,
        };
    }

    private static double Scale(double canonical, double reference) =>
        reference <= 0 ? canonical : canonical / reference * 100;
}
