using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Data;

public sealed class PerformanceTierSource : IPerformanceTierSource
{
    private readonly Dictionary<string, CpuTier> _cpuById;
    private readonly Dictionary<string, GpuTier> _gpuById;
    private readonly List<(CpuTier, string)> _cpuAliases;
    private readonly List<(GpuTier, string)> _gpuAliases;

    public IReadOnlyList<CpuTier> Cpus { get; }
    public IReadOnlyList<GpuTier> Gpus { get; }

    public PerformanceTierSource(IReadOnlyList<CpuTier> cpus, IReadOnlyList<GpuTier> gpus)
    {
        Cpus = cpus;
        Gpus = gpus;
        _cpuById = cpus.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _gpuById = gpus.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        _cpuAliases = cpus.SelectMany(c => AliasesOf(c.Name, c.Aliases).Select(a => (c, a))).ToList();
        _gpuAliases = gpus.SelectMany(g => AliasesOf(g.Name, g.Aliases).Select(a => (g, a))).ToList();
    }

    public CpuTier? GetCpu(string id) => _cpuById.GetValueOrDefault(id);
    public GpuTier? GetGpu(string id) => _gpuById.GetValueOrDefault(id);
    public CpuTier? FindCpu(string name) => NameMatcher.BestMatch(name, _cpuAliases);
    public GpuTier? FindGpu(string name) => NameMatcher.BestMatch(name, _gpuAliases);

    private static IEnumerable<string> AliasesOf(string name, string[] aliases) =>
        (aliases.Length > 0 ? aliases : [name]).Append(name).Select(NameMatcher.Normalize).Distinct();

    public static PerformanceTierSource FromCsv(string cpuCsv, string gpuCsv)
    {
        var c = CsvTable.Parse(cpuCsv);
        var cpus = c.Rows.Select(r => new CpuTier
        {
            Id = NameMatcher.Slug(c.Get(r, "name")),
            Name = c.Get(r, "name"),
            Vendor = c.Get(r, "vendor"),
            Family = c.Get(r, "family"),
            Socket = c.Get(r, "socket"),
            MemoryTypes = c.List(r, "mem"),
            Cores = c.Int(r, "cores"),
            Threads = c.Int(r, "threads"),
            TdpW = c.Int(r, "tdp"),
            SingleScore = c.Double(r, "single"),
            MultiScore = c.Double(r, "multi"),
            Year = c.Int(r, "year"),
            IsLaptop = c.Get(r, "kind") == "l",
            GamingFactor = c.Double(r, "gaming", 1),
            Aliases = c.List(r, "aliases"),
        }).ToList();

        var g = CsvTable.Parse(gpuCsv);
        var gpus = g.Rows.Select(r => new GpuTier
        {
            Id = NameMatcher.Slug(g.Get(r, "name")),
            Name = g.Get(r, "name"),
            Vendor = g.Get(r, "vendor"),
            Score = g.Double(r, "score"),
            VramGb = g.Int(r, "vram"),
            TdpW = g.Int(r, "tdp"),
            RayTracing = g.Get(r, "rt") == "1",
            Upscaling = g.Get(r, "upscaling"),
            DirectX = g.Get(r, "dx"),
            Year = g.Int(r, "year"),
            IsIntegrated = g.Get(r, "kind") == "i",
            IsLaptop = g.Get(r, "kind") == "l",
            Aliases = g.List(r, "aliases"),
        }).ToList();

        return new PerformanceTierSource(cpus, gpus);
    }
}
