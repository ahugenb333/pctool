using PcTool.Core.Models;

namespace PcTool.Benchmarks;

public sealed class BenchmarkOptions
{
    public bool Cpu { get; init; } = true;
    public bool Memory { get; init; } = true;
    public bool Disk { get; init; } = true;
    public bool Gpu { get; init; } = true;
    /// <summary>Folder for the disk test file; defaults to the temp folder (usually on the system drive).</summary>
    public string? DiskDirectory { get; init; }
    public long DiskFileBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>Scales every timed window; 1.0 is the full run (~1 minute), smaller values give quicker, noisier results.</summary>
    public double DurationScale { get; init; } = 1.0;
}

public sealed class BenchmarkSuite
{
    public async Task<BenchmarkRun> RunAsync(BenchmarkOptions options, string cpuName, string gpuName,
        IProgress<BenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        var run = new BenchmarkRun { CpuName = cpuName, GpuName = gpuName };
        var stages = new List<(string Name, Func<IProgress<double>, IEnumerable<BenchmarkResult>> Work)>();
        TimeSpan Scaled(double seconds) => TimeSpan.FromSeconds(Math.Max(0.3, seconds * options.DurationScale));

        if (options.Cpu)
        {
            stages.Add(("CPU single-thread", p => [CpuBenchmark.RunSingleThread(Scaled(2.5), ct, p)]));
            stages.Add(("CPU multi-thread", p => [CpuBenchmark.RunMultiThread(Scaled(2.5), ct, p)]));
        }
        if (options.Memory)
        {
            stages.Add(("Memory bandwidth", _ => [MemoryBenchmark.RunBandwidth(Scaled(3), ct)]));
            stages.Add(("Memory latency", _ => [MemoryBenchmark.RunLatency(ct)]));
        }
        if (options.Disk)
        {
            var dir = options.DiskDirectory ?? Path.Combine(Path.GetTempPath(), "PcTool");
            stages.Add(("Disk", p => DiskBenchmark.Run(dir, options.DiskFileBytes, Scaled(4), ct, p)));
        }
        if (options.Gpu)
        {
            stages.Add(("GPU 3D headless", _ => [GpuBenchmark.Run(Scaled(8), ct)]));
            stages.Add(("GPU 3D arena", _ => [GpuRasterBenchmark.Run(Scaled(8), ct)]));
        }

        for (var i = 0; i < stages.Count; i++)
        {
            var (name, work) = stages[i];
            var index = i;
            progress?.Report(new BenchmarkProgress(name, (double)index / stages.Count));
            var inner = new Progress<double>(f => progress?.Report(new BenchmarkProgress(name, (index + f) / stages.Count)));
            try
            {
                var results = await Task.Factory.StartNew(() => work(inner).ToList(), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                run.Results.AddRange(results);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException)
            {
                // A failing stage (no D3D11 device, read-only disk) should not throw away the other results.
                progress?.Report(new BenchmarkProgress($"{name} failed: {ex.Message}", (index + 1d) / stages.Count));
            }
        }

        progress?.Report(new BenchmarkProgress("Done", 1));
        return run;
    }
}
