using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTool.App.Services;
using PcTool.Benchmarks;
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

    public BenchmarksViewModel(AppState state, BenchmarkSuite suite)
    {
        State = state;
        _suite = suite;
        state.SpecsChanged += (_, _) => ShowRun(state.Benchmarks.Latest);
        ShowRun(state.Benchmarks.Latest);
        RefreshHistory();
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

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var r in State.Benchmarks.Runs.OrderByDescending(r => r.StartedAt).Take(10))
        {
            History.Add(new(r.StartedAt.ToLocalTime().ToString("g"),
                Fmt(r.Get(BenchmarkKind.CpuSingleThread)), Fmt(r.Get(BenchmarkKind.CpuMultiThread)),
                Fmt(r.Get(BenchmarkKind.Gpu3D)), Fmt(r.Get(BenchmarkKind.DiskSequentialRead))));
        }

        static string Fmt(BenchmarkResult? b) => b?.Score is { } s ? s.ToString("F0") : "-";
    }

    private void ShowRun(BenchmarkRun? run)
    {
        Results.Clear();
        if (run is null) return;
        ResultsTitle = $"Results from {run.StartedAt.ToLocalTime():g}";
        var s = State.Specs;

        foreach (var r in run.Results)
        {
            double? typical = r.Kind switch
            {
                BenchmarkKind.CpuSingleThread => s?.CpuTier?.SingleScore,
                BenchmarkKind.CpuMultiThread => s?.CpuTier?.MultiScore,
                BenchmarkKind.Gpu3D => s?.GpuTier?.Score,
                _ => null,
            };
            var displayScore = r.Kind == BenchmarkKind.Gpu3D && s?.GpuTier is { } gt ? gt.Score : r.Score;
            string? note = null;
            if (typical is { } t && displayScore is { } score)
            {
                var ratio = score / t;
                note = ratio switch
                {
                    < 0.85 => r.Kind == BenchmarkKind.Gpu3D
                        ? "Well below typical. Check GPU temperatures, the power plan, and that the monitor is plugged into the graphics card."
                        : "Well below typical. Check CPU temperatures, the power plan, and close background apps.",
                    > 1.15 => r.Kind == BenchmarkKind.Gpu3D
                        ? "Above typical on the raster index."
                        : "Above typical - nicely tuned or overclocked.",
                    _ => "In line with typical results for this part.",
                };
                if (r.Kind == BenchmarkKind.Gpu3D && r.Score is { } synth && Math.Abs(synth - t) > 10)
                    note = $"{note} This raymarch test scored {synth:F0}; tier index {t:F0} (VRAM is separate).";
            }
            Results.Add(new BenchmarkRow(
                Label(r.Kind),
                $"{r.Value:#,0.#} {r.Unit}",
                displayScore is { } sc ? sc.ToString("F0") : "-",
                typical is { } tt ? $"typical {tt:F0}" : "",
                note,
                Math.Clamp((displayScore ?? 0) / 300, 0, 1)));
        }
    }

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
        BenchmarkKind.Gpu3D => "GPU 3D (1440p)",
        _ => k.ToString(),
    };
}
