using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>
/// Timed count of preview frames from <see cref="GpuStressSession"/> (same output as the stress window viewport).
/// </summary>
public static class GpuRasterBenchmark
{
    public const int Width = GpuStressSession.PreviewWidth;
    public const int Height = GpuStressSession.PreviewHeight;

    public static BenchmarkResult Run(TimeSpan duration, CancellationToken ct) =>
        GpuStressHub.RunPreviewBenchmark(duration, ct);
}
