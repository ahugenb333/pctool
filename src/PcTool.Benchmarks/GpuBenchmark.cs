using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>
/// Scored 1440p raymarch via the shared <see cref="GpuStressSession"/> (same scene as the live stress window).
/// </summary>
public static class GpuBenchmark
{
    public const int Width = GpuRaymarchScene.Width;
    public const int Height = GpuRaymarchScene.Height;
    // Calibrated as measuredFps * 100 / tierScore on an RTX 3070 (140).
    internal const double ReferenceFps = 46.2;

    public static BenchmarkResult Run(TimeSpan duration, CancellationToken ct) =>
        GpuStressHub.RunBenchmark(duration, ct);

    public static string DescribeAdapter()
    {
        using var adapter = D3D11GpuHelper.PickAdapter(out var name);
        return name;
    }
}
