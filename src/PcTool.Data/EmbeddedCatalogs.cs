using System.Reflection;
using PcTool.Core.Models;

namespace PcTool.Data;

/// <summary>Loads every bundled data file (tiers, chipsets, games, parts, prices) from this assembly's embedded resources.</summary>
public sealed class EmbeddedCatalogs
{
    public required PerformanceTierSource Tiers { get; init; }
    public required ChipsetCatalog Chipsets { get; init; }
    public required JsonGameCatalog Games { get; init; }
    public required PartsCatalog Parts { get; init; }
    public required JsonPriceSource Prices { get; init; }

    public static readonly DataAttribution TechFuelHq = new(
        "techfuelhq",
        "TechFuelHQ PC Builder Parts Dataset",
        "CC BY 4.0",
        "https://techfuelhq.com/tools/pc-builder/",
        "TechFuelHQ PC Builder Parts Dataset (CC BY 4.0). https://techfuelhq.com/tools/pc-builder/ - part specifications used unmodified apart from field normalization.");

    public static readonly DataAttribution PcTool = new(
        "pctool",
        "PcTool supplemental data",
        "Same license as PcTool",
        "",
        "Performance tiers, game requirements, older-platform parts and reference prices curated by PcTool.");

    private static EmbeddedCatalogs? _cached;

    public static EmbeddedCatalogs Load() => _cached ??= LoadCore();

    private static EmbeddedCatalogs LoadCore()
    {
        var tiers = PerformanceTierSource.FromCsv(Read("tiers/cpus.csv"), Read("tiers/gpus.csv"));

        var parts = new List<Part>();
        foreach (var (name, json) in ReadAll("parts/"))
        {
            var key = name.Contains("techfuelhq", StringComparison.OrdinalIgnoreCase) ? TechFuelHq.Key : PcTool.Key;
            parts.AddRange(PartsCatalog.ParseDataset(json, key, tiers));
        }

        return new EmbeddedCatalogs
        {
            Tiers = tiers,
            Chipsets = ChipsetCatalog.FromCsv(Read("platforms/chipsets.csv")),
            Games = JsonGameCatalog.FromJson(Read("games/games.json")),
            Parts = new PartsCatalog(parts, [TechFuelHq, PcTool]),
            Prices = JsonPriceSource.FromJson(Read("prices/reference-prices.json")),
        };
    }

    private static Assembly Assembly => typeof(EmbeddedCatalogs).Assembly;

    private static string Normalize(string resourceName) => resourceName.Replace('\\', '/');

    private static string Read(string relativePath)
    {
        var name = Assembly.GetManifestResourceNames().FirstOrDefault(n => Normalize(n).EndsWith("data/" + relativePath, StringComparison.OrdinalIgnoreCase))
                   ?? throw new FileNotFoundException($"Bundled data file not found: {relativePath}");
        return ReadResource(name);
    }

    private static IEnumerable<(string Name, string Content)> ReadAll(string folder) =>
        Assembly.GetManifestResourceNames()
            .Where(n => Normalize(n).Contains("data/" + folder, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n)
            .Select(n => (Normalize(n), ReadResource(n)));

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
