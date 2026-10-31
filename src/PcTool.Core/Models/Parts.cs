namespace PcTool.Core.Models;

public enum PartCategory { Cpu, Gpu, Motherboard, Ram, Storage, Psu, Case }

public abstract class Part
{
    public string Id { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "";
    public string Notes { get; init; } = "";
    /// <summary>Key into <see cref="DataAttribution"/> naming the dataset this row came from.</summary>
    public string SourceDataset { get; init; } = "";
    public abstract PartCategory Category { get; }

    public string DisplayName =>
        Model.StartsWith(Vendor, StringComparison.OrdinalIgnoreCase) || Vendor.Length == 0 || Vendor.EndsWith("AIB")
            ? Model
            : $"{Vendor} {Model}";

    public override string ToString() => DisplayName;
}

public sealed class CpuPart : Part
{
    public override PartCategory Category => PartCategory.Cpu;
    public int Cores { get; init; }
    public int Threads { get; init; }
    public double BoostGhz { get; init; }
    public int TdpW { get; init; }
    public string Socket { get; init; } = "";
    public string RamType { get; init; } = "";
    public string[] SupportedChipsets { get; init; } = [];
    public string? TierId { get; set; }
}

public sealed class GpuPart : Part
{
    public override PartCategory Category => PartCategory.Gpu;
    public int VramGb { get; init; }
    public int TgpW { get; init; }
    public double LengthMm { get; init; }
    public int? PcieGen { get; init; }
    public string? TierId { get; set; }
}

public sealed class MotherboardPart : Part
{
    public override PartCategory Category => PartCategory.Motherboard;
    public string Socket { get; init; } = "";
    public string Chipset { get; init; } = "";
    public string FormFactor { get; init; } = "";
    public string RamType { get; init; } = "";
    public int RamSlots { get; init; }
    public int RamMaxGb { get; init; }
    public int M2Slots { get; init; }
    public int? PcieGen { get; init; }
}

public sealed class RamPart : Part
{
    public override PartCategory Category => PartCategory.Ram;
    public int CapacityGb { get; init; }
    public int ModuleCount { get; init; }
    public int SpeedMtps { get; init; }
    public int CasLatency { get; init; }
    public string RamType { get; init; } = "";
}

public sealed class StoragePart : Part
{
    public override PartCategory Category => PartCategory.Storage;
    public int CapacityGb { get; init; }
    public string Interface { get; init; } = "";
    public string FormFactor { get; init; } = "";
    public int ReadMbps { get; init; }
    public int WriteMbps { get; init; }
    public StorageKind Kind => Interface.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? StorageKind.Nvme : StorageKind.SataSsd;
}

public sealed class PsuPart : Part
{
    public override PartCategory Category => PartCategory.Psu;
    public int WattageW { get; init; }
    public string Efficiency { get; init; } = "";
}

public sealed class CasePart : Part
{
    public override PartCategory Category => PartCategory.Case;
    public string FormFactor { get; init; } = "";
    public string[] SupportsFormFactors { get; init; } = [];
    public int MaxGpuLengthMm { get; init; }
    public int MaxCpuCoolerMm { get; init; }
}

public sealed record DataAttribution(string Key, string Name, string License, string Url, string Notice);
