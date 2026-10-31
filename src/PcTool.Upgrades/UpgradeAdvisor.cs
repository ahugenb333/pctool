using PcTool.Compatibility;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Games;

namespace PcTool.Upgrades;

public enum UpgradeGoal { Gaming1080p, Gaming1440p, Gaming4K, Productivity, SpecificGame }

public sealed record UpgradeOptions(UpgradeGoal Goal, decimal BudgetUsd, string? GameId = null);

public sealed record UpgradeCandidate(
    string Title,
    PartCategory Category,
    IReadOnlyList<Part> Parts,
    decimal? PriceUsd,
    double GainPercent,
    IReadOnlyList<string> GamesUnlocked,
    IReadOnlyList<CompatibilityReport> Compatibility,
    string Reason)
{
    public double? GainPer100Usd => PriceUsd is > 0 ? GainPercent / (double)PriceUsd.Value * 100 : null;
    public CheckStatus Overall => Compatibility.Count == 0 ? CheckStatus.Pass : Compatibility.Max(c => c.Overall);
    public bool IsBundle => Parts.Count > 1;
}

public sealed record BottleneckInfo(string Component, string Summary, IReadOnlyList<(string Component, double Ratio)> Ratios);

public sealed record UpgradePlan(
    UpgradeOptions Options,
    BottleneckInfo Bottleneck,
    IReadOnlyList<UpgradeCandidate> Candidates,
    UpgradeCandidate? Good,
    UpgradeCandidate? Better,
    UpgradeCandidate? Best,
    string PriceSource);

