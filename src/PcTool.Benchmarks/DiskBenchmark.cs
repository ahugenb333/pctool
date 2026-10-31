using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PcTool.Core.Models;

namespace PcTool.Benchmarks;

/// <summary>
/// Sequential 1 MiB and random 4 KiB (queue depth 1) transfers against a temp file opened with
/// FILE_FLAG_NO_BUFFERING so the Windows cache does not inflate results.
/// </summary>
public static unsafe class DiskBenchmark
{
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const int SeqBlock = 1024 * 1024;
    private const int RandomBlock = 4096;

    // Reference: a typical PCIe 3.0 NVMe drive.
    private const double RefSeqRead = 3000, RefSeqWrite = 2500, RefRandRead = 50, RefRandWrite = 120;

    public static IReadOnlyList<BenchmarkResult> Run(string directory, long fileSizeBytes, TimeSpan randomDuration, CancellationToken ct, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"pctool-bench-{Guid.NewGuid():N}.tmp");
        var buffer = (byte*)NativeMemory.AlignedAlloc(SeqBlock, 4096);
        try
        {
            new Random(7).NextBytes(new Span<byte>(buffer, SeqBlock)); // incompressible, so drive-side compression cannot cheat
            var results = new List<BenchmarkResult>();

            using (var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, NoBuffering | FileOptions.WriteThrough, fileSizeBytes))
            {
                var seqWrite = Sequential(handle, buffer, fileSizeBytes, write: true, ct);
                results.Add(Result(BenchmarkKind.DiskSequentialWrite, seqWrite, RefSeqWrite));
                progress?.Report(0.25);

                var seqRead = Sequential(handle, buffer, fileSizeBytes, write: false, ct);
                results.Add(Result(BenchmarkKind.DiskSequentialRead, seqRead, RefSeqRead));
                progress?.Report(0.5);

                var (readMbps, readIops) = Random4K(handle, buffer, fileSizeBytes, write: false, randomDuration, ct);
                results.Add(Result(BenchmarkKind.DiskRandomRead4K, readMbps, RefRandRead, readIops));
                progress?.Report(0.75);

                var (writeMbps, writeIops) = Random4K(handle, buffer, fileSizeBytes, write: true, randomDuration, ct);
                results.Add(Result(BenchmarkKind.DiskRandomWrite4K, writeMbps, RefRandWrite, writeIops));
                progress?.Report(1);
            }
            return results;
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static BenchmarkResult Result(BenchmarkKind kind, double mbps, double reference, double? iops = null) =>
        new(kind, Math.Round(mbps, 1), "MB/s", Math.Round(mbps / reference * 100, 1),
            iops is null ? null : new Dictionary<string, double> { ["IOPS"] = Math.Round(iops.Value) });

    private static double Sequential(SafeFileHandle handle, byte* buffer, long size, bool write, CancellationToken ct)
    {
        var span = new Span<byte>(buffer, SeqBlock);
        var sw = Stopwatch.StartNew();
        for (long offset = 0; offset < size; offset += SeqBlock)
        {
            ct.ThrowIfCancellationRequested();
            if (write) RandomAccess.Write(handle, span, offset);
            else RandomAccess.Read(handle, span, offset);
        }
        return size / sw.Elapsed.TotalSeconds / 1e6;
    }

    private static (double Mbps, double Iops) Random4K(SafeFileHandle handle, byte* buffer, long size, bool write, TimeSpan duration, CancellationToken ct)
    {
        var span = new Span<byte>(buffer, RandomBlock);
        var blocks = size / RandomBlock;
        var rng = new Random(99);
        long ops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            var offset = rng.NextInt64(blocks) * RandomBlock;
            if (write) RandomAccess.Write(handle, span, offset);
            else RandomAccess.Read(handle, span, offset);
            ops++;
        }
        var iops = ops / sw.Elapsed.TotalSeconds;
        return (iops * RandomBlock / 1e6, iops);
    }
}
