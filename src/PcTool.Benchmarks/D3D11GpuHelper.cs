using Vortice.DXGI;

namespace PcTool.Benchmarks;

internal static class D3D11GpuHelper
{
    internal static IDXGIAdapter1 PickAdapter(out string name)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();
        IDXGIAdapter1? adapter = null;
        if (factory6 is not null)
            factory6.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out adapter);
        if (adapter is null) factory.EnumAdapters1(0, out adapter).CheckError();
        name = adapter!.Description1.Description.Trim();
        return adapter;
    }
}
