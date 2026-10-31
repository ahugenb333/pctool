using System.Text.RegularExpressions;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Data;

public sealed partial class ChipsetCatalog : IChipsetCatalog
{
    private readonly Dictionary<string, ChipsetInfo> _byName;

    public IReadOnlyList<ChipsetInfo> All { get; }

    public ChipsetCatalog(IReadOnlyList<ChipsetInfo> chipsets)
    {
        All = chipsets;
        _byName = chipsets.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Accepts "B550", "AMD B850" or "Intel B860" (the parts dataset prefixes some chipsets with the vendor).</summary>
    public ChipsetInfo? Find(string? name)
    {
        var key = NormalizeChipset(name);
        if (key.Length == 0) return null;
        if (_byName.TryGetValue(key, out var exact)) return exact;
        // Board makers append variant letters ("A620A", "B650E" when only "B650" is listed).
        return key.Length > 4 && char.IsLetter(key[^1]) ? _byName.GetValueOrDefault(key[..^1]) : null;
    }

    public string? DetectFromBoardName(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        foreach (Match m in BoardChipset().Matches(product.ToUpperInvariant()))
        {
            if (_byName.ContainsKey(m.Groups[1].Value)) return m.Groups[1].Value;
        }
        return null;
    }

    public static string NormalizeChipset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim().ToUpperInvariant();
        foreach (var prefix in new[] { "AMD ", "INTEL " })
            if (s.StartsWith(prefix)) s = s[prefix.Length..];
        return s.Trim();
    }

    public static string NormalizeSocket(string? socket)
    {
        if (string.IsNullOrWhiteSpace(socket)) return "";
        var s = socket.ToUpperInvariant().Replace("SOCKET", "").Replace(" ", "").Trim();
        return s switch
        {
            "1700" => "LGA1700",
            "1851" => "LGA1851",
            "1200" => "LGA1200",
            _ => s,
        };
    }

    public static ChipsetCatalog FromCsv(string csv)
    {
        var t = CsvTable.Parse(csv);
        return new ChipsetCatalog(t.Rows.Select(r => new ChipsetInfo
        {
            Name = t.Get(r, "name"),
            Vendor = t.Get(r, "vendor"),
            Socket = t.Get(r, "socket"),
            MemoryTypes = t.List(r, "mem"),
            PcieGen = t.Int(r, "pcie"),
            BiosUpdateFamilies = t.List(r, "biosUpdate"),
            UnsupportedFamilies = t.List(r, "unsupported"),
        }).ToList());
    }

    [GeneratedRegex(@"(?<![A-Z0-9])([ABHQXZ]\d{2,3}E?)(?![0-9])")]
    private static partial Regex BoardChipset();
}
