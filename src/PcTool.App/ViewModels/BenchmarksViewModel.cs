using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTool.App.Services;
using PcTool.App.Views;
using PcTool.Benchmarks;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.App.ViewModels;

public sealed record BenchmarkRow(string Name, string Value, string Score, string Typical, string? Note, double Fraction);

public sealed record BenchmarkHistoryRow(string When, string CpuSingle, string CpuMulti, string Gpu, string Disk);

public sealed partial class BenchmarksViewModel : ObservableObject
{
    private readonly BenchmarkSuite _suite;
    private CancellationTokenSource? _cts;

    public AppState State { get; }
    public ObservableCollection<BenchmarkRow> Results { get; } = [];
    public ObservableCollection<BenchmarkHistoryRow> History { get; } = [];

    [ObservableProperty] private bool _runCpu = true;
    [ObservableProperty] private bool _runMemory = true;
    [ObservableProperty] private bool _runDisk = true;
    [ObservableProperty] private bool _runGpu = true;
    [ObservableProperty] private bool _quick;
    [ObservableProperty] private string _diskDirectory = Path.GetTempPath();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _resultsTitle = "No benchmark run yet";
    [ObservableProperty] private string _indexCaption =
        "Scores compare your PC to the Budget preset. Close games and heavy apps first; a full run takes about a minute.";

    public BenchmarksViewModel(AppState state, BenchmarkSuite suite)
    {
        State = state;
        _suite = suite;
        state.SpecsChanged += (_, _) =>
        {
            UpdateIndexCaption();
            ShowRun(state.Benchmarks.Latest);
            RefreshHistory();
        };
        UpdateIndexCaption();
        ShowRun(state.Benchmarks.Latest);
        RefreshHistory();
    }

