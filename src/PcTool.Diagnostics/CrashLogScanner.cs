using System.Diagnostics.Eventing.Reader;
using PcTool.Core.Models;

namespace PcTool.Diagnostics;

public sealed record CrashEvent(DateTime Time, string App, string Module, string ExceptionCode, bool IsHang);

public sealed record SystemEventCount(string Name, int Count, DateTime? Latest);

/// <summary>Reads application crash/hang events, GPU driver resets (TDR), WHEA hardware errors and WER report folders.</summary>
public static class CrashLogScanner
{
    public static List<CrashEvent> ReadAppCrashes(int days)
    {
        var list = new List<CrashEvent>();
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var query = new EventLogQuery("Application", PathType.LogName,
            $"*[System[(EventID=1000 or EventID=1002) and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
        using var reader = new EventLogReader(query);
        for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
        {
            using (e)
            {
                var p = e.Properties;
                string Prop(int i) => i < p.Count ? p[i].Value?.ToString() ?? "" : "";
                var isHang = e.Id == 1002;
                // 1000 (Application Error): 0 app, 3 faulting module, 6 exception code. 1002 (Application Hang): 0 app.
                list.Add(new CrashEvent(e.TimeCreated ?? DateTime.MinValue, Prop(0), isHang ? "" : Prop(3), isHang ? "" : Prop(6), isHang));
            }
        }
        return list;
    }

    public static SystemEventCount CountSystemEvents(string name, string provider, int[] ids, int days)
    {
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var idFilter = string.Join(" or ", ids.Select(i => $"EventID={i}"));
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[Provider[@Name='{provider}'] and ({idFilter}) and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
        var count = 0;
        DateTime? latest = null;
        using var reader = new EventLogReader(query);
        for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
        {
            using (e)
            {
                count++;
                if (e.TimeCreated is { } t && (latest is null || t > latest)) latest = t;
            }
        }
        return new SystemEventCount(name, count, latest);
    }

    /// <summary>Counts Windows Error Reporting archive folders such as "AppCrash_Game.exe_..." newer than the cutoff.</summary>
    public static int CountWerReports(string exeName, int days)
    {
        var cutoff = DateTime.Now.AddDays(-days);
        var count = 0;
        foreach (var root in new[]
                 {
                     Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                     Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive"),
                 })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                count += new DirectoryInfo(root).EnumerateDirectories($"App*_{exeName}_*")
                    .Count(d => d.CreationTime >= cutoff);
            }
            catch (UnauthorizedAccessException) { }
        }
        return count;
    }

    /// <summary>GPU hang dumps written by the kernel after a driver reset.</summary>
    public static int CountLiveKernelGpuReports(int days)
    {
        var cutoff = DateTime.Now.AddDays(-days);
        var dir = Environment.ExpandEnvironmentVariables(@"%windir%\LiveKernelReports\WATCHDOG");
        try
        {
            return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.dmp").Count(f => f.CreationTime >= cutoff) : 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Explains common faulting modules and exception codes from Application Error events.</summary>
    public static (string Cause, string Fix) Explain(string module, string exceptionCode)
    {
        var m = module.ToLowerInvariant();
        if (m.StartsWith("nvwgf2um") || m.StartsWith("nvd3dum") || m.StartsWith("nvoglv") || m.StartsWith("nvlddmkm"))
            return ("NVIDIA graphics driver", "Clean-install the latest NVIDIA driver (use DDU in Safe Mode for a full reset) and remove any GPU overclock.");
        if (m.StartsWith("atidxx") || m.StartsWith("amdxx") || m.StartsWith("atiumd") || m.StartsWith("amdvlk") || m.StartsWith("atio6"))
            return ("AMD graphics driver", "Clean-install the latest AMD Adrenalin driver (Factory Reset option or DDU) and reset Adrenalin tuning to defaults.");
        if (m.StartsWith("igd") || m.StartsWith("igxe") || m.StartsWith("igc64"))
            return ("Intel graphics driver", "Install the latest Intel Arc/Iris driver from intel.com rather than the PC maker's older one.");
        if (m is "d3d11.dll" or "d3d12.dll" or "dxgi.dll" or "d3d12core.dll")
            return ("DirectX / graphics stack", "Update the graphics driver and Windows; verify the game's files.");
        if (m.StartsWith("vcruntime") || m.StartsWith("msvcp") || m.StartsWith("ucrtbase") || m.StartsWith("concrt"))
            return ("Visual C++ runtime", "Repair or reinstall the Microsoft Visual C++ 2015-2022 Redistributable (x64 and x86).");
        if (m.StartsWith("gameoverlayrenderer") || m.StartsWith("discordhook") || m.StartsWith("rtsshooks") || m.StartsWith("owclient") || m.StartsWith("nvspcap"))
            return ("Overlay software", "Disable in-game overlays (Steam, Discord, RivaTuner, Overwolf, GeForce Experience) for this game.");
        if (m == "ntdll.dll")
            return ("Memory corruption inside the game", "Verify game files, disable overlays, and if it persists turn off XMP/EXPO and CPU/GPU overclocks to rule out instability.");
        if (m == "kernelbase.dll")
            return exceptionCode.Equals("e0434352", StringComparison.OrdinalIgnoreCase)
                ? (".NET exception in the game or launcher", "Repair the .NET runtime and verify the game's files.")
                : ("Unhandled exception in the game", "Verify the game's files and check the developer's known-issues page.");
        if (m.Length > 0)
            return ($"Crash in {module}", "Verify the game's files; if the module belongs to a mod or third-party tool, remove it.");
        return ("Unknown", "Verify the game's files and update the graphics driver.");
    }
}
