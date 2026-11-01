using System.Diagnostics;
using PcTool.Core.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PcTool.Benchmarks;

/// <summary>
/// Runs the 1440p raymarch continuously. The FPS figure is GPU frame time. A half-resolution copy is published
/// about every 8 ms so the window can follow the monitor without reading back every GPU frame.
/// </summary>
public sealed class GpuStressSession : IDisposable
{
    public const int PreviewWidth = GpuRaymarchScene.Width / 2;
    public const int PreviewHeight = GpuRaymarchScene.Height / 2;

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();
    private readonly byte[] _preview = new byte[PreviewWidth * PreviewHeight * 4];
    private readonly byte[] _scratch = new byte[PreviewWidth * PreviewHeight * 4];
    private int _previewVersion;
    private double _fps;
    private Exception? _startError;
    private TimedBenchRequest? _timedBench;

    private sealed class TimedBenchRequest
    {
        public required TimeSpan Duration { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public required TaskCompletionSource<BenchmarkResult> Completion { get; init; }
        public Stopwatch WarmupClock { get; } = Stopwatch.StartNew();
        public Stopwatch? MeasureClock { get; set; }
        public long MeasureFrames { get; set; }
    }

    public GpuStressSession()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "PcTool GPU stress" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("GPU stress test did not start.");
        if (_startError is not null) throw _startError;
    }

    public double Fps
    {
        get { lock (_gate) return _fps; }
    }

    /// <summary>Copies the latest BGRA preview. Returns false until the first picture is ready.</summary>
    public bool CopyPreview(IntPtr destination, int destinationStride, out int version)
    {
        lock (_gate)
        {
            version = _previewVersion;
            if (version == 0) return false;
            unsafe
            {
                var dst = (byte*)destination;
                fixed (byte* src = _preview)
                {
                    var rowBytes = PreviewWidth * 4;
                    for (var y = 0; y < PreviewHeight; y++)
                        Buffer.MemoryCopy(src + y * rowBytes, dst + y * destinationStride, rowBytes, rowBytes);
                }
            }
            return true;
        }
    }

