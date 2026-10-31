using PcTool.Core.Models;
using PcTool.Data;

namespace PcTool.Logic.Tests;

public class DataIntegrityTests
{
    private static EmbeddedCatalogs C => TestSystems.Catalogs;

    [Fact]
    public void Every_game_requirement_points_at_a_known_tier()
    {
        var missing = C.Games.Games
            .SelectMany(g => new[] { (g.Name, g.Minimum.Cpu, true), (g.Name, g.Recommended.Cpu, true), (g.Name, g.Minimum.Gpu, false), (g.Name, g.Recommended.Gpu, false) })
            .Where(x => x.Item3 ? C.Tiers.GetCpu(x.Item2) is null : C.Tiers.GetGpu(x.Item2) is null)
            .Select(x => $"{x.Item1}: {x.Item2}")
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Game_recommended_spec_is_not_below_minimum()
    {
        foreach (var g in C.Games.Games)
        {
            Assert.True(C.Tiers.GetGpu(g.Recommended.Gpu)!.Score >= C.Tiers.GetGpu(g.Minimum.Gpu)!.Score, $"{g.Name} GPU");
            Assert.True(C.Tiers.GetCpu(g.Recommended.Cpu)!.GamingScore >= C.Tiers.GetCpu(g.Minimum.Cpu)!.GamingScore * 0.95, $"{g.Name} CPU");
            Assert.True(g.Recommended.RamGb >= g.Minimum.RamGb, $"{g.Name} RAM");
        }
    }

    [Fact]
    public void Every_cpu_and_gpu_part_maps_to_a_tier()
    {
        var unmatched = C.Parts.Parts
            .Where(p => p is CpuPart { TierId: null } or GpuPart { TierId: null })
            .Select(p => p.DisplayName)
            .ToList();
        Assert.Empty(unmatched);
    }

    [Fact]
    public void Every_part_has_a_reference_price()
    {
        var unpriced = C.Parts.Parts.Where(p => C.Prices.GetPriceUsd(p.Id) is null).Select(p => p.Id).ToList();
        Assert.Empty(unpriced);
    }

    [Fact]
    public void Board_and_cpu_chipsets_are_in_the_chipset_table()
    {
        var names = C.Parts.Parts.OfType<MotherboardPart>().Select(b => b.Chipset)
            .Concat(C.Parts.Parts.OfType<CpuPart>().SelectMany(c => c.SupportedChipsets))
            .Distinct()
            .Where(n => C.Chipsets.Find(n) is null)
            .ToList();
        Assert.Empty(names);
    }

    [Fact]
    public void Tier_ids_are_unique()
    {
        Assert.Equal(C.Tiers.Cpus.Count, C.Tiers.Cpus.Select(c => c.Id).Distinct().Count());
        Assert.Equal(C.Tiers.Gpus.Count, C.Tiers.Gpus.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Chipset_families_exist_in_cpu_tiers()
    {
        var families = C.Tiers.Cpus.Select(c => c.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = C.Chipsets.All
            .SelectMany(c => c.BiosUpdateFamilies.Concat(c.UnsupportedFamilies))
            .Distinct()
            .Where(f => !families.Contains(f))
            .ToList();
        Assert.Empty(unknown);
    }
}
