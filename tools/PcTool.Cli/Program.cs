using PcTool.Benchmarks;
using PcTool.Compatibility;
using PcTool.Core.Models;
using PcTool.Data;
using PcTool.Diagnostics;
using PcTool.Games;
using PcTool.Hardware;
using PcTool.Upgrades;

// Developer CLI for exercising the engines without the UI: profile, bench, games, upgrades, diag, check.
var command = args.FirstOrDefault() ?? "profile";
var catalogs = EmbeddedCatalogs.Load();
var specsBuilder = new SystemSpecsBuilder(catalogs.Tiers, catalogs.Chipsets);
var gameAdvisor = new GameAdvisor(catalogs.Games, catalogs.Tiers);
var compat = new CompatibilityEngine(catalogs.Tiers, catalogs.Chipsets);

string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
int? IntOpt(string name) => int.TryParse(Opt(name), out var v) ? v : null;

var overrides = new UserSystemOverrides
{
    Chipset = Opt("--chipset"), PsuWatts = IntOpt("--psu"), CaseFormFactor = Opt("--case"),
    CaseMaxGpuLengthMm = IntOpt("--gpu-len"), FreeM2Slots = IntOpt("--m2"),
};

async Task<(HardwareProfile, SystemSpecs)> Detect()
{
    var profile = await new WindowsHardwareProfiler().CaptureAsync();
    return (profile, specsBuilder.Build(profile, overrides));
}

switch (command)
{
    case "profile":
    {
        var (profile, specs) = await Detect();
        Console.WriteLine(ProfileExporter.ToJson(profile));
        PrintSpecs(specs);
        break;
    }
    case "bench":
    {
        var quick = args.Contains("--quick");
        var options = new BenchmarkOptions { DurationScale = quick ? 0.3 : 1, Disk = !args.Contains("--no-disk"), Gpu = !args.Contains("--no-gpu") };
        var run = await new BenchmarkSuite().RunAsync(options, "", "",
            new Progress<BenchmarkProgress>(p => Console.Error.WriteLine($"[{p.Fraction:P0}] {p.Stage}")));
        foreach (var r in run.Results)
        {
            Console.WriteLine($"{r.Kind,-22} {r.Value,10} {r.Unit,-7} score {r.Score}");
            if (r.Details is not null)
                foreach (var (k, v) in r.Details) Console.WriteLine($"    {k}: {v}");
        }
        break;
    }
    case "games":
    {
        var (_, specs) = await Detect();
        PrintSpecs(specs);
        foreach (var v in gameAdvisor.EvaluateAll(specs).OrderByDescending(v => v.Level).ThenBy(v => v.Game.Name))
        {
            Console.WriteLine($"{v.Level,-16} {v.Game.Name,-36} {v.SuggestedSettings,-14} gpu x{v.GpuRatio:F2} cpu x{v.CpuRatio:F2}  {v.Bottleneck}");
            foreach (var n in v.Notes) Console.WriteLine($"{"",18}- {n}");
        }
        break;
    }
    case "upgrades":
    {
        var (_, specs) = await Detect();
        PrintSpecs(specs);
        var goal = Enum.TryParse<UpgradeGoal>(Opt("--goal"), true, out var g) ? g : UpgradeGoal.Gaming1440p;
        var options = new UpgradeOptions(goal, IntOpt("--budget") ?? 600, Opt("--game"));
        var advisor = new UpgradeAdvisor(catalogs.Parts, catalogs.Prices, catalogs.Games, compat, gameAdvisor);
        var plan = advisor.Recommend(specs, options);
        Console.WriteLine($"\nGoal: {advisor.GoalLabel(options)}  budget ${options.BudgetUsd}");
        Console.WriteLine(plan.Bottleneck.Summary);
        Console.WriteLine("  " + string.Join("  ", plan.Bottleneck.Ratios.Select(r => $"{r.Component} {r.Ratio:P0}")));
        Console.WriteLine($"Good: {plan.Good?.Title}  Better: {plan.Better?.Title}  Best: {plan.Best?.Title}\n");
        foreach (var c in plan.Candidates.Take(20))
        {
            Console.WriteLine($"{c.Overall,-7} +{c.GainPercent,5:F1}%  ${c.PriceUsd,-6} {c.GainPer100Usd,5:F1}/$100  {c.Title}  [{string.Join(" + ", c.Parts.Select(p => p.DisplayName))}]");
            Console.WriteLine($"{"",9}{c.Reason}");
            if (c.GamesUnlocked.Count > 0) Console.WriteLine($"{"",9}Unlocks: {string.Join(", ", c.GamesUnlocked)}");
        }
        break;
    }
    case "check":
    {
        var (_, specs) = await Detect();
        var query = Opt("--part") ?? "";
        foreach (var part in catalogs.Parts.Parts.Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10))
        {
            var report = compat.Check(part, specs);
            Console.WriteLine($"{report.Overall,-7} {part.Category,-11} {part.DisplayName}");
            foreach (var c in report.Checks) Console.WriteLine($"{"",9}{c.Status,-7} {c.Rule}: {c.Message}");
        }
        break;
    }
    case "diag":
    {
        var (profile, specs) = await Detect();
        var game = Opt("--game") is { } id ? catalogs.Games.Get(id) : null;
        using var sensors = new LibreSensorMonitor();
        var findings = await new GameDiagnostics(sensors, gameAdvisor).RunAsync(new DiagnosticsRequest(game, Opt("--exe"), specs, profile));
        foreach (var f in findings)
        {
            Console.WriteLine($"{f.Severity,-8} [{f.Category}] {f.Title}");
            Console.WriteLine($"{"",9}{f.Detail}");
            if (f.SuggestedFix is not null) Console.WriteLine($"{"",9}Fix: {f.SuggestedFix}");
        }
        break;
    }
    default:
        Console.WriteLine("usage: pctool-cli profile | bench [--quick] [--no-disk] [--no-gpu] | games | upgrades [--goal G] [--budget N] [--game id]");
        Console.WriteLine("                  | check --part text | diag [--game id] [--exe name.exe]");
        Console.WriteLine("overrides: --chipset B550 --psu 750 --case ATX --gpu-len 330 --m2 1");
        break;
}

static void PrintSpecs(SystemSpecs s)
{
    Console.WriteLine();
    Console.WriteLine($"CPU tier: {s.CpuTier?.Name ?? "(none)"}  single {s.CpuSingleScore} multi {s.CpuMultiScore} gaming {s.CpuGamingScore:F0}");
    Console.WriteLine($"GPU tier: {s.GpuTier?.Name ?? "(none)"}  score {s.GpuScore} vram {s.VramGb}GB");
    Console.WriteLine($"Socket {s.Socket}  chipset {s.Chipset?.Name ?? "(unknown)"}  {s.MemoryType} {s.RamGb}GB  slots {s.RamSlotsUsed}/{s.RamSlots} max {s.MaxRamGb}GB");
    Console.WriteLine($"System drive {s.SystemDriveKind}  SSD {s.HasSsd}  NVMe {s.HasNvme}  free {s.LargestFreeSpaceGb:F0}GB  PSU {s.PsuWatts?.ToString() ?? "?"}W");
}
