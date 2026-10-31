using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Data;

namespace PcTool.Compatibility;

/// <summary>Ordered by severity so the worst check decides a report's overall status.</summary>
public enum CheckStatus { Pass, Unknown, Warn, Fail }

public sealed record CompatibilityCheck(string Rule, CheckStatus Status, string Message);

public sealed record CompatibilityReport(Part Part, IReadOnlyList<CompatibilityCheck> Checks)
{
    public CheckStatus Overall => Checks.Count == 0 ? CheckStatus.Pass : Checks.Max(c => c.Status);
    public bool Fits => Overall != CheckStatus.Fail;
}

public sealed class CompatibilityEngine(IPerformanceTierSource tiers, IChipsetCatalog chipsets)
{
    /// <summary>Rule of thumb: CPU TDP + GPU board power + 150 W for the rest of the system, with 20% headroom.</summary>
    public static int RecommendedPsuWatts(int cpuTdpW, int gpuTdpW)
    {
        var raw = (Math.Max(cpuTdpW, 35) + Math.Max(gpuTdpW, 0) + 150) * 1.2;
        return (int)(Math.Ceiling(raw / 50) * 50);
    }

    public static bool IsLaptop(SystemSpecs s) => s.CpuTier?.IsLaptop == true || s.Socket == "BGA";

    public CpuTier? TierFor(CpuPart cpu) => cpu.TierId is { } id ? tiers.GetCpu(id) : tiers.FindCpu(cpu.Model);
    public GpuTier? TierFor(GpuPart gpu) => gpu.TierId is { } id ? tiers.GetGpu(id) : tiers.FindGpu(gpu.Model);

    /// <summary>The system as it would look after swapping in a new motherboard (used for platform bundles).</summary>
    public SystemSpecs WithBoard(SystemSpecs s, MotherboardPart board)
    {
        var memType = ParseMemoryTypes(board.RamType).FirstOrDefault(s.MemoryType);
        return s.With(socket: board.Socket, chipset: chipsets.Find(board.Chipset), memoryType: memType,
            ramSlots: board.RamSlots, maxRamGb: board.RamMaxGb);
    }

    public CompatibilityReport Check(Part part, SystemSpecs s) => new(part, part switch
    {
        CpuPart cpu => CheckCpu(cpu, s),
        GpuPart gpu => CheckGpu(gpu, s),
        RamPart ram => CheckRam(ram, s),
        MotherboardPart board => CheckBoard(board, s),
        StoragePart storage => CheckStorage(storage, s),
        PsuPart psu => CheckPsu(psu, s),
        CasePart @case => CheckCase(@case, s),
        _ => [],
    });

    private List<CompatibilityCheck> CheckCpu(CpuPart cpu, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Platform", CheckStatus.Fail, "Laptop and mini-PC CPUs are soldered to the board and can't be replaced."));
            return checks;
        }

        if (string.IsNullOrEmpty(s.Socket))
            checks.Add(new("Socket", CheckStatus.Unknown, $"Couldn't detect your CPU socket. This CPU needs a {cpu.Socket} motherboard."));
        else if (!cpu.Socket.Equals(s.Socket, StringComparison.OrdinalIgnoreCase))
            checks.Add(new("Socket", CheckStatus.Fail, $"Needs a {cpu.Socket} motherboard; yours is {s.Socket}. This would be a platform upgrade (board{(RamFits(cpu.RamType, s.MemoryType) ? "" : " and RAM")})."));
        else
        {
            checks.Add(new("Socket", CheckStatus.Pass, $"{cpu.Socket} matches your motherboard."));
            checks.Add(ChipsetCheck(cpu, s));
            if (s.MemoryType != MemoryType.Unknown && !RamFits(cpu.RamType, s.MemoryType))
                checks.Add(new("Memory", CheckStatus.Fail, $"This CPU uses {cpu.RamType}; your system has {s.MemoryType}."));
        }

        checks.Add(PowerCheck(TierFor(cpu)?.TdpW ?? cpu.TdpW, s.GpuTdpW, s));

        if (cpu.TdpW > s.CpuTdpW + 20)
            checks.Add(new("Cooling", CheckStatus.Warn, $"Rated {cpu.TdpW} W vs your current {s.CpuTdpW} W; make sure your cooler can handle it."));

