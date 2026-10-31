using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PcTool.Benchmarks;

/// <summary>The 1440p raymarch used by the scored headless test and the live stress view.</summary>
internal sealed class GpuRaymarchScene : IDisposable
{
    public const int Width = 2560;
    public const int Height = 1440;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public float Time;
        public Vector3 Padding;
        public Vector2 Resolution;
        public Vector2 Padding2;
    }

    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11Texture2D _color;
    private readonly ID3D11RenderTargetView _rtv;
    private readonly ID3D11ShaderResourceView _srv;
    private FrameConstants _frame;

    public ID3D11Texture2D Color => _color;
    public ID3D11ShaderResourceView ColorView => _srv;

    public GpuRaymarchScene(ID3D11Device device)
    {
        _vs = device.CreateVertexShader(Compiler.Compile(Shader, "VSMain", "bench.hlsl", "vs_5_0").Span);
        _ps = device.CreatePixelShader(Compiler.Compile(Shader, "PSMain", "bench.hlsl", "ps_5_0").Span);
        _constants = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer));
        _color = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        _rtv = device.CreateRenderTargetView(_color);
        _srv = device.CreateShaderResourceView(_color);
        _frame.Resolution = new Vector2(Width, Height);
    }

    public void Draw(ID3D11DeviceContext context, float time)
    {
        _frame.Time = time;
        context.UpdateSubresource(in _frame, _constants);
        context.OMSetRenderTargets(_rtv);
        context.RSSetViewport(new Viewport(Width, Height));
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vs);
        context.PSSetShader(_ps);
        context.PSSetConstantBuffer(0, _constants);
        context.ClearRenderTargetView(_rtv, new Color4(0, 0, 0, 1));
        context.Draw(3, 0);
    }

    public void Dispose()
    {
        _srv.Dispose();
        _rtv.Dispose();
        _color.Dispose();
        _constants.Dispose();
        _ps.Dispose();
        _vs.Dispose();
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

        float3 albedoAt(float3 p, float t)
        {
            float best = p.y + 1.0 + 0.08 * sin(p.x * 2.0 + t) * cos(p.z * 2.0);
            float tiles = saturate(0.5 + 0.5 * sin(p.x * 1.6) * sin(p.z * 1.6));
            float3 col = lerp(float3(0.12, 0.45, 0.62), float3(0.55, 0.16, 0.48), tiles);

            [loop] for (int i = 0; i < 10; i++)
            {
                float a = i * 0.628 + t * 0.3;
                float3 c = float3(cos(a) * 2.6, sin(t + i) * 0.5, sin(a) * 2.6 + 5.0);
                float ds = length(p - c) - 0.55;
                float3 sc = 0.55 + 0.45 * cos(float3(0.0, 2.0, 4.0) + i * 0.65);
                if (ds < best) { best = ds; col = sc; }
            }

            float3 q = p - float3(0, 0.1, 5.0);
            float db = length(max(abs(q) - 0.7, 0.0)) - 0.12;
            if (db < best) col = float3(0.95, 0.78, 0.18);
            return col;
        }

        float softShadow(float3 ro, float3 rd, float t, float maxDist)
        {
            float res = 1.0, d = 0.02;
            [loop] for (int i = 0; i < 48; i++)
            {
                float h = scene(ro + rd * d, t);
                res = min(res, 10.0 * h / d);
                d += clamp(h, 0.02, 0.35);
                if (h < 0.001 || d > maxDist) break;
            }
            return saturate(res);
        }

        float3 applyLight(float3 p, float3 n, float3 rd, float3 albedo, float t, float3 lightPos, float3 lightColor)
        {
            float3 toLight = lightPos - p;
            float dist = length(toLight);
            float3 l = toLight / dist;
            float ndl = saturate(dot(n, l));
            float shadow = softShadow(p + n * 0.02, l, t, dist);
            float atten = 1.0 / (1.0 + dist * dist * 0.015);
            float spec = pow(saturate(dot(reflect(-l, n), -rd)), 48.0);
            return (albedo * lightColor * ndl + lightColor * spec * 0.55) * shadow * atten;
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
            float3 sky = float3(0.07, 0.09, 0.16) + rd.y * float3(0.15, 0.18, 0.28);
            float3 col = sky;
            if (hit > 0)
            {
                float3 p = ro + rd * d;
                float3 n = normalAt(p, time);
                float3 albedo = albedoAt(p, time);
                col = albedo * 0.045;
                col += applyLight(p, n, rd, albedo, time, float3(6.0, 7.0, 1.0), float3(1.0, 0.72, 0.38));
                col += applyLight(p, n, rd, albedo, time, float3(-6.0, 5.0, 8.0), float3(0.30, 0.62, 1.0));
                col += applyLight(p, n, rd, albedo, time, float3(0.5, 6.5, -3.0), float3(1.0, 0.28, 0.62));
                col = lerp(col, sky, 1.0 - exp(-0.012 * d * d));
            }
            return float4(pow(saturate(col), 0.4545), 1);
        }
        """;
}
