using PcTool.Core;

namespace PcTool.Logic.Tests;

public sealed class CompareScaleTests
{
    private static readonly Core.IPerformanceTierSource Tiers = TestSystems.Catalogs.Tiers;

    [Fact]
    public void Budget_preset_is_identity_for_reference_tiers()
    {
        var scale = CompareScale.FromPreset(ComparePresets.Budget, Tiers);
        Assert.Equal(100, scale.IndexCpuSingle(100));
        Assert.Equal(100, scale.IndexGpu(100));
    }

    [Fact]
    public void Beast_preset_scales_midrange_gpu_down()
    {
        var scale = CompareScale.FromPreset(ComparePresets.Beast, Tiers);
        var rtx3060 = Tiers.GetGpu("rtx-3060")!.Score;
        Assert.InRange(scale.IndexGpu(rtx3060), 35, 38);
    }

    [Fact]
    public void All_presets_resolve_tier_ids()
    {
        foreach (var preset in ComparePresets.All)
        {
            var scale = CompareScale.FromPreset(preset, Tiers);
            Assert.True(scale.CpuSingleRef > 0);
            Assert.True(scale.GpuRef > 0);
        }
    }

    [Fact]
    public void Potato_preset_scales_strong_cpu_up()
    {
        var scale = CompareScale.FromPreset(ComparePresets.Potato, Tiers);
        var cpu5800 = Tiers.GetCpu("ryzen-5-5600x")!.SingleScore;
        Assert.True(scale.IndexCpuSingle(cpu5800) > 140);
    }
}
