using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PcTool.Benchmarks;

/// <summary>Shared arena mesh and shaders for the timed benchmark and the live preview.</summary>
internal sealed class GpuArenaScene : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProj;
        public Vector4 CameraPosAndTime;
        public Vector4 LightDirAndPad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex(Vector3 position, Vector3 normal, Vector3 color, float emissive)
    {
        public Vector3 Position = position;
        public Vector3 Normal = normal;
        public Vector3 Color = color;
        public float Emissive = emissive;
    }

    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11Buffer _frameCb;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _vb;
    private readonly ID3D11Buffer _ib;
    private readonly ID3D11DepthStencilState _depthState;
    private readonly ID3D11RasterizerState _rasterState;
    private readonly uint _indexCount;

    public GpuArenaScene(ID3D11Device device)
    {
        _vs = device.CreateVertexShader(Compiler.Compile(VertexShader, "VSMain", "arena-vs.hlsl", "vs_5_0").Span);
        _ps = device.CreatePixelShader(Compiler.Compile(PixelShader, "PSMain", "arena-ps.hlsl", "ps_5_0").Span);
        _frameCb = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer));
        var vsBytecode = Compiler.Compile(VertexShader, "VSMain", "arena-vs.hlsl", "vs_5_0").Span;
        _layout = device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32_Float, 24, 0),
            new InputElementDescription("EMISSIVE", 0, Format.R32_Float, 36, 0),
        ], vsBytecode);

        var (vertices, indices) = BuildArenaMesh();
        _vb = device.CreateBuffer(vertices, BindFlags.VertexBuffer);
        _ib = device.CreateBuffer(indices, BindFlags.IndexBuffer);
        _indexCount = (uint)indices.Length;
        _depthState = device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.Less));
        // Projection flips winding, so the default clockwise front face matches this mesh.
        var raster = new RasterizerDescription(CullMode.Back, FillMode.Solid);
        _rasterState = device.CreateRasterizerState(raster);
    }

    public void Draw(ID3D11DeviceContext context, ID3D11RenderTargetView rtv, ID3D11DepthStencilView dsv, int width, int height, float time)
    {
        var viewProj = BuildViewProj(time, width, height, out var camPos);
        var light = Vector3.Normalize(new Vector3(-0.35f, -0.9f, -0.25f));
        var frame = new FrameConstants
        {
            ViewProj = viewProj,
            CameraPosAndTime = new Vector4(camPos, time),
            LightDirAndPad = new Vector4(light, 0),
        };
        context.UpdateSubresource(in frame, _frameCb);
        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vs);
        context.PSSetShader(_ps);
        context.VSSetConstantBuffer(0, _frameCb);
        context.PSSetConstantBuffer(0, _frameCb);
        context.OMSetRenderTargets(rtv, dsv);
        context.RSSetViewport(new Viewport(width, height));
        context.OMSetDepthStencilState(_depthState);
        context.RSSetState(_rasterState);
        context.IASetVertexBuffer(0, _vb, (uint)Marshal.SizeOf<Vertex>());
        context.IASetIndexBuffer(_ib, Format.R32_UInt, 0);
        context.ClearRenderTargetView(rtv, new Color4(0.04f, 0.05f, 0.1f, 1));
        context.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1, 0);
        context.DrawIndexed(_indexCount, 0, 0);
    }

    public void Dispose()
    {
        _vs.Dispose();
        _ps.Dispose();
        _frameCb.Dispose();
        _layout.Dispose();
        _vb.Dispose();
        _ib.Dispose();
        _depthState.Dispose();
        _rasterState.Dispose();
    }

    internal static Matrix4x4 BuildViewProj(float time, int width, int height, out Vector3 camPos)
    {
        var angle = time * 0.4f;
        camPos = new Vector3(MathF.Sin(angle) * 12f, 5.5f, MathF.Cos(angle) * 12f);
        var view = Matrix4x4.CreateLookAt(camPos, new Vector3(0, 1.2f, 0), Vector3.UnitY);
        var aspect = width / (float)Math.Max(height, 1);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, aspect, 0.1f, 60f);
        return view * proj;
    }

    private static (Vertex[] Vertices, uint[] Indices) BuildArenaMesh()
    {
        var verts = new List<Vertex>();
        var idx = new List<uint>();
        AppendBox(verts, idx, Vector3.Zero, new Vector3(22f, 0.25f, 22f), new Vector3(0.22f, 0.24f, 0.28f), 0);

        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
            {
                if (x == 0 && z == 0) continue;
                var h = 1.4f + ((x + 2) * 5 + (z + 2)) % 4 * 0.55f;
                var neon = (x + z) % 2 == 0;
                var color = neon ? new Vector3(0.15f, 0.75f, 1f) : new Vector3(0.38f, 0.4f, 0.44f);
                var pos = new Vector3(x * 2.2f, h * 0.5f + 0.25f, z * 2.2f);
                AppendBox(verts, idx, pos, new Vector3(0.55f, h, 0.55f), color, neon ? 2.2f : 0);
            }
        }

        AppendBox(verts, idx, new Vector3(0, 2.8f, 0), new Vector3(0.9f, 2.2f, 0.9f), new Vector3(1f, 0.2f, 0.5f), 3f);
        return (verts.ToArray(), idx.ToArray());
    }

    private static void AppendBox(List<Vertex> verts, List<uint> idx, Vector3 center, Vector3 size, Vector3 color, float emissive)
    {
        var half = size * 0.5f;
        Vector3[] corners =
        [
            center + new Vector3(-half.X, -half.Y, -half.Z),
            center + new Vector3(half.X, -half.Y, -half.Z),
            center + new Vector3(half.X, half.Y, -half.Z),
            center + new Vector3(-half.X, half.Y, -half.Z),
            center + new Vector3(-half.X, -half.Y, half.Z),
            center + new Vector3(half.X, -half.Y, half.Z),
            center + new Vector3(half.X, half.Y, half.Z),
            center + new Vector3(-half.X, half.Y, half.Z),
        ];

        void Face(int a, int b, int c, int d, Vector3 normal)
        {
            var i = (uint)verts.Count;
            verts.Add(new Vertex(corners[a], normal, color, emissive));
            verts.Add(new Vertex(corners[b], normal, color, emissive));
            verts.Add(new Vertex(corners[c], normal, color, emissive));
            verts.Add(new Vertex(corners[d], normal, color, emissive));
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
            idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
        }

        Face(0, 1, 2, 3, new Vector3(0, 0, -1));
        Face(5, 4, 7, 6, new Vector3(0, 0, 1));
        Face(4, 0, 3, 7, new Vector3(-1, 0, 0));
        Face(1, 5, 6, 2, new Vector3(1, 0, 0));
        Face(4, 5, 1, 0, new Vector3(0, -1, 0));
        Face(3, 2, 6, 7, new Vector3(0, 1, 0));
    }

    private const string VertexShader = """
cbuffer Frame : register(b0)
{
    row_major float4x4 viewProj;
    float4 cameraPos_time;
    float4 lightDir_pad;
};

struct VSIn
{
    float3 pos : POSITION;
    float3 normal : NORMAL;
    float3 color : COLOR;
    float emissive : EMISSIVE;
};
struct VSOut
{
    float4 pos : SV_Position;
    float3 worldPos : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float3 color : TEXCOORD2;
    float emissive : TEXCOORD3;
};

VSOut VSMain(VSIn input)
{
    VSOut o;
    o.worldPos = input.pos;
    o.pos = mul(float4(input.pos, 1), viewProj);
    o.normal = input.normal;
    o.color = input.color;
    o.emissive = input.emissive;
    return o;
}
""";

    private const string PixelShader = """
cbuffer Frame : register(b0)
{
    row_major float4x4 viewProj;
    float4 cameraPos_time;
    float4 lightDir_pad;
};

struct PSIn
{
    float4 pos : SV_Position;
    float3 worldPos : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float3 color : TEXCOORD2;
    float emissive : TEXCOORD3;
};

float4 PSMain(PSIn i) : SV_Target
{
    float3 n = normalize(i.normal);
    float3 l = normalize(lightDir_pad.xyz);
    float ndl = saturate(dot(n, -l));
    float3 diffuse = i.color * (0.28 + 0.72 * ndl);
    float pulse = 0.6 + 0.4 * sin(cameraPos_time.w * 3.5 + i.worldPos.x * 0.7 + i.worldPos.z * 0.5);
    float3 neon = i.color * i.emissive * pulse * 0.4;
    float3 col = diffuse + neon;
    float dist = length(i.worldPos - cameraPos_time.xyz);
    float fog = exp(-dist * 0.06);
    col = lerp(float3(0.04, 0.05, 0.1), col, fog);
    return float4(col, 1);
}
""";
}