        if (cpu.Model.EndsWith('F') && s.GpuIsIntegrated)
            checks.Add(new("Graphics", CheckStatus.Fail, "This CPU has no integrated graphics and you have no graphics card."));

        return checks;
    }

    private CompatibilityCheck ChipsetCheck(CpuPart cpu, SystemSpecs s)
    {
        if (s.Chipset is null)
            return new("Chipset", CheckStatus.Unknown, "Your motherboard chipset is unknown, so BIOS support can't be verified. Set it on the Settings page.");

        var family = TierFor(cpu)?.Family;
        if (family is not null)
        {
            if (s.Chipset.UnsupportedFamilies.Contains(family, StringComparer.OrdinalIgnoreCase))
                return new("Chipset", CheckStatus.Fail, $"{s.Chipset.Name} boards don't support {family} CPUs.");
            if (s.Chipset.BiosUpdateFamilies.Contains(family, StringComparer.OrdinalIgnoreCase))
                return new("Chipset", CheckStatus.Warn, $"{s.Chipset.Name} boards need a BIOS update for {family} CPUs. Update the BIOS with your current CPU installed first.");
            return new("Chipset", CheckStatus.Pass, $"{s.Chipset.Name} supports {family} CPUs.");
        }

        if (cpu.SupportedChipsets.Length > 0 && !cpu.SupportedChipsets.Contains(s.Chipset.Name, StringComparer.OrdinalIgnoreCase))
            return new("Chipset", CheckStatus.Fail, $"Not listed as supported on {s.Chipset.Name} boards.");
        return new("Chipset", CheckStatus.Pass, $"Supported on {s.Chipset.Name} boards (check your board's CPU support list for the minimum BIOS).");
    }

    private List<CompatibilityCheck> CheckGpu(GpuPart gpu, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Platform", CheckStatus.Fail, "Laptop GPUs can't be replaced. An external GPU enclosure over Thunderbolt/USB4 is the only option."));
            return checks;
        }

        if (gpu.PcieGen is { } cardGen && s.PcieGen is { } slotGen && slotGen < cardGen)
            checks.Add(new("PCIe", CheckStatus.Warn, $"Your slot is PCIe {slotGen}.0 and the card is {cardGen}.0. It works, but x8 cards can lose a few percent."));
        else
            checks.Add(new("PCIe", CheckStatus.Pass, "Fits any PCIe x16 slot (PCIe is backward compatible)."));

        if (gpu.LengthMm <= 0)
            checks.Add(new("Case clearance", CheckStatus.Unknown, "Card length not listed; check the manufacturer's spec sheet."));
        else if (s.CaseMaxGpuLengthMm is not { } maxLen)
            checks.Add(new("Case clearance", CheckStatus.Unknown, $"Card is {gpu.LengthMm:F0} mm long. Enter your case's GPU clearance on the Settings page to verify."));
        else if (gpu.LengthMm > maxLen)
            checks.Add(new("Case clearance", CheckStatus.Fail, $"Card is {gpu.LengthMm:F0} mm; your case fits {maxLen} mm."));
        else if (gpu.LengthMm > maxLen - 10)
            checks.Add(new("Case clearance", CheckStatus.Warn, $"Card is {gpu.LengthMm:F0} mm; your case fits {maxLen} mm. Tight with front fans or radiators."));
        else
            checks.Add(new("Case clearance", CheckStatus.Pass, $"Card is {gpu.LengthMm:F0} mm; your case fits {maxLen} mm."));

        checks.Add(PowerCheck(s.CpuTdpW, TierFor(gpu)?.TdpW is > 0 and var t ? t : gpu.TgpW, s));
        return checks;
    }

    private static List<CompatibilityCheck> CheckRam(RamPart ram, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Form factor", CheckStatus.Fail, "Laptops use SO-DIMM modules (or soldered RAM); this is a desktop kit."));
            return checks;
        }

        if (s.MemoryType == MemoryType.Unknown)
            checks.Add(new("Type", CheckStatus.Unknown, $"Couldn't detect your memory type. This kit is {ram.RamType}."));
        else if (!RamFits(ram.RamType, s.MemoryType))
            checks.Add(new("Type", CheckStatus.Fail, $"This is {ram.RamType}; your board takes {s.MemoryType}."));
        else
            checks.Add(new("Type", CheckStatus.Pass, $"{ram.RamType} matches your board."));

        if (s.RamSlots > 0 && ram.ModuleCount > s.RamSlots)
        {
            checks.Add(new("Slots", CheckStatus.Fail, $"Kit has {ram.ModuleCount} modules; your board has {s.RamSlots} slots."));
            return checks;
        }

        var freeSlots = s.RamSlots - s.RamSlotsUsed;
        var addsTo = s.RamSlots > 0 && ram.ModuleCount <= freeSlots;
        var total = addsTo ? s.RamGb + ram.CapacityGb : ram.CapacityGb;

        if (s.MaxRamGb > 0 && total > s.MaxRamGb)
            checks.Add(new("Capacity", CheckStatus.Fail, $"{total} GB total exceeds your board's {s.MaxRamGb} GB maximum."));
        else if (s.RamSlots == 0)
            checks.Add(new("Slots", CheckStatus.Unknown, "Couldn't read your slot count. Assuming this kit replaces your current RAM."));
        else if (addsTo)
            checks.Add(new("Slots", CheckStatus.Warn, $"Fits your {freeSlots} free slots for {total} GB total. Mixing kits can force lower speeds; a single matched kit is safer."));
        else
            checks.Add(new("Slots", CheckStatus.Pass, $"Replaces your current {s.RamGb} GB ({s.RamSlotsUsed} of {s.RamSlots} slots used) for {total} GB total."));

        return checks;
    }

    private List<CompatibilityCheck> CheckBoard(MotherboardPart board, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Platform", CheckStatus.Fail, "Laptop motherboards are proprietary and can't be swapped for a desktop board."));
            return checks;
        }

        if (string.IsNullOrEmpty(s.Socket))
            checks.Add(new("CPU", CheckStatus.Unknown, $"Couldn't detect your CPU socket. This board is {board.Socket}."));
        else if (!board.Socket.Equals(s.Socket, StringComparison.OrdinalIgnoreCase))
            checks.Add(new("CPU", CheckStatus.Fail, $"Your {s.CpuName} ({s.Socket}) doesn't fit this {board.Socket} board; pair it with a {board.Socket} CPU."));
        else
            checks.Add(new("CPU", CheckStatus.Pass, $"Your {s.CpuName} fits the {board.Socket} socket (check the board's CPU support list for BIOS requirements)."));

        if (s.MemoryType != MemoryType.Unknown && !RamFits(board.RamType, s.MemoryType))
            checks.Add(new("Memory", CheckStatus.Fail, $"Takes {board.RamType}; your current RAM is {s.MemoryType} and would need replacing."));

        checks.Add(FormFactorCheck(board.FormFactor, s.CaseFormFactor));
        return checks;
    }

    private static List<CompatibilityCheck> CheckStorage(StoragePart storage, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (storage.Kind == StorageKind.Nvme)
        {
            if (s.FreeM2Slots is not { } free)
                checks.Add(new("M.2 slot", CheckStatus.Unknown, "Needs a free M.2 NVMe slot. Enter your free M.2 slots on the Settings page to verify."));
            else if (free == 0)
                checks.Add(new("M.2 slot", CheckStatus.Fail, "No free M.2 slots. Replace an existing drive, or use a SATA SSD or a PCIe adapter card."));
            else
                checks.Add(new("M.2 slot", CheckStatus.Pass, $"{free} free M.2 slot(s)."));

            var driveGen = PcieGenOf(storage.Interface);
            if (driveGen is { } dg && s.PcieGen is { } bg && bg < dg)
                checks.Add(new("Speed", CheckStatus.Warn, $"A PCIe {dg}.0 drive runs at PCIe {bg}.0 speeds on your board; a cheaper Gen{bg} drive performs the same."));
        }
        else
        {
            checks.Add(IsLaptop(s)
                ? new("Bay", CheckStatus.Warn, "Check that your laptop has a 2.5-inch drive bay; many newer models only have M.2.")
                : new("SATA", CheckStatus.Pass, "Uses a SATA port and a 2.5-inch bay, which nearly every desktop has."));
        }
        return checks;
    }

    private static List<CompatibilityCheck> CheckPsu(PsuPart psu, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Platform", CheckStatus.Fail, "Laptops use an external power adapter."));
            return checks;
        }

        var need = RecommendedPsuWatts(s.CpuTdpW, s.GpuTdpW);
        checks.Add(psu.WattageW >= need
            ? new("Wattage", CheckStatus.Pass, $"{psu.WattageW} W covers the recommended {need} W for your CPU and GPU.")
            : new("Wattage", psu.WattageW >= need * 0.85 ? CheckStatus.Warn : CheckStatus.Fail, $"{psu.WattageW} W is below the recommended {need} W for your CPU and GPU."));

        if (s.CaseFormFactor == "ITX")
            checks.Add(new("Size", CheckStatus.Warn, "Many ITX cases need an SFX power supply; check your case's PSU support."));
        return checks;
    }

    private static List<CompatibilityCheck> CheckCase(CasePart @case, SystemSpecs s)
    {
        var checks = new List<CompatibilityCheck>();
        if (IsLaptop(s))
        {
            checks.Add(new("Platform", CheckStatus.Fail, "Laptops can't be moved into a desktop case."));
            return checks;
        }

        checks.Add(new("Motherboard", CheckStatus.Unknown,
            $"Supports {string.Join(", ", @case.SupportsFormFactors.DefaultIfEmpty(@case.FormFactor))} boards. Check your board's form factor."));
        if (@case.MaxGpuLengthMm > 0)
            checks.Add(new("GPU clearance", CheckStatus.Unknown, $"Fits graphics cards up to {@case.MaxGpuLengthMm} mm; measure your card."));
        if (@case.MaxCpuCoolerMm > 0)
            checks.Add(new("Cooler clearance", CheckStatus.Unknown, $"Fits CPU coolers up to {@case.MaxCpuCoolerMm} mm tall."));
        return checks;
    }

    private static CompatibilityCheck PowerCheck(int cpuTdp, int gpuTdp, SystemSpecs s)
    {
        var need = RecommendedPsuWatts(cpuTdp, gpuTdp);
        if (s.PsuWatts is not { } have)
            return new("Power", CheckStatus.Unknown, $"Recommended PSU: {need} W. Enter your PSU wattage on the Settings page to verify.");
        if (have >= need)
            return new("Power", CheckStatus.Pass, $"Your {have} W PSU covers the recommended {need} W.");
        return new("Power", have >= need * 0.85 ? CheckStatus.Warn : CheckStatus.Fail,
            $"Your {have} W PSU is below the recommended {need} W.");
    }

    private static CompatibilityCheck FormFactorCheck(string boardFf, string? caseFf)
    {
        if (string.IsNullOrEmpty(caseFf))
            return new("Case", CheckStatus.Unknown, $"{boardFf} board. Set your case size on the Settings page to verify it fits.");
        return CaseFits(caseFf, boardFf)
            ? new("Case", CheckStatus.Pass, $"{boardFf} board fits your {caseFf} case.")
            : new("Case", CheckStatus.Fail, $"{boardFf} board won't fit your {caseFf} case.");
    }

    public static bool CaseFits(string caseFf, string boardFf)
    {
        static int Rank(string ff) => PartsCatalog.NormalizeFormFactor(ff) switch
        {
            "ITX" => 0, "mATX" => 1, "ATX" => 2, "E-ATX" => 3, _ => -1,
        };
        int c = Rank(caseFf), b = Rank(boardFf);
        return c < 0 || b < 0 || b <= c;
    }

    public static IReadOnlyList<MemoryType> ParseMemoryTypes(string ramType) =>
        ramType.Split(['/', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Enum.TryParse<MemoryType>(t, true, out var m) ? m : MemoryType.Unknown)
            .Where(m => m != MemoryType.Unknown)
            .ToList();

    public static bool RamFits(string partRamType, MemoryType system) =>
        system == MemoryType.Unknown || string.IsNullOrWhiteSpace(partRamType) || ParseMemoryTypes(partRamType).Contains(system);

    private static int? PcieGenOf(string iface)
    {
        var i = iface.IndexOf("Gen", StringComparison.OrdinalIgnoreCase);
        return i >= 0 && i + 3 < iface.Length && char.IsDigit(iface[i + 3]) ? iface[i + 3] - '0' : null;
    }
}
