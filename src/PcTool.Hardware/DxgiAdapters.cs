using Vortice.DXGI;

namespace PcTool.Hardware;

internal sealed record DxgiAdapter(string Name, uint VendorId, ulong DedicatedVideoMemory, ulong SharedSystemMemory);

internal static class DxgiAdapters
{
    public static List<DxgiAdapter> Enumerate()
    {
        var list = new List<DxgiAdapter>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != 0) continue;
                    list.Add(new DxgiAdapter(desc.Description.Trim(), desc.VendorId,
                        (ulong)desc.DedicatedVideoMemory, (ulong)desc.SharedSystemMemory));
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException or SharpGen.Runtime.SharpGenException)
        {
        }
        return list;
    }
}