/// <summary>
/// Finds the component holding the system back for a goal, then ranks compatible upgrades (single parts and
/// platform bundles) by how much they raise the goal score, per dollar, and by games they move to playable/recommended.
/// </summary>
public sealed class UpgradeAdvisor(
    IPartsCatalog parts,
    IPriceSource prices,
    IGameCatalog games,
    CompatibilityEngine compat,
    GameAdvisor gameAdvisor)
{
    private const double MinGainPercent = 3;

    public UpgradePlan Recommend(SystemSpecs s, UpgradeOptions o)
    {
        var bottleneck = FindBottleneck(s, o);
        var baseScore = GoalScore(s, o);
        var baseVerdicts = games.Games.ToDictionary(g => g.Id, g => gameAdvisor.Evaluate(g, s).Level);

        var raw = new List<(string Title, PartCategory Category, List<Part> Parts, SystemSpecs After, string Reason)>();
        if (!CompatibilityEngine.IsLaptop(s))
        {
            raw.AddRange(GpuCandidates(s));
            raw.AddRange(CpuCandidates(s, o));
        }
        raw.AddRange(RamCandidates(s));
        raw.AddRange(StorageCandidates(s));

        var candidates = new List<UpgradeCandidate>();
        foreach (var c in raw)
        {
            var reports = c.Parts.Select(p => compat.Check(p, BundleContext(s, c.Parts, p))).ToList();
            if (reports.Any(r => r.Overall == CheckStatus.Fail)) continue;

            var gain = (GoalScore(c.After, o) / baseScore - 1) * 100;
            var unlocked = GamesUnlocked(c.After, baseVerdicts);
            var isStorageFix = c.Category == PartCategory.Storage;
            if (gain < MinGainPercent && unlocked.Count == 0 && !isStorageFix) continue;

            var price = SumPrices(c.Parts);
            candidates.Add(new UpgradeCandidate(c.Title, c.Category, c.Parts, price, Math.Round(gain, 1), unlocked, reports, c.Reason));
        }

        var inBudget = candidates
            .Where(c => o.BudgetUsd <= 0 || c.PriceUsd is null || c.PriceUsd <= o.BudgetUsd)
            .OrderByDescending(c => c.GainPer100Usd ?? 0)
            .ThenByDescending(c => c.GainPercent)
            .ToList();

        var ranked = inBudget.Where(c => c.GainPercent >= MinGainPercent && c.PriceUsd is > 0).ToList();
        var good = ranked.MaxBy(c => c.GainPer100Usd);
        var best = ranked.OrderByDescending(c => c.GainPercent).ThenBy(c => c.PriceUsd).FirstOrDefault();
        var better = good is null || best is null ? null
            : ranked.Where(c => c != good && c != best && c.PriceUsd > good.PriceUsd && c.PriceUsd < best.PriceUsd && c.GainPercent > good.GainPercent)
                .MaxBy(c => c.GainPer100Usd);
        if (best == good) best = null;

        return new UpgradePlan(o, bottleneck, inBudget, good, better, best, prices.Description);
    }

    // ---- Scoring --------------------------------------------------------------------------------------------

    /// <summary>A single number for "how well does this system meet the goal". Only ratios between systems matter.</summary>
    public double GoalScore(SystemSpecs s, UpgradeOptions o)
    {
        var r = Ratios(s, o);
        return SoftMin(r.Select(x => x.Ratio).ToArray());
    }

    public BottleneckInfo FindBottleneck(SystemSpecs s, UpgradeOptions o)
    {
        var ratios = Ratios(s, o);
        var worst = ratios.MinBy(r => r.Ratio);
        var goal = GoalLabel(o);
        var summary = worst.Ratio >= 1
            ? $"No component falls short for {goal}. The {worst.Component} is the closest to its limit ({worst.Ratio:P0} of target)."
            : $"Your {worst.Component} is holding you back for {goal}: it reaches {worst.Ratio:P0} of what the goal needs.";
        return new BottleneckInfo(worst.Component, summary, ratios);
    }

    public string GoalLabel(UpgradeOptions o) => o.Goal switch
    {
        UpgradeGoal.Gaming1080p => "1080p gaming",
        UpgradeGoal.Gaming1440p => "1440p gaming",
        UpgradeGoal.Gaming4K => "4K gaming",
        UpgradeGoal.Productivity => "productivity work",
        UpgradeGoal.SpecificGame => games.Get(o.GameId ?? "")?.Name is { } n ? $"{n} at recommended settings" : "the selected game",
        _ => o.Goal.ToString(),
    };

    private List<(string Component, double Ratio)> Ratios(SystemSpecs s, UpgradeOptions o)
    {
        if (o.Goal == UpgradeGoal.SpecificGame && games.Get(o.GameId ?? "") is { } game)
        {
            var v = gameAdvisor.Evaluate(game, s);
            var list = new List<(string, double)>
            {
                ("GPU", v.GpuRatio), ("CPU", v.CpuRatio),
                ("RAM", game.Recommended.RamGb > 0 ? s.RamGb / (double)game.Recommended.RamGb : 2),
            };
            if (!s.GpuIsIntegrated && game.Recommended.VramGb > 0) list.Add(("VRAM", s.VramGb / game.Recommended.VramGb));
            if (game.SsdRequired) list.Add(("Storage", s.HasSsd ? 1.5 : 0.6));
            return list.Select(x => (x.Item1, Math.Min(x.Item2, 3))).ToList();
        }

        if (o.Goal == UpgradeGoal.Productivity)
        {
            var cpu = Math.Pow(s.CpuMultiScore / 160, 0.75) * Math.Pow(s.CpuSingleScore / 110, 0.25);
            return
            [
                ("CPU", cpu),
                ("RAM", s.RamGb >= 64 ? 1.2 : s.RamGb >= 32 ? 1.0 : s.RamGb >= 16 ? 0.8 : 0.55),
                ("GPU", Math.Min(s.GpuScore / 90, 3)),
                ("Storage", StorageRatio(s)),
            ];
        }

        var (gpuNeed, cpuNeed, vramNeed) = o.Goal switch
        {
            UpgradeGoal.Gaming1080p => (110.0, 95.0, 8.0),
            UpgradeGoal.Gaming4K => (300.0, 110.0, 14.0),
            _ => (180.0, 105.0, 10.0),
        };
        var ratios = new List<(string, double)>
        {
            ("GPU", Math.Min(s.GpuScore / gpuNeed, 3)),
            ("CPU", Math.Min(s.CpuGamingScore / cpuNeed, 3)),
            ("RAM", s.RamGb >= 32 ? 1.3 : s.RamGb >= 16 ? 1.05 : s.RamGb >= 12 ? 0.85 : s.RamGb >= 8 ? 0.65 : 0.45),
            ("Storage", StorageRatio(s)),
        };
        if (!s.GpuIsIntegrated) ratios.Add(("VRAM", Math.Min(s.VramGb / vramNeed, 2)));
        return ratios;
    }

    private static double StorageRatio(SystemSpecs s) => s.SystemDriveKind switch
    {
        StorageKind.Nvme => 1.6,
        StorageKind.SataSsd => 1.4,
        StorageKind.Hdd => 0.8,
        _ => 1.3,
    };

    /// <summary>Generalized mean with p = -6: dominated by the weakest component, but improving others still counts a little.</summary>
    private static double SoftMin(double[] xs) =>
        Math.Pow(xs.Average(x => Math.Pow(Math.Max(x, 0.01), -6)), -1.0 / 6);

    private IReadOnlyList<string> GamesUnlocked(SystemSpecs after, Dictionary<string, GameVerdictLevel> before)
    {
        var list = new List<string>();
        foreach (var g in games.Games)
        {
            var was = before[g.Id];
            var now = gameAdvisor.Evaluate(g, after).Level;
            if (was < GameVerdictLevel.Recommended && now >= GameVerdictLevel.Recommended) list.Add($"{g.Name} (recommended)");
            else if (was == GameVerdictLevel.BelowMinimum && now >= GameVerdictLevel.Minimum) list.Add($"{g.Name} (playable)");
        }
        return list;
    }

    // ---- Candidate generation -------------------------------------------------------------------------------

    private IEnumerable<(string, PartCategory, List<Part>, SystemSpecs, string)> GpuCandidates(SystemSpecs s)
    {
        foreach (var gpu in CheapestPerTier(parts.OfType<GpuPart>(), g => g.TierId))
        {
            if (compat.TierFor(gpu) is not { } tier || tier.Score < s.GpuScore * 1.1) continue;
            var after = s.With(gpu: tier);
            var bundle = new List<Part> { gpu };
            var reason = $"{tier.Score / Math.Max(s.GpuScore, 1):F1}x your current GPU's raster performance, {tier.VramGb} GB VRAM.";
            if (PsuFor(s, s.CpuTdpW, tier.TdpW) is { } psu)
            {
                if (psu is PsuPart p) { bundle.Add(p); after = after.With(psuWatts: p.WattageW); reason += $" Includes a {p.WattageW} W PSU for the extra power draw."; }
                else continue;
            }
            yield return (gpu.DisplayName, PartCategory.Gpu, bundle, after, reason);
        }
    }

    private IEnumerable<(string, PartCategory, List<Part>, SystemSpecs, string)> CpuCandidates(SystemSpecs s, UpgradeOptions o)
    {
        var boards = parts.OfType<MotherboardPart>().ToList();
        var rams = parts.OfType<RamPart>().ToList();
        double Metric(double single, double multi, double factor) =>
            o.Goal == UpgradeGoal.Productivity ? multi : CpuTier.GamingScoreOf(single, multi, factor);
        var current = Metric(s.CpuSingleScore, s.CpuMultiScore, s.CpuTier?.GamingFactor ?? 1);

        foreach (var cpu in CheapestPerTier(parts.OfType<CpuPart>(), c => c.TierId))
        {
            if (compat.TierFor(cpu) is not { } tier || Metric(tier.SingleScore, tier.MultiScore, tier.GamingFactor) < current * 1.08) continue;

            var sameSocket = !string.IsNullOrEmpty(s.Socket) && cpu.Socket.Equals(s.Socket, StringComparison.OrdinalIgnoreCase);
            if (sameSocket)
            {
                var after = s.With(cpu: tier);
                var bundle = new List<Part> { cpu };
                var reason = $"Drop-in upgrade for your {s.Socket} board.";
                if (PsuFor(s, tier.TdpW, s.GpuTdpW) is { } psu)
                {
                    if (psu is not PsuPart p) continue;
                    bundle.Add(p); after = after.With(psuWatts: p.WattageW); reason += $" Includes a {p.WattageW} W PSU.";
                }
                yield return (cpu.DisplayName, PartCategory.Cpu, bundle, after, reason);
                continue;
            }

            // Platform upgrade: cheapest board for this CPU, plus RAM when the memory type changes.
            var board = boards
                .Where(b => b.Socket.Equals(cpu.Socket, StringComparison.OrdinalIgnoreCase))
                .Where(b => cpu.SupportedChipsets.Length == 0 || cpu.SupportedChipsets.Contains(b.Chipset, StringComparer.OrdinalIgnoreCase))
                .Where(b => s.CaseFormFactor is null || CompatibilityEngine.CaseFits(s.CaseFormFactor, b.FormFactor))
                .OrderBy(b => prices.GetPriceUsd(b.Id) ?? decimal.MaxValue)
                .FirstOrDefault();
            if (board is null) continue;

            var platform = compat.WithBoard(s, board);
            var parts2 = new List<Part> { cpu, board };
            var reasonP = $"Platform upgrade to {cpu.Socket}: new CPU and {board.Chipset} motherboard";
            var ramGb = s.RamGb;
            if (!CompatibilityEngine.RamFits(board.RamType, s.MemoryType))
            {
                var wantGb = Math.Max(s.RamGb, 32);
                var kit = rams
                    .Where(r => CompatibilityEngine.ParseMemoryTypes(r.RamType).Contains(platform.MemoryType) && r.CapacityGb >= wantGb)
                    .OrderBy(r => prices.GetPriceUsd(r.Id) ?? decimal.MaxValue)
                    .FirstOrDefault();
                if (kit is null) continue;
                parts2.Add(kit);
                ramGb = kit.CapacityGb;
                reasonP += $" and {kit.CapacityGb} GB {platform.MemoryType} (your {s.MemoryType} won't fit)";
            }
            var afterP = platform.With(cpu: tier, ramGb: ramGb);
            if (PsuFor(s, tier.TdpW, s.GpuTdpW) is { } psu2)
            {
                if (psu2 is not PsuPart p) continue;
                parts2.Add(p); afterP = afterP.With(psuWatts: p.WattageW); reasonP += $", plus a {p.WattageW} W PSU";
            }
            yield return ($"{cpu.DisplayName} platform", PartCategory.Cpu, parts2, afterP, reasonP + ".");
        }
    }

    private IEnumerable<(string, PartCategory, List<Part>, SystemSpecs, string)> RamCandidates(SystemSpecs s)
    {
        if (s.MemoryType == MemoryType.Unknown) yield break;
        var laptop = CompatibilityEngine.IsLaptop(s);
        if (laptop) yield break;

        var free = s.RamSlots - s.RamSlotsUsed;
        foreach (var kit in parts.OfType<RamPart>()
                     .Where(r => CompatibilityEngine.ParseMemoryTypes(r.RamType).Contains(s.MemoryType))
                     .GroupBy(r => r.CapacityGb)
                     .Select(g => g.OrderBy(r => prices.GetPriceUsd(r.Id) ?? decimal.MaxValue).First()))
        {
            var adds = s.RamSlots > 0 && kit.ModuleCount <= free;
            var total = adds ? s.RamGb + kit.CapacityGb : kit.CapacityGb;
            if (total <= s.RamGb || (s.MaxRamGb > 0 && total > s.MaxRamGb)) continue;
            var reason = adds
                ? $"Adds {kit.CapacityGb} GB in your free slots for {total} GB total."
                : $"Replaces your {s.RamGb} GB with {total} GB.";
            yield return (kit.DisplayName, PartCategory.Ram, [kit], s.With(ramGb: total), reason);
        }
    }

    private IEnumerable<(string, PartCategory, List<Part>, SystemSpecs, string)> StorageCandidates(SystemSpecs s)
    {
        var needsSsd = s.SystemDriveKind == StorageKind.Hdd || !s.HasSsd;
        var lowSpace = s.LargestFreeSpaceGb is > 0 and < 100;
        if (!needsSsd && !lowSpace) yield break;

        foreach (var drive in parts.OfType<StoragePart>()
                     .GroupBy(d => (d.Kind, d.CapacityGb))
                     .Select(g => g.OrderBy(d => prices.GetPriceUsd(d.Id) ?? decimal.MaxValue).First()))
        {
            if (drive.Kind == StorageKind.Nvme && s.FreeM2Slots == 0) continue;
            var after = needsSsd ? s.With(systemDrive: drive.Kind) : s;
            var reason = needsSsd
                ? "Move Windows and your games off the hard drive: much faster boots, loads and fewer stutters."
                : $"Only {s.LargestFreeSpaceGb:F0} GB free; modern games need 100+ GB each.";
            yield return (drive.DisplayName, PartCategory.Storage, [drive], after, reason);
        }
    }

    /// <summary>Null when the current PSU is fine or unknown; a PSU part when one is needed; a sentinel when none suffices.</summary>
    private object? PsuFor(SystemSpecs s, int cpuTdp, int gpuTdp)
    {
        var need = CompatibilityEngine.RecommendedPsuWatts(cpuTdp, gpuTdp);
        if (s.PsuWatts is not { } have || have >= need * 0.85) return null;
        return parts.OfType<PsuPart>()
            .Where(p => p.WattageW >= need)
            .OrderBy(p => prices.GetPriceUsd(p.Id) ?? decimal.MaxValue)
            .FirstOrDefault() ?? (object)"none";
    }

    /// <summary>Checks a bundle member against the system as it would be with the bundle's board installed.</summary>
    private SystemSpecs BundleContext(SystemSpecs s, List<Part> bundle, Part part)
    {
        var board = bundle.OfType<MotherboardPart>().FirstOrDefault();
        var ctx = board is not null && part is not MotherboardPart ? compat.WithBoard(s, board) : s;
        if (board is not null && part is MotherboardPart && bundle.OfType<CpuPart>().FirstOrDefault() is { } cpu && compat.TierFor(cpu) is { } tier)
            ctx = ctx.With(cpu: tier, socket: cpu.Socket, memoryType: CompatibilityEngine.ParseMemoryTypes(board.RamType).FirstOrDefault(ctx.MemoryType));
        if (bundle.OfType<PsuPart>().FirstOrDefault() is { } psu && part is not PsuPart)
            ctx = ctx.With(psuWatts: psu.WattageW);
        if (part is PsuPart)
        {
            var cpuT = bundle.OfType<CpuPart>().Select(compat.TierFor).FirstOrDefault(t => t is not null);
            var gpuT = bundle.OfType<GpuPart>().Select(compat.TierFor).FirstOrDefault(t => t is not null);
            ctx = ctx.With(cpu: cpuT, gpu: gpuT);
        }
        return ctx;
    }

    private IEnumerable<T> CheapestPerTier<T>(IEnumerable<T> items, Func<T, string?> tierId) where T : Part =>
        items.Where(p => tierId(p) is not null)
            .GroupBy(p => tierId(p)!)
            .Select(g => g.OrderBy(p => prices.GetPriceUsd(p.Id) ?? decimal.MaxValue).First());

    private decimal? SumPrices(IEnumerable<Part> bundle)
    {
        decimal total = 0;
        foreach (var p in bundle)
        {
            if (prices.GetPriceUsd(p.Id) is not { } price) return null;
            total += price;
        }
        return total;
    }
}
