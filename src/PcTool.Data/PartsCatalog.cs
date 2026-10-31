using System.Text.Json;
using System.Text.RegularExpressions;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Data;

/// <summary>
/// Parts loaded from files in the TechFuelHQ PC Builder Parts Dataset schema (category arrays of snake_case rows).
/// The bundled supplemental file uses the same schema so both load through one path.
/// </summary>
public sealed partial class PartsCatalog : IPartsCatalog
{
    private readonly Dictionary<string, Part> _byId;

    public IReadOnlyList<Part> Parts { get; }
    public IReadOnlyList<DataAttribution> Attributions { get; }

    public PartsCatalog(IReadOnlyList<Part> parts, IReadOnlyList<DataAttribution> attributions)
    {
        Parts = parts;
        Attributions = attributions;
        _byId = parts.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public Part? Get(string id) => _byId.GetValueOrDefault(id);

    public static IEnumerable<Part> ParseDataset(string json, string datasetKey, IPerformanceTierSource tiers)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var e in Rows(root, "cpu"))
        {
            var model = Str(e, "model");
            yield return new CpuPart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = model, Notes = Str(e, "notes"), SourceDataset = datasetKey,
                Cores = Int(e, "cores"), Threads = Int(e, "threads"), BoostGhz = Dbl(e, "boost_ghz"), TdpW = Int(e, "tdp_w"),
                Socket = ChipsetCatalog.NormalizeSocket(Str(e, "socket")), RamType = Str(e, "ram_type"),
                SupportedChipsets = StrArray(e, "supported_chipsets").Select(ChipsetCatalog.NormalizeChipset).ToArray(),
                TierId = tiers.FindCpu(model)?.Id,
            };
        }

        foreach (var e in Rows(root, "gpu"))
        {
            var model = Str(e, "model");
            yield return new GpuPart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = model, Notes = Str(e, "notes"), SourceDataset = datasetKey,
                VramGb = Int(e, "vram_gb"), TgpW = Int(e, "tgp_w"), LengthMm = Dbl(e, "length_mm"), PcieGen = Gen(Str(e, "pcie")),
                TierId = tiers.FindGpu(model)?.Id,
            };
        }

        foreach (var e in Rows(root, "motherboard"))
        {
            yield return new MotherboardPart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = Str(e, "model"), Notes = Str(e, "notes"), SourceDataset = datasetKey,
                Socket = ChipsetCatalog.NormalizeSocket(Str(e, "socket")), Chipset = ChipsetCatalog.NormalizeChipset(Str(e, "chipset")),
                FormFactor = NormalizeFormFactor(Str(e, "form_factor")), RamType = Str(e, "ram_type"),
                RamSlots = Int(e, "ram_slots"), RamMaxGb = Int(e, "ram_max_gb"), M2Slots = Int(e, "m2_slots"), PcieGen = Gen(Str(e, "pcie_gen")),
            };
        }

        foreach (var e in Rows(root, "ram"))
        {
            var kit = Str(e, "kit");
            yield return new RamPart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = Str(e, "model"), Notes = Str(e, "notes"), SourceDataset = datasetKey,
                CapacityGb = Int(e, "capacity_gb"), SpeedMtps = Int(e, "speed_mtps"), CasLatency = Int(e, "cas_latency"), RamType = Str(e, "ram_type"),
                ModuleCount = KitModules().Match(kit) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 2,
            };
        }

        foreach (var e in Rows(root, "storage"))
        {
            yield return new StoragePart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = Str(e, "model"), Notes = Str(e, "notes"), SourceDataset = datasetKey,
                CapacityGb = Int(e, "capacity_gb"), Interface = Str(e, "interface"), FormFactor = Str(e, "form_factor"),
                ReadMbps = Int(e, "read_mbps"), WriteMbps = Int(e, "write_mbps"),
            };
        }

        foreach (var e in Rows(root, "psu"))
        {
            yield return new PsuPart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = Str(e, "model"), Notes = Str(e, "notes"), SourceDataset = datasetKey,
                WattageW = Int(e, "wattage_w"), Efficiency = Str(e, "efficiency"),
            };
        }

        foreach (var e in Rows(root, "case"))
        {
            yield return new CasePart
            {
                Id = Str(e, "id"), Vendor = Str(e, "vendor"), Model = Str(e, "model"), Notes = Str(e, "notes"), SourceDataset = datasetKey,
                FormFactor = NormalizeFormFactor(Str(e, "form_factor")),
                SupportsFormFactors = StrArray(e, "supports_form_factors").Select(NormalizeFormFactor).ToArray(),
                MaxGpuLengthMm = Int(e, "max_gpu_length_mm"), MaxCpuCoolerMm = Int(e, "max_cpu_cooler_mm"),
            };
        }
    }

    public static string NormalizeFormFactor(string? ff)
    {
        var s = (ff ?? "").Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        return s switch
        {
            "MICROATX" or "MATX" or "UATX" => "mATX",
            "MINIITX" or "ITX" => "ITX",
            "EATX" => "E-ATX",
            "ATX" => "ATX",
            _ => ff?.Trim() ?? "",
        };
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray().ToList() : [];

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : 0;

    private static double Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string[] StrArray(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToArray()
            : [];

    private static int? Gen(string s) => GenPattern().Match(s) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    [GeneratedRegex(@"Gen\s*(\d)", RegexOptions.IgnoreCase)]
    private static partial Regex GenPattern();

    [GeneratedRegex(@"(\d+)\s*x\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex KitModules();
}
