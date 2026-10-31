using System.Diagnostics;
using PcTool.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PcTool.Benchmarks;

/// <summary>
/// Renders a raymarched scene (soft shadows, many SDF evaluations per pixel) offscreen at 2560x1440 with Direct3D 11
/// on the high-performance adapter. Event queries keep up to three frames in flight so the count reflects real GPU
/// completion, not CPU submission. Each frame waits on its GPU event query before counting so pipelining cannot
/// inflate FPS. Score is normalized so an RTX 3060 12GB = 100 on the tier raster index (calibrated from a known GPU).
/// </summary>
public static class GpuBenchmark
{
    public const int Width = GpuRaymarchScene.Width;
    public const int Height = GpuRaymarchScene.Height;
    // Implied RTX 3060 12GB FPS on this scene: ReferenceFps. Calibrated as measuredFps * 100 / tierScore on an RTX 3070 (140) at 64.7 FPS.
    internal const double ReferenceFps = 46.2;

    public static BenchmarkResult Run(TimeSpan duration, CancellationToken ct)
    {
        using var adapter = D3D11GpuHelper.PickAdapter(out _);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device, out _, out ID3D11DeviceContext? context).CheckError();

        using (device)
        using (context)
        using (var scene = new GpuRaymarchScene(device!))
        {
            var queries = Enumerable.Range(0, 3).Select(_ => device.CreateQuery(new QueryDescription(QueryType.Event))).ToArray();
            try
            {
                long frames = 0;

                void RenderFrame(float time)
                {
                    var slot = (int)(frames % queries.Length);
                    scene.Draw(context!, time);
                    context.End(queries[slot]);
                    context.Flush();
                    while (!context.IsDataAvailable(queries[slot])) Thread.SpinWait(32);
                    frames++;
                }

                var warmup = Stopwatch.StartNew();
                while (warmup.Elapsed < TimeSpan.FromSeconds(1.5)) RenderFrame((float)warmup.Elapsed.TotalSeconds);
                foreach (var q in queries) while (!context.IsDataAvailable(q)) Thread.SpinWait(32);

                frames = 0;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    RenderFrame((float)sw.Elapsed.TotalSeconds);
                }
                foreach (var q in queries) while (!context.IsDataAvailable(q)) Thread.SpinWait(32);
                var fps = frames / sw.Elapsed.TotalSeconds;

                return new BenchmarkResult(BenchmarkKind.Gpu3D, Math.Round(fps, 1), "FPS", Math.Round(fps / ReferenceFps * 100, 1),
                    new Dictionary<string, double> { ["Width"] = Width, ["Height"] = Height, ["Frames"] = frames });
            }
            finally
            {
                foreach (var q in queries) q.Dispose();
            }
        }
    }

    public static string DescribeAdapter()
    {
        using var adapter = D3D11GpuHelper.PickAdapter(out var name);
        return name;
    }
}
