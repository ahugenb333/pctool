using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>One shared <see cref="GpuStressSession"/> for the stress window and the scored GPU benchmark.</summary>
public static class GpuStressHub
{
    private static readonly object Gate = new();
    private static GpuStressSession? _session;
    private static int _refs;

    /// <summary>When true, <see cref="SessionAvailable"/> is raised at the start of a timed benchmark.</summary>
    public static bool ShowWindowDuringBenchmark { get; set; } = true;

    /// <summary>Raised on the benchmark thread; handlers should open the preview on the UI thread.</summary>
    public static event Action? SessionAvailable;

    /// <summary>Raised after the scored GPU stress stage finishes; close benchmark-only preview windows on the UI thread.</summary>
    public static event Action? BenchmarkGpuStressFinished;

    public static GpuStressSession Acquire()
    {
        lock (Gate)
        {
            _refs++;
            _session ??= new GpuStressSession();
            return _session;
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (_refs <= 0) return;
            _refs--;
            if (_refs == 0)
            {
                _session?.Dispose();
                _session = null;
            }
        }
    }

    public static BenchmarkResult RunBenchmark(TimeSpan duration, CancellationToken ct)
    {
        Acquire();
        try
        {
            SessionAvailable?.Invoke();
            return _session!.RunTimedBenchmark(duration, ct);
        }
        finally
        {
            Release();
            BenchmarkGpuStressFinished?.Invoke();
        }
    }

    public static BenchmarkResult RunPreviewBenchmark(TimeSpan duration, CancellationToken ct)
    {
        Acquire();
        try
        {
            return _session!.RunTimedPreviewBenchmark(duration, ct);
        }
        finally
        {
            Release();
        }
    }
}
