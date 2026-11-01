using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTool.App.Services;
using PcTool.App.Views;
using PcTool.Benchmarks;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.App.ViewModels;

public sealed record BenchmarkRow(string Name, string Value, string Score, string Typical, string? Note, double Fraction, bool ShowIndexBar);

public sealed record BenchmarkHistoryRow(string When, string CpuSingle, string CpuMulti, string Gpu, string Disk);

public sealed partial class BenchmarksViewModel : ObservableObject
{
    private readonly BenchmarkSuite _suite;
    private CancellationTokenSource? _cts;

    public AppState State { get; }
    public ObservableCollection<BenchmarkRow> Results { get; } = [];

    public string ComparePresetId
    {
        get => State.ComparePresetId;
        set => State.ComparePresetId = value;
    }
    public ObservableCollection<BenchmarkHistoryRow> History { get; } = [];

    [ObservableProperty] private bool _runCpu = true;
    [ObservableProperty] private bool _runMemory = true;
    [ObservableProperty] private bool _runDisk = true;
    [ObservableProperty] private bool _runGpu = true;
    [ObservableProperty] private bool _showGpuStressDuringRun = true;
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
        state.SpecsChanged += (_, _) => RefreshDisplayedResults();
        state.PropertyChanged += OnStatePropertyChanged;
        RefreshDisplayedResults();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.ComparePresetId))
            OnPropertyChanged(nameof(ComparePresetId));
        if (e.PropertyName is nameof(AppState.CompareScale) or nameof(AppState.ComparePresetId))
            RefreshDisplayedResults();
    }

    private void RefreshDisplayedResults()
    {
        UpdateIndexCaption();
        ShowRun(State.Benchmarks.Latest);
        RefreshHistory();
        OnPropertyChanged(nameof(Results));
    }

    private void UpdateIndexCaption()
    {
        var scale = State.CompareScale;
        IndexCaption =
            $"{scale.IndexCaption} GPU tier scores are raster gaming (VRAM checked in Games). The scored GPU test uses the same 1440p raymarch as View GPU stress test. Close games and heavy apps first; a full run takes about a minute.";
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
            ShowGpuStressDuringRun = ShowGpuStressDuringRun,
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
            var displayScore = PresetIndexedScore(scale, r);
            var refLabel = displayScore is not null ? $"{presetLabel} 100" : "";

            var showBar = displayScore is not null;
            string? note = null;
            if (r.Kind == BenchmarkKind.Gpu3DArena)
                note =
                    "Preview frames sent to the viewport (same rate as the stress window counter). Not used for tier scoring — use GPU 3D stress for that.";
            else if (displayScore is { } indexed && r.Kind is BenchmarkKind.CpuSingleThread or BenchmarkKind.CpuMultiThread or BenchmarkKind.Gpu3D)
            {
                note = indexed switch
                {
                    < 85 => r.Kind == BenchmarkKind.Gpu3D
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
                        note = $"{note} Stress test {synthIndexed:F0} vs tier {tierIndexed:F0} (VRAM is separate).";
                }
            }

            Results.Add(new BenchmarkRow(
                Label(r.Kind),
                FormatValue(r),
                displayScore is { } sc ? sc.ToString("F0") : "—",
                refLabel,
                note,
                Math.Clamp((displayScore ?? 0) / 200, 0, 1),
                showBar));
        }
    }

    /// <summary>Preset-relative index for the score column (CPU + GPU stress only).</summary>
    private static double? PresetIndexedScore(CompareScale scale, BenchmarkResult r) => r.Kind switch
    {
        BenchmarkKind.CpuSingleThread when r.Score is { } s => scale.IndexCpuSingle(s),
        BenchmarkKind.CpuMultiThread when r.Score is { } s => scale.IndexCpuMulti(s),
        BenchmarkKind.Gpu3D when r.Score is { } s => scale.IndexGpu(s),
        _ => null,
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
        BenchmarkKind.Gpu3D => "GPU 3D stress (1440p)",
        BenchmarkKind.Gpu3DArena => "GPU preview (viewport)",
        _ => k.ToString(),
    };

    private static string FormatValue(BenchmarkResult r) => $"{r.Value:#,0.#} {r.Unit}";
}
