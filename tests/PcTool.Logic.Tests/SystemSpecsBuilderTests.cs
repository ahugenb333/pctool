using PcTool.Core.Models;
using PcTool.Data;

namespace PcTool.Logic.Tests;

public class SystemSpecsBuilderTests
{
    private static readonly SystemSpecsBuilder Builder = new(TestSystems.Catalogs.Tiers, TestSystems.Catalogs.Chipsets);

    private static HardwareProfile Profile(string cpu, string gpu, string board, ulong vramBytes = 8UL << 30) => new()
    {
        Cpu = new CpuInfo { Name = cpu, Cores = 6, Threads = 12, MaxClockMhz = 3700 },
        Gpus = [new GpuInfo { Name = gpu, VramBytes = vramBytes }],
        Memory = new MemoryInfo
        {
            TotalBytes = 32UL << 30, Type = MemoryType.DDR4, SlotCount = 4, MaxCapacityBytes = 128UL << 30,
            Modules = [new MemoryModule { CapacityBytes = 16UL << 30 }, new MemoryModule { CapacityBytes = 16UL << 30 }],
        },
        Storage = [new StorageInfo { Kind = StorageKind.Nvme, IsSystemDrive = true, SizeBytes = 1_000_000_000_000 }],
        Motherboard = new MotherboardInfo { Manufacturer = "ASUSTeK COMPUTER INC.", Product = board },
    };

    [Fact]
    public void Resolves_tiers_socket_and_chipset_from_detected_names()
    {
        var s = Builder.Build(Profile("AMD Ryzen 5 5600X 6-Core Processor", "NVIDIA GeForce RTX 3060", "ROG STRIX B550-F GAMING"));
        Assert.Equal("Ryzen 5 5600X", s.CpuTier?.Name);
        Assert.Equal("RTX 3060", s.GpuTier?.Name);
        Assert.Equal("AM4", s.Socket);
        Assert.Equal("B550", s.Chipset?.Name);
        Assert.Equal(32, s.RamGb);
        Assert.Equal(2, s.RamSlotsUsed);
    }

    [Fact]
    public void User_override_wins_over_missing_chipset()
    {
        var profile = Profile("AMD Ryzen 7 5800XT 8-Core Processor", "NVIDIA GeForce RTX 3070", "G15DK");
        Assert.Null(Builder.Build(profile).Chipset);
        Assert.Equal("X570", Builder.Build(profile, new UserSystemOverrides { Chipset = "X570" }).Chipset?.Name);
    }

    [Fact]
    public void Unknown_gpu_falls_back_to_the_users_benchmark()
    {
        var run = new BenchmarkRun { Results = [new BenchmarkResult(BenchmarkKind.Gpu3D, 120, "FPS", 87)] };
        var s = Builder.Build(Profile("AMD Ryzen 5 5600X 6-Core Processor", "Some Future GPU 9000", "B550M"), benchmark: run);
        Assert.Null(s.GpuTier);
        Assert.True(s.GpuScoreFromBenchmark);
        Assert.Equal(87, s.GpuScore);
    }

    [Theory]
    [InlineData(8_407_482_368UL, 8)]
    [InlineData(12_878_610_432UL, 12)]
    [InlineData(25_438_126_080UL, 24)]
    public void Vram_rounds_to_the_marketed_size(ulong bytes, double expectedGb)
    {
        Assert.Equal(expectedGb, new GpuInfo { VramBytes = bytes }.VramGb);
    }
}
