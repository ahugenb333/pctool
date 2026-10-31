namespace PcTool.Logic.Tests;

public class TierMatchingTests
{
    private static readonly Core.IPerformanceTierSource Tiers = TestSystems.Catalogs.Tiers;

    [Theory]
    [InlineData("AMD Ryzen 7 5800XT 8-Core Processor", "Ryzen 7 5800XT")]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor", "Ryzen 7 5800X")]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", "Ryzen 7 5800X3D")]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", "Ryzen 5 5600X")]
    [InlineData("AMD Ryzen 5 5600 6-Core Processor", "Ryzen 5 5600")]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600K", "Core i5-13600K")]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", "Core i7-8700K")]
    [InlineData("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", "Core i7-8700")]
    [InlineData("Intel(R) Core(TM) Ultra 7 265K", "Core Ultra 7 265K")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", "Ryzen 7 7840HS")]
    public void Detected_cpu_names_match_the_right_tier(string detected, string expected)
    {
        Assert.Equal(expected, Tiers.FindCpu(detected)?.Name);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", "RTX 3070")]
    [InlineData("NVIDIA GeForce RTX 3070 Ti", "RTX 3070 Ti")]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "RTX 4060 Laptop")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", "GTX 1060 6GB")]
    [InlineData("AMD Radeon RX 7900 XTX", "RX 7900 XTX")]
    [InlineData("AMD Radeon RX 7900 XT", "RX 7900 XT")]
    [InlineData("AMD Radeon RX 9070 XT", "RX 9070 XT")]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", "Arc B580")]
    [InlineData("AMD Radeon 780M Graphics", "Radeon 780M")]
    public void Detected_gpu_names_match_the_right_tier(string detected, string expected)
    {
        Assert.Equal(expected, Tiers.FindGpu(detected)?.Name);
    }

    [Fact]
    public void Unknown_hardware_returns_null_instead_of_a_wrong_guess()
    {
        Assert.Null(Tiers.FindGpu("Microsoft Basic Display Adapter"));
        Assert.Null(Tiers.FindCpu("Qualcomm Snapdragon X Elite"));
    }

    [Fact]
    public void Reference_parts_score_100()
    {
        Assert.Equal(100, Tiers.GetCpu("ryzen-5-5600x")!.SingleScore);
        Assert.Equal(100, Tiers.GetCpu("ryzen-5-5600x")!.MultiScore);
        Assert.Equal(100, Tiers.GetGpu("rtx-3060")!.Score);
    }
}
