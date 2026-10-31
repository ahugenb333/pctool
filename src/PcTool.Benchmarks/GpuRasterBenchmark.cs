using System.Diagnostics;
using PcTool.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PcTool.Benchmarks;

/// <summary>
/// Timed offscreen run of <see cref="GpuArenaScene"/> at 1440p (same visuals as the preview window).
/// </summary>
public static class GpuRasterBenchmark
{
    public const int Width = 2560;
    public const int Height = 1440;
    internal const double ReferenceFps = 120;

    public static BenchmarkResult Run(TimeSpan duration, CancellationToken ct)
    {
        using var adapter = D3D11GpuHelper.PickAdapter(out _);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device, out _, out ID3D11DeviceContext? context).CheckError();

        using (device)
        using (context)
        using (var scene = new GpuArenaScene(device!))
        {
            using var colorTarget = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
            using var depthTarget = device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, Width, Height, 1, 1, BindFlags.DepthStencil));
            using var rtv = device.CreateRenderTargetView(colorTarget);
            using var dsv = device.CreateDepthStencilView(depthTarget);

            var queries = Enumerable.Range(0, 3).Select(_ => device.CreateQuery(new QueryDescription(QueryType.Event))).ToArray();
            try
            {
                long frames = 0;
                void DrawScene(float time)
                {
                    var slot = (int)(frames % queries.Length);
                    scene.Draw(context!, rtv, dsv, Width, Height, time);
                    context.End(queries[slot]);
                    context.Flush();
                    while (!context.IsDataAvailable(queries[slot])) Thread.SpinWait(32);
                    frames++;
                }

                var warmup = Stopwatch.StartNew();
                while (warmup.Elapsed < TimeSpan.FromSeconds(1.5)) DrawScene((float)warmup.Elapsed.TotalSeconds);
                foreach (var q in queries) while (!context.IsDataAvailable(q)) Thread.SpinWait(32);

                frames = 0;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    DrawScene((float)sw.Elapsed.TotalSeconds);
                }
                foreach (var q in queries) while (!context.IsDataAvailable(q)) Thread.SpinWait(32);

                var fps = frames / sw.Elapsed.TotalSeconds;
                return new BenchmarkResult(BenchmarkKind.Gpu3DArena, Math.Round(fps, 1), "FPS",
                    Math.Round(fps / ReferenceFps * 100, 1),
                    new Dictionary<string, double>
                    {
                        ["Width"] = Width,
                        ["Height"] = Height,
                        ["Frames"] = frames,
                    });
            }
            finally
            {
                foreach (var q in queries) q.Dispose();
            }
        }
    }
}