    /// <summary>Blocks until the render thread finishes warmup plus <paramref name="duration"/> (GPU-synced frames).</summary>
    public BenchmarkResult RunTimedBenchmark(TimeSpan duration, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<BenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_timedBench is not null)
                throw new InvalidOperationException("A timed GPU benchmark is already running.");
            _timedBench = new TimedBenchRequest { Duration = duration, CancellationToken = ct, Completion = tcs };
        }

        using (ct.Register(() => tcs.TrySetCanceled(ct)))
            return tcs.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            using var adapter = D3D11GpuHelper.PickAdapter(out _);
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device, out _, out ID3D11DeviceContext? context).CheckError();
            using (device)
            using (context)
            using (var scene = new GpuRaymarchScene(device!))
            using (var preview = new PreviewBlit(device!, scene))
            using (var query = device.CreateQuery(new QueryDescription(QueryType.Event)))
            {
                _ready.Set();
                var clock = Stopwatch.StartNew();
                var sample = Stopwatch.StartNew();
                var nextPreview = Stopwatch.StartNew();
                long frames = 0;
                long gpuTicks = 0;
                while (!_cts.IsCancellationRequested)
                {
                    var frame = Stopwatch.StartNew();
                    scene.Draw(context!, (float)clock.Elapsed.TotalSeconds);
                    context.End(query);
                    context.Flush();
                    while (!context.IsDataAvailable(query))
                    {
                        if (_cts.IsCancellationRequested) return;
                        Thread.SpinWait(64);
                    }
                    gpuTicks += frame.ElapsedTicks;
                    frames++;

                    TryAdvanceTimedBenchmark();

                    if (sample.Elapsed.TotalSeconds >= 1 && gpuTicks > 0)
                    {
                        var fps = frames / (gpuTicks / (double)Stopwatch.Frequency);
                        lock (_gate) _fps = fps;
                        frames = 0;
                        gpuTicks = 0;
                        sample.Restart();
                    }

                    if (nextPreview.Elapsed.TotalSeconds >= 0.008)
                    {
                        preview.Publish(context!, _scratch);
                        lock (_gate)
                        {
                            Buffer.BlockCopy(_scratch, 0, _preview, 0, _scratch.Length);
                            _previewVersion++;
                        }
                        nextPreview.Restart();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _startError = ex;
            _ready.Set();
        }
    }

    private void TryAdvanceTimedBenchmark()
    {
        TimedBenchRequest? req;
        lock (_gate) req = _timedBench;
        if (req is null) return;

        try
        {
            req.CancellationToken.ThrowIfCancellationRequested();
            if (req.WarmupClock.Elapsed < TimeSpan.FromSeconds(1.5)) return;

            req.MeasureClock ??= Stopwatch.StartNew();
            if (req.MeasureClock.Elapsed < req.Duration)
            {
                req.MeasureFrames++;
                return;
            }

            var fps = req.MeasureFrames / req.MeasureClock.Elapsed.TotalSeconds;
            var result = new BenchmarkResult(
                BenchmarkKind.Gpu3D,
                Math.Round(fps, 1),
                "FPS",
                Math.Round(fps / GpuBenchmark.ReferenceFps * 100, 1),
                new Dictionary<string, double>
                {
                    ["Width"] = GpuRaymarchScene.Width,
                    ["Height"] = GpuRaymarchScene.Height,
                    ["Frames"] = req.MeasureFrames,
                    ["SharedSession"] = 1,
                });

            lock (_gate) _timedBench = null;
            req.Completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            lock (_gate) _timedBench = null;
            req.Completion.TrySetException(ex);
        }
    }

    /// <summary>GPU scale of the 1440p target into a half-res BGRA image the window can upload.</summary>
    private sealed class PreviewBlit : IDisposable
    {
        private readonly ID3D11VertexShader _vs;
        private readonly ID3D11PixelShader _ps;
        private readonly ID3D11SamplerState _sampler;
        private readonly ID3D11Texture2D _target;
        private readonly ID3D11RenderTargetView _rtv;
        private readonly ID3D11Texture2D _staging;
        private readonly ID3D11ShaderResourceView _source;

        public PreviewBlit(ID3D11Device device, GpuRaymarchScene scene)
        {
            _source = scene.ColorView;
            _vs = device.CreateVertexShader(Compiler.Compile(BlitShader, "VSMain", "blit-vs.hlsl", "vs_5_0").Span);
            _ps = device.CreatePixelShader(Compiler.Compile(BlitShader, "PSMain", "blit-ps.hlsl", "ps_5_0").Span);
            _sampler = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxLOD = float.MaxValue,
            });
            _target = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, PreviewWidth, PreviewHeight, 1, 1, BindFlags.RenderTarget));
            _rtv = device.CreateRenderTargetView(_target);
            _staging = device.CreateTexture2D(new Texture2DDescription
            {
                Width = PreviewWidth,
                Height = PreviewHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
        }

        public unsafe void Publish(ID3D11DeviceContext context, byte[] destination)
        {
            context.OMSetRenderTargets(_rtv);
            context.RSSetViewport(new Viewport(PreviewWidth, PreviewHeight));
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vs);
            context.PSSetShader(_ps);
            context.PSSetShaderResource(0, _source);
            context.PSSetSampler(0, _sampler);
            context.Draw(3, 0);
            context.PSSetShaderResource(0, null!);
            context.CopyResource(_staging, _target);
            context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
            try
            {
                var rowBytes = PreviewWidth * 4;
                fixed (byte* dst = destination)
                {
                    var src = (byte*)mapped.DataPointer;
                    for (var y = 0; y < PreviewHeight; y++)
                        Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * rowBytes, rowBytes, rowBytes);
                }
            }
            finally
            {
                context.Unmap(_staging, 0);
            }
        }

        public void Dispose()
        {
            _staging.Dispose();
            _rtv.Dispose();
            _target.Dispose();
            _sampler.Dispose();
            _ps.Dispose();
            _vs.Dispose();
        }

        private const string BlitShader = """
            Texture2D sceneTex : register(t0);
            SamplerState sceneSamp : register(s0);
            struct VSOut { float4 pos : SV_Position; };
            VSOut VSMain(uint id : SV_VertexID)
            {
                VSOut o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            float4 PSMain(VSOut i) : SV_Target
            {
                float2 uv = i.pos.xy / float2(1280, 720);
                return sceneTex.Sample(sceneSamp, uv);
            }
            """;
    }
}