    private void UpdateIndexCaption()
    {
        var scale = State.CompareScale;
        IndexCaption =
            $"{scale.IndexCaption} GPU tier scores are raster gaming (VRAM checked in Games). The stress test is a 1440p raymarch; the arena is a lighter raster scene. One stress window at a time. Close games and heavy apps first; a full run takes about a minute.";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        Error = null;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var options = new BenchmarkOptions
        {
            Cpu = RunCpu, Memory = RunMemory, Disk = RunDisk, Gpu = RunGpu,
            DiskDirectory = DiskDirectory, DurationScale = Quick ? 0.3 : 1,
        };
        try
        {
            var progress = new Progress<BenchmarkProgress>(p => { Progress = p.Fraction * 100; Stage = p.Stage; });
            var run = await _suite.RunAsync(options, State.Specs?.CpuName ?? "", State.Specs?.GpuName ?? "", progress, _cts.Token);
            State.Benchmarks.Add(run);
            RefreshHistory();
            State.RebuildSpecs();
            ShowRun(run);
            Stage = "Done";
        }
        catch (OperationCanceledException)
        {
            Stage = "Cancelled";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Stage = "Failed";
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void PreviewGpuArena()
    {
        GpuArenaPreviewWindow.ShowSingle(System.Windows.Application.Current.MainWindow);
    }

    private void RefreshHistory()
    {
        History.Clear();
        var scale = State.CompareScale;
        foreach (var r in State.Benchmarks.Runs.OrderByDescending(r => r.StartedAt).Take(10))
        {
            History.Add(new(r.StartedAt.ToLocalTime().ToString("g"),
                FmtCpu(scale, r.Get(BenchmarkKind.CpuSingleThread), BenchmarkKind.CpuSingleThread),
                FmtCpu(scale, r.Get(BenchmarkKind.CpuMultiThread), BenchmarkKind.CpuMultiThread),
                FmtGpu(scale, r.Get(BenchmarkKind.Gpu3D)),
                Fmt(r.Get(BenchmarkKind.DiskSequentialRead))));
        }

        static string Fmt(BenchmarkResult? b) => b?.Score is { } s ? s.ToString("F0") : "-";

        static string FmtCpu(CompareScale scale, BenchmarkResult? b, BenchmarkKind kind)
        {
            if (b?.Score is not { } s) return "-";
            return kind == BenchmarkKind.CpuSingleThread
                ? scale.IndexCpuSingle(s).ToString("F0")
                : scale.IndexCpuMulti(s).ToString("F0");
        }

        string FmtGpu(CompareScale scale, BenchmarkResult? b)
        {
            if (State.Specs?.GpuTier is { } gt) return scale.IndexGpu(gt.Score).ToString("F0");
            return b?.Score is { } s ? scale.IndexGpu(s).ToString("F0") : "-";
        }
    }

    private void ShowRun(BenchmarkRun? run)
    {
        Results.Clear();
        if (run is null) return;
        ResultsTitle = $"Results from {run.StartedAt.ToLocalTime():g}";
        var s = State.Specs;
        var scale = State.CompareScale;
        var presetLabel = scale.Preset.Label;

        foreach (var r in run.Results)
        {
            double? canonical = CanonicalScore(r, s);
            var displayScore = IndexScore(scale, r.Kind, canonical);
            var refLabel = r.Kind is BenchmarkKind.CpuSingleThread or BenchmarkKind.CpuMultiThread
                or BenchmarkKind.Gpu3D or BenchmarkKind.Gpu3DArena
                ? $"{presetLabel} 100"
                : "";

            string? note = null;
            if (displayScore is { } indexed && r.Kind is BenchmarkKind.CpuSingleThread or BenchmarkKind.CpuMultiThread
                or BenchmarkKind.Gpu3D or BenchmarkKind.Gpu3DArena)
            {
                note = indexed switch
                {
                    < 85 => r.Kind is BenchmarkKind.Gpu3D or BenchmarkKind.Gpu3DArena
                        ? $"Below the {presetLabel} reference. Check GPU temperatures, power plan, and that the monitor uses the graphics card."
                        : $"Below the {presetLabel} reference. Check CPU temperatures, power plan, and background apps.",
                    > 115 => $"Above the {presetLabel} reference.",
                    _ => $"In line with the {presetLabel} reference.",
                };
                if (r.Kind == BenchmarkKind.Gpu3D && s?.GpuTier is { } gt && r.Score is { } synth)
                {
                    var synthIndexed = scale.IndexGpu(synth);
                    var tierIndexed = scale.IndexGpu(gt.Score);
                    if (Math.Abs(synthIndexed - tierIndexed) > 10)
                        note = $"{note} Headless test {synthIndexed:F0} vs tier {tierIndexed:F0} (VRAM is separate).";
                }
                if (r.Kind == BenchmarkKind.Gpu3DArena && s?.GpuTier is { } gt2 && r.Score is { } arenaSynth)
                {
                    var arenaIndexed = scale.IndexGpu(arenaSynth);
                    var tierIndexed = scale.IndexGpu(gt2.Score);
                    if (Math.Abs(arenaIndexed - tierIndexed) > 10)
                        note = $"{note} Arena {arenaIndexed:F0} vs tier {tierIndexed:F0} (VRAM is separate).";
                }
            }

            Results.Add(new BenchmarkRow(
                Label(r.Kind),
                $"{r.Value:#,0.#} {r.Unit}",
                displayScore is { } sc ? sc.ToString("F0") : "-",
                refLabel,
                note,
                Math.Clamp((displayScore ?? 0) / 200, 0, 1)));
        }
    }

    private static double? CanonicalScore(BenchmarkResult r, SystemSpecs? specs) => r.Kind switch
    {
        BenchmarkKind.CpuSingleThread or BenchmarkKind.CpuMultiThread => r.Score,
        BenchmarkKind.Gpu3D when specs?.GpuTier is { } gt => gt.Score,
        BenchmarkKind.Gpu3D => r.Score,
        BenchmarkKind.Gpu3DArena => r.Score,
        _ => r.Score,
    };

    private static double? IndexScore(CompareScale scale, BenchmarkKind kind, double? canonical) => kind switch
    {
        BenchmarkKind.CpuSingleThread when canonical is { } c => scale.IndexCpuSingle(c),
        BenchmarkKind.CpuMultiThread when canonical is { } c => scale.IndexCpuMulti(c),
        BenchmarkKind.Gpu3D when canonical is { } c => scale.IndexGpu(c),
        BenchmarkKind.Gpu3DArena when canonical is { } c => scale.IndexGpu(c),
        _ => canonical,
    };

    private static string Label(BenchmarkKind k) => k switch
    {
        BenchmarkKind.CpuSingleThread => "CPU single-thread",
        BenchmarkKind.CpuMultiThread => "CPU multi-thread",
        BenchmarkKind.MemoryBandwidth => "Memory bandwidth",
        BenchmarkKind.MemoryLatency => "Memory latency",
        BenchmarkKind.DiskSequentialRead => "Disk sequential read",
        BenchmarkKind.DiskSequentialWrite => "Disk sequential write",
        BenchmarkKind.DiskRandomRead4K => "Disk 4K random read",
        BenchmarkKind.DiskRandomWrite4K => "Disk 4K random write",
        BenchmarkKind.Gpu3D => "GPU 3D headless (1440p)",
        BenchmarkKind.Gpu3DArena => "GPU 3D arena (1440p)",
        _ => k.ToString(),
    };
}
