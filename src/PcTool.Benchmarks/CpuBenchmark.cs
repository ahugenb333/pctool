using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>
/// Integer hashing, floating-point Mandelbrot and Brotli compression, each timed over a fixed window.
/// Throughput is normalized so a Ryzen 5 5600X scores ~100 single- and multi-thread, matching the tier database.
/// </summary>
public static class CpuBenchmark
{
    // Iterations/second of each workload on the reference CPU (Ryzen 5 5600X equivalents, calibrated on a 5800XT).
    // Measured from a Release build; Debug builds run the integer and floating-point workloads ~4x slower.
    internal static readonly double[] SingleReference = [2634.4, 402.0, 354.3];
    internal static readonly double[] MultiReference = [26127, 3880.2, 2127.0];

    private static readonly Func<long>[] Workloads = [IntegerHash, Mandelbrot, Compress];
    private static readonly string[] WorkloadNames = ["Integer", "Floating point", "Compression"];

    public static BenchmarkResult RunSingleThread(TimeSpan perWorkload, CancellationToken ct, IProgress<double>? progress = null)
    {
        var rates = new double[Workloads.Length];
        for (var i = 0; i < Workloads.Length; i++)
        {
            rates[i] = Measure(Workloads[i], 1, perWorkload, ct);
            progress?.Report((i + 1d) / Workloads.Length);
        }
        return ToResult(BenchmarkKind.CpuSingleThread, rates, SingleReference);
    }

    public static BenchmarkResult RunMultiThread(TimeSpan perWorkload, CancellationToken ct, IProgress<double>? progress = null)
    {
        var threads = Environment.ProcessorCount;
        var rates = new double[Workloads.Length];
        for (var i = 0; i < Workloads.Length; i++)
        {
            rates[i] = Measure(Workloads[i], threads, perWorkload, ct);
            progress?.Report((i + 1d) / Workloads.Length);
        }
        return ToResult(BenchmarkKind.CpuMultiThread, rates, MultiReference, threads);
    }

    private static BenchmarkResult ToResult(BenchmarkKind kind, double[] rates, double[] reference, int threads = 1)
    {
        var logSum = 0d;
        var details = new Dictionary<string, double> { ["Threads"] = threads };
        for (var i = 0; i < rates.Length; i++)
        {
            var ratio = rates[i] / reference[i];
            logSum += Math.Log(ratio);
            details[WorkloadNames[i] + " (ops/s)"] = Math.Round(rates[i], 1);
        }
        var score = Math.Exp(logSum / rates.Length) * 100;
        return new BenchmarkResult(kind, Math.Round(score), "points", Math.Round(score, 1), details);
    }

    /// <summary>Runs <paramref name="work"/> on <paramref name="threads"/> dedicated threads; returns total iterations per second.</summary>
    private static double Measure(Func<long> work, int threads, TimeSpan duration, CancellationToken ct)
    {
        work(); // warm up JIT and caches

        var counts = new long[threads];
        var start = new ManualResetEventSlim();
        var deadline = 0L;
        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var index = t;
            workers[t] = new Thread(() =>
            {
                start.Wait();
                long done = 0, sink = 0;
                while (Stopwatch.GetTimestamp() < Volatile.Read(ref deadline) && !ct.IsCancellationRequested)
                {
                    sink ^= work();
                    done++;
                }
                counts[index] = done + (sink == long.MinValue ? 1 : 0);
            }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            workers[t].Start();
        }

        var began = Stopwatch.GetTimestamp();
        Volatile.Write(ref deadline, began + (long)(duration.TotalSeconds * Stopwatch.Frequency));
        start.Set();
        foreach (var w in workers) w.Join();
        ct.ThrowIfCancellationRequested();

        var elapsed = Stopwatch.GetElapsedTime(began).TotalSeconds;
        return counts.Sum() / elapsed;
    }

    [ThreadStatic] private static ulong[]? _hashBuffer;

    private static long IntegerHash()
    {
        var buffer = _hashBuffer ??= Enumerable.Range(0, 32 * 1024).Select(i => (ulong)i * 0x9E3779B97F4A7C15UL).ToArray();
        ulong h = 0xCBF29CE484222325UL;
        for (var round = 0; round < 4; round++)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var v = buffer[i] ^ h;
                v ^= v >> 33;
                v *= 0xFF51AFD7ED558CCDUL;
                v ^= v >> 29;
                h = BitOperations.RotateLeft(h ^ v, 27) * 0x100000001B3UL + (ulong)i;
            }
        }
        return (long)h;
    }

    private static long Mandelbrot()
    {
        const int size = 160, maxIter = 200;
        long total = 0;
        for (var py = 0; py < size; py++)
        {
            var y0 = py * 2.4 / size - 1.2;
            for (var px = 0; px < size; px++)
            {
                double x0 = px * 3.2 / size - 2.2, x = 0, y = 0;
                var iter = 0;
                while (x * x + y * y <= 4 && iter < maxIter)
                {
                    var xt = x * x - y * y + x0;
                    y = 2 * x * y + y0;
                    x = xt;
                    iter++;
                }
                total += iter;
            }
        }
        return total;
    }

    [ThreadStatic] private static byte[]? _compressInput;
    [ThreadStatic] private static byte[]? _compressOutput;

    private static long Compress()
    {
        var input = _compressInput ??= CompressibleData(256 * 1024);
        var output = _compressOutput ??= new byte[BrotliEncoder.GetMaxCompressedLength(input.Length)];
        BrotliEncoder.TryCompress(input, output, out var written, quality: 4, window: 22);
        return written;
    }

    private static byte[] CompressibleData(int length)
    {
        var rng = new Random(42);
        var words = new[] { "pc", "tool", "frame", "shader", "texture", "benchmark", "cache", "thread", "memory", "pixel", " ", " ", "\n" };
        var bytes = new byte[length];
        var pos = 0;
        while (pos < length)
        {
            var w = words[rng.Next(words.Length)];
            for (var i = 0; i < w.Length && pos < length; i++) bytes[pos++] = (byte)w[i];
            if (rng.Next(8) == 0 && pos < length) bytes[pos++] = (byte)rng.Next(256);
        }
        return bytes;
    }
}
