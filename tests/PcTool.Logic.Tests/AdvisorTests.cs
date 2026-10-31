using PcTool.Compatibility;
using PcTool.Core.Models;
using PcTool.Games;
using PcTool.Upgrades;

namespace PcTool.Logic.Tests;

public class AdvisorTests
{
    private static readonly GameAdvisor Games = new(TestSystems.Catalogs.Games, TestSystems.Catalogs.Tiers);

    private static readonly UpgradeAdvisor Upgrades = new(
        TestSystems.Catalogs.Parts, TestSystems.Catalogs.Prices, TestSystems.Catalogs.Games,
        new CompatibilityEngine(TestSystems.Catalogs.Tiers, TestSystems.Catalogs.Chipsets), Games);

    private static GameEntry Game(string id) => TestSystems.Catalogs.Games.Get(id) ?? throw new ArgumentException(id);

    [Fact]
    public void High_end_pc_runs_everything_at_least_at_recommended()
    {
        var s = TestSystems.Desktop("ryzen-7-9800x3d", "rtx-5090", ramGb: 64, mem: MemoryType.DDR5);
        var below = Games.EvaluateAll(s).Where(v => v.Level < GameVerdictLevel.Recommended).Select(v => v.Game.Name).ToList();
        Assert.Empty(below);
    }

    [Fact]
    public void Old_pc_is_below_minimum_for_new_aaa_games()
    {
        var s = TestSystems.Desktop("core-i5-4460", "gtx-750-ti", ramGb: 8, mem: MemoryType.DDR3, drive: StorageKind.Hdd);
        Assert.Equal(GameVerdictLevel.BelowMinimum, Games.Evaluate(Game("cyberpunk-2077"), s).Level);
    }

    [Fact]
    public void Ray_tracing_required_games_reject_non_rt_gpus()
    {
        var rtGame = TestSystems.Catalogs.Games.Games.FirstOrDefault(g => g.RayTracingRequired);
        Assert.NotNull(rtGame);
        var s = TestSystems.Desktop("ryzen-7-7800x3d", "gtx-1080-ti", ramGb: 32, mem: MemoryType.DDR5);
        Assert.Equal(GameVerdictLevel.BelowMinimum, Games.Evaluate(rtGame!, s).Level);
    }

    [Fact]
    public void Ssd_required_games_are_capped_on_a_hard_drive()
    {
        var ssdGame = TestSystems.Catalogs.Games.Games.First(g => g.SsdRequired);
        var s = TestSystems.Desktop("ryzen-7-9800x3d", "rtx-5090", ramGb: 64, mem: MemoryType.DDR5, drive: StorageKind.Hdd);
        Assert.True(Games.Evaluate(ssdGame, s).Level <= GameVerdictLevel.Minimum);
    }

    [Fact]
    public void Gpu_bottlenecked_system_gets_a_gpu_as_the_best_value_pick()
    {
        var s = TestSystems.Desktop("ryzen-7-5800x3d", "gtx-1660-super", chipset: "B550", ramGb: 32, psuWatts: 750);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1440p, 800));
        Assert.Equal("GPU", plan.Bottleneck.Component);
        Assert.Equal(PartCategory.Gpu, plan.Good?.Category);
    }

    [Fact]
    public void Cpu_bottlenecked_am4_system_is_offered_a_drop_in_cpu()
    {
        var s = TestSystems.Desktop("ryzen-5-1600", "rtx-4070-super", chipset: "B450", ramGb: 16, psuWatts: 750);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1080p, 500));
        Assert.Equal("CPU", plan.Bottleneck.Component);
        Assert.Contains(plan.Candidates, c => c.Category == PartCategory.Cpu && c.Parts.Count == 1 && ((CpuPart)c.Parts[0]).Socket == "AM4");
    }

    [Fact]
    public void Recommendations_respect_budget_and_never_include_incompatible_parts()
    {
        var s = TestSystems.Desktop("ryzen-5-3600", "rtx-2060", chipset: "B450", psuWatts: 550);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1440p, 400));
        Assert.NotEmpty(plan.Candidates);
        Assert.All(plan.Candidates, c =>
        {
            Assert.NotEqual(CheckStatus.Fail, c.Overall);
            Assert.True(c.PriceUsd is null || c.PriceUsd <= 400);
        });
    }

    [Fact]
    public void Ddr4_platform_jump_to_am5_bundles_ddr5_memory()
    {
        var s = TestSystems.Desktop("ryzen-5-1600", "rtx-4070-super", chipset: "B350", ramGb: 16, psuWatts: 750);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1440p, 2000));
        var platform = plan.Candidates.FirstOrDefault(c => c.Parts.OfType<CpuPart>().Any(p => p.Socket == "AM5"));
        Assert.NotNull(platform);
        Assert.Contains(platform!.Parts, p => p is MotherboardPart);
        Assert.Contains(platform.Parts, p => p is RamPart r && r.RamType.Contains("DDR5"));
    }

    [Fact]
    public void Laptops_only_get_ram_and_storage_suggestions()
    {
        var s = TestSystems.Desktop("ryzen-7-7840hs", "radeon-780m", ramGb: 8, mem: MemoryType.DDR5, drive: StorageKind.Hdd);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1080p, 2000));
        Assert.DoesNotContain(plan.Candidates, c => c.Category is PartCategory.Cpu or PartCategory.Gpu);
    }

    [Fact]
    public void Hdd_system_drive_gets_an_ssd_suggestion()
    {
        var s = TestSystems.Desktop("ryzen-5-5600x", "rtx-3060", chipset: "B550", drive: StorageKind.Hdd, freeM2: 1);
        var plan = Upgrades.Recommend(s, new UpgradeOptions(UpgradeGoal.Gaming1080p, 300));
        Assert.Contains(plan.Candidates, c => c.Category == PartCategory.Storage);
    }
}
