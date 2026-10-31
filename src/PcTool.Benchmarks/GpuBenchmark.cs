using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using PcTool.Core.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PcTool.Benchmarks;

/// <summary>
/// Renders a raymarched scene (soft shadows, many SDF evaluations per pixel) offscreen at 2560x1440 with Direct3D 11
/// on the high-performance adapter. Event queries keep up to three frames in flight so the count reflects real GPU
/// completion, not CPU submission. Each frame waits on its GPU event query before counting so pipelining cannot
/// inflate FPS. Score is normalized so an RTX 3060 12GB = 100 on the tier raster index (calibrated from a known GPU).
/// </summary>
public static class GpuBenchmark
{
    public const int Width = 2560;
    public const int Height = 1440;
    // Implied RTX 3060 12GB FPS on this scene: ReferenceFps. Calibrated as measuredFps * 100 / tierScore on an RTX 3070 (140).
    internal const double ReferenceFps = 261.6;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public float Time;
        public Vector3 Padding;
        public Vector2 Resolution;
        public Vector2 Padding2;
    }

    public static BenchmarkResult Run(TimeSpan duration, CancellationToken ct)
    {
        using var adapter = PickAdapter(out var adapterName);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device, out _, out ID3D11DeviceContext? context).CheckError();

        using (device)
        using (context)
        {
            using var target = device!.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
            using var rtv = device.CreateRenderTargetView(target);
            using var vs = device.CreateVertexShader(Compiler.Compile(Shader, "VSMain", "bench.hlsl", "vs_5_0").Span);
            using var ps = device.CreatePixelShader(Compiler.Compile(Shader, "PSMain", "bench.hlsl", "ps_5_0").Span);
            using var constants = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer));

            var queries = Enumerable.Range(0, 3).Select(_ => device.CreateQuery(new QueryDescription(QueryType.Event))).ToArray();
            try
            {
                context!.OMSetRenderTargets(rtv);
                context.RSSetViewport(new Viewport(Width, Height));
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.VSSetShader(vs);
                context.PSSetShader(ps);
                context.PSSetConstantBuffer(0, constants);

                var frame = new FrameConstants { Resolution = new Vector2(Width, Height) };
                long frames = 0;

                void RenderFrame(float time)
                {
                    var slot = (int)(frames % queries.Length);
                    frame.Time = time;
                    context.UpdateSubresource(in frame, constants);
                    context.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
                    context.Draw(3, 0);
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
        using var adapter = PickAdapter(out var name);
        return name;
    }

    private static IDXGIAdapter1 PickAdapter(out string name)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();
        IDXGIAdapter1? adapter = null;
        if (factory6 is not null)
        {
            factory6.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out adapter);
        }
        if (adapter is null) factory.EnumAdapters1(0, out adapter).CheckError();
        name = adapter!.Description1.Description.Trim();
        return adapter;
    }

    private const string Shader = """
        cbuffer Frame : register(b0) { float time; float3 pad; float2 resolution; float2 pad2; };

        struct VSOut { float4 pos : SV_Position; };

        VSOut VSMain(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float scene(float3 p, float t)
        {
            float d = p.y + 1.0 + 0.08 * sin(p.x * 2.0 + t) * cos(p.z * 2.0);
            [loop] for (int i = 0; i < 10; i++)
            {
                float a = i * 0.628 + t * 0.3;
                float3 c = float3(cos(a) * 2.6, sin(t + i) * 0.5, sin(a) * 2.6 + 5.0);
                d = min(d, length(p - c) - 0.55);
            }
            float3 q = p - float3(0, 0.1, 5.0);
            d = min(d, length(max(abs(q) - 0.7, 0.0)) - 0.12);
            return d;
        }

        float3 normalAt(float3 p, float t)
        {
            float2 e = float2(0.001, 0);
            return normalize(float3(scene(p + e.xyy, t) - scene(p - e.xyy, t),
                                    scene(p + e.yxy, t) - scene(p - e.yxy, t),
                                    scene(p + e.yyx, t) - scene(p - e.yyx, t)));
        }

        float softShadow(float3 ro, float3 rd, float t)
        {
            float res = 1.0, d = 0.02;
            [loop] for (int i = 0; i < 40; i++)
            {
                float h = scene(ro + rd * d, t);
                res = min(res, 8.0 * h / d);
                d += clamp(h, 0.02, 0.4);
                if (h < 0.001 || d > 20.0) break;
            }
            return saturate(res);
        }

        float4 PSMain(VSOut i) : SV_Target
        {
            float2 uv = (2.0 * i.pos.xy - resolution) / resolution.y;
            float3 ro = float3(0, 0.6, -0.5);
            float3 rd = normalize(float3(uv.x, -uv.y, 1.6));
            float d = 0, hit = 0;
            [loop] for (int s = 0; s < 128; s++)
            {
                float h = scene(ro + rd * d, time);
                if (h < 0.0008) { hit = 1; break; }
                d += h;
                if (d > 40.0) break;
            }
            float3 sky = float3(0.10, 0.12, 0.18) + rd.y * 0.2;
            float3 col = sky;
            if (hit > 0)
            {
                float3 p = ro + rd * d;
                float3 n = normalAt(p, time);
                float3 l = normalize(float3(0.6, 0.8, -0.4));
                float dif = saturate(dot(n, l)) * softShadow(p + n * 0.01, l, time);
                float spec = pow(saturate(dot(reflect(-l, n), -rd)), 32.0);
                col = float3(0.85, 0.55, 0.35) * dif + 0.12 + spec * 0.6;
                col = lerp(col, sky, 1.0 - exp(-0.01 * d * d));
            }
            return float4(pow(saturate(col), 0.4545), 1);
        }
        """;
}
