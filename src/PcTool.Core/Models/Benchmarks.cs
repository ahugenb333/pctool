namespace PcTool.Core.Models;

public enum BenchmarkKind
{
    CpuSingleThread,
    CpuMultiThread,
    MemoryBandwidth,
    MemoryLatency,
    DiskSequentialRead,
    DiskSequentialWrite,
    DiskRandomRead4K,
    DiskRandomWrite4K,
    Gpu3D
}

public sealed record BenchmarkResult(
    BenchmarkKind Kind,
    double Value,
    string Unit,
    /// <summary>Normalized score where the reference system scores 100 (see each benchmark for its reference).</summary>
    double? Score,
    IReadOnlyDictionary<string, double>? Details = null);

public sealed class BenchmarkRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public string CpuName { get; init; } = "";
    public string GpuName { get; init; } = "";
    public List<BenchmarkResult> Results { get; init; } = [];

    public BenchmarkResult? Get(BenchmarkKind kind) => Results.FirstOrDefault(r => r.Kind == kind);
}

public sealed record BenchmarkProgress(string Stage, double Fraction);
