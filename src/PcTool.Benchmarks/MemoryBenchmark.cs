using System.Diagnostics;
using System.Runtime.InteropServices;
using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>Multi-threaded copy bandwidth and single-threaded pointer-chasing latency over buffers far larger than any CPU cache.</summary>
public static unsafe class MemoryBenchmark
{
    private const double ReferenceBandwidthGbps = 40;  // dual-channel DDR4-3200, read + write
    private const double ReferenceLatencyNs = 80;

    public static BenchmarkResult RunBandwidth(TimeSpan duration, CancellationToken ct)
    {
        const long size = 256L * 1024 * 1024;
        var src = (byte*)NativeMemory.AlignedAlloc((nuint)size, 4096);
        var dst = (byte*)NativeMemory.AlignedAlloc((nuint)size, 4096);
        try
        {
            NativeMemory.Fill(src, (nuint)size, 0x5A);
            NativeMemory.Clear(dst, (nuint)size);

            var threads = Math.Min(Environment.ProcessorCount, 16);
            var chunk = size / threads;
            long bytes = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
                {
                    Buffer.MemoryCopy(src + t * chunk, dst + t * chunk, chunk, chunk);
                });
                bytes += size * 2; // each copied byte is read once and written once
            }
            var gbps = bytes / sw.Elapsed.TotalSeconds / 1e9;
            return new BenchmarkResult(BenchmarkKind.MemoryBandwidth, Math.Round(gbps, 1), "GB/s", Math.Round(gbps / ReferenceBandwidthGbps * 100, 1));
        }
        finally
        {
            NativeMemory.AlignedFree(src);
            NativeMemory.AlignedFree(dst);
        }
    }

    public static BenchmarkResult RunLatency(CancellationToken ct)
    {
        const int count = 32 * 1024 * 1024; // 128 MB of int32 indices
        var next = (int*)NativeMemory.AlignedAlloc((nuint)count * sizeof(int), 4096);
        try
        {
            // Sattolo's algorithm: one random cycle through every slot, so the prefetcher cannot help.
            for (var i = 0; i < count; i++) next[i] = i;
            var rng = new Random(1234);
            for (var i = count - 1; i > 0; i--)
            {
                var j = rng.Next(i);
                (next[i], next[j]) = (next[j], next[i]);
            }
            ct.ThrowIfCancellationRequested();

            const int steps = 20_000_000;
            var p = 0;
            for (var i = 0; i < 1_000_000; i++) p = next[p];
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < steps; i++) p = next[p];
            sw.Stop();
            GC.KeepAlive(p);

            var ns = sw.Elapsed.TotalNanoseconds / steps;
            return new BenchmarkResult(BenchmarkKind.MemoryLatency, Math.Round(ns, 1), "ns", Math.Round(ReferenceLatencyNs / ns * 100, 1));
        }
        finally
        {
            NativeMemory.AlignedFree(next);
        }
    }
}
