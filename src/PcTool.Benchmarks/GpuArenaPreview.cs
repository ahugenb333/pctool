using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PcTool.Benchmarks;

/// <summary>
/// Renders <see cref="GpuArenaScene"/> into a BGRA buffer the UI can display.
/// A child-HWND swap chain often stays blank inside WPF, so the preview copies pixels instead.
/// </summary>
public sealed class GpuArenaPreview : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly GpuArenaScene _scene;
    private ID3D11Texture2D? _color;
    private ID3D11RenderTargetView? _rtv;
    private ID3D11Texture2D? _depth;
    private ID3D11DepthStencilView? _dsv;
    private ID3D11Texture2D? _staging;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public GpuArenaPreview(int width, int height)
    {
        using var adapter = D3D11GpuHelper.PickAdapter(out _);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out _device, out _, out _context).CheckError();
        _scene = new GpuArenaScene(_device);
        Resize(width, height);
    }

    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, 1, 3840);
        height = Math.Clamp(height, 1, 2160);
        if (width == Width && height == Height && _color is not null) return;

        _rtv?.Dispose();
        _color?.Dispose();
        _dsv?.Dispose();
        _depth?.Dispose();
        _staging?.Dispose();

        Width = width;
        Height = height;
        _color = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        _rtv = _device.CreateRenderTargetView(_color);
        _depth = _device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)width, (uint)height, 1, 1, BindFlags.DepthStencil));
        _dsv = _device.CreateDepthStencilView(_depth);
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
    }

    /// <summary>Draw one frame and copy BGRA pixels into <paramref name="destination"/> (row stride in bytes).</summary>
    public unsafe void Render(float timeSeconds, IntPtr destination, int destinationStride)
    {
        _scene.Draw(_context, _rtv!, _dsv!, Width, Height, timeSeconds);
        _context.CopyResource(_staging!, _color!);
        _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
        try
        {
            var rowBytes = Width * 4;
            var src = (byte*)mapped.DataPointer;
            var dst = (byte*)destination;
            for (var y = 0; y < Height; y++)
            {
                Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * destinationStride, rowBytes, rowBytes);
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
    }

    public void Dispose()
    {
        _rtv?.Dispose();
        _color?.Dispose();
        _dsv?.Dispose();
        _depth?.Dispose();
        _staging?.Dispose();
        _scene.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
