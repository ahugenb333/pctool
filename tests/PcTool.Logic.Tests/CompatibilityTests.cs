using PcTool.Compatibility;
using PcTool.Core.Models;

namespace PcTool.Logic.Tests;

public class CompatibilityTests
{
    private static readonly CompatibilityEngine Engine = new(TestSystems.Catalogs.Tiers, TestSystems.Catalogs.Chipsets);

    private static T Part<T>(Func<T, bool> where) where T : Part => TestSystems.Catalogs.Parts.Parts.OfType<T>().First(where);

    private static CheckStatus StatusOf(CompatibilityReport r, string rule) => r.Checks.First(c => c.Rule == rule).Status;

    [Fact]
    public void Am5_cpu_does_not_fit_an_am4_board()
    {
        var s = TestSystems.Desktop("ryzen-5-3600", "rtx-3060", chipset: "B450");
        var report = Engine.Check(Part<CpuPart>(c => c.Socket == "AM5"), s);
        Assert.Equal(CheckStatus.Fail, report.Overall);
        Assert.Equal(CheckStatus.Fail, StatusOf(report, "Socket"));
    }

    [Fact]
    public void Ryzen_5000_on_b450_needs_a_bios_update()
    {
        var s = TestSystems.Desktop("ryzen-5-3600", "rtx-3060", chipset: "B450");
        var report = Engine.Check(Part<CpuPart>(c => c.Model == "Ryzen 7 5800X3D"), s);
        Assert.Equal(CheckStatus.Warn, StatusOf(report, "Chipset"));
        Assert.True(report.Fits);
    }

    [Fact]
    public void Unknown_chipset_is_reported_as_unverified_not_failed()
    {
        var s = TestSystems.Desktop("ryzen-5-3600", "rtx-3060");
        var report = Engine.Check(Part<CpuPart>(c => c.Model == "Ryzen 7 5800X3D"), s);
        Assert.Equal(CheckStatus.Unknown, StatusOf(report, "Chipset"));
    }

    [Fact]
    public void Ddr5_kit_does_not_fit_a_ddr4_board()
    {
        var s = TestSystems.Desktop("ryzen-5-5600x", "rtx-3060");
        var report = Engine.Check(Part<RamPart>(r => r.RamType.Contains("DDR5")), s);
        Assert.Equal(CheckStatus.Fail, StatusOf(report, "Type"));
    }

    [Fact]
    public void Psu_rule_uses_tdp_plus_150_with_20_percent_headroom()
    {
        Assert.Equal(550, CompatibilityEngine.RecommendedPsuWatts(65, 220));
        Assert.Equal(800, CompatibilityEngine.RecommendedPsuWatts(120, 360));
    }

    [Fact]
    public void Big_gpu_fails_on_small_psu_and_short_case()
    {
        var s = TestSystems.Desktop("ryzen-5-5600x", "rtx-3060", psuWatts: 450, caseGpuMm: 250);
        var gpu = Part<GpuPart>(g => g.TierId == "rtx-5080" && g.LengthMm > 300);
        var report = Engine.Check(gpu, s);
        Assert.Equal(CheckStatus.Fail, StatusOf(report, "Power"));
        Assert.Equal(CheckStatus.Fail, StatusOf(report, "Case clearance"));
    }

    [Fact]
    public void Unknown_psu_and_case_are_unverified()
    {
        var s = TestSystems.Desktop("ryzen-5-5600x", "rtx-3060");
        var report = Engine.Check(Part<GpuPart>(g => g.TierId == "rtx-5070"), s);
        Assert.Equal(CheckStatus.Unknown, StatusOf(report, "Power"));
        Assert.Equal(CheckStatus.Unknown, StatusOf(report, "Case clearance"));
    }

    [Fact]
    public void Laptop_cpu_and_gpu_cannot_be_replaced()
    {
        var s = TestSystems.Desktop("ryzen-7-7840hs", "radeon-780m", mem: MemoryType.DDR5);
        Assert.True(CompatibilityEngine.IsLaptop(s));
        Assert.Equal(CheckStatus.Fail, Engine.Check(Part<GpuPart>(_ => true), s).Overall);
        Assert.Equal(CheckStatus.Fail, Engine.Check(Part<CpuPart>(_ => true), s).Overall);
    }

    [Fact]
    public void No_free_m2_slot_fails_nvme_drives()
    {
        var s = TestSystems.Desktop("ryzen-5-5600x", "rtx-3060", freeM2: 0);
        var report = Engine.Check(Part<StoragePart>(d => d.Kind == StorageKind.Nvme), s);
        Assert.Equal(CheckStatus.Fail, report.Overall);
    }

    [Theory]
    [InlineData("ATX", "mATX", true)]
    [InlineData("mATX", "ATX", false)]
    [InlineData("ITX", "ITX", true)]
    [InlineData("Mini-ITX", "Micro-ATX", false)]
    public void Board_fits_case_by_size(string caseFf, string boardFf, bool fits)
    {
        Assert.Equal(fits, CompatibilityEngine.CaseFits(caseFf, boardFf));
    }
}
