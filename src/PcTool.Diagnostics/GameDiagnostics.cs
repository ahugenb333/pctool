using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Games;

namespace PcTool.Diagnostics;

public sealed record DiagnosticsRequest(GameEntry? Game, string? ExecutableName, SystemSpecs? Specs, HardwareProfile? Profile, int LookbackDays = 14)
{
    public string? Exe => !string.IsNullOrWhiteSpace(ExecutableName) ? ExecutableName.Trim() : Game?.Executables.FirstOrDefault();
}

/// <summary>
/// Runs a battery of independent checks for "my game crashes / stutters / runs badly". Each check catches its own
/// failures so one unreadable log does not hide the rest.
/// </summary>
public sealed class GameDiagnostics(ISensorMonitor sensors, GameAdvisor advisor)
{
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    private static readonly (string Process, string Name)[] Overlays =
    [
        ("RTSS", "RivaTuner Statistics Server"), ("MSIAfterburner", "MSI Afterburner"), ("Overwolf", "Overwolf"),
        ("Discord", "Discord (in-game overlay)"), ("NVIDIA Overlay", "NVIDIA overlay"), ("Medal", "Medal.tv"),
        ("obs64", "OBS Studio"), ("XboxGameBar", "Xbox Game Bar"), ("Fraps", "Fraps"), ("WallpaperEngine", "Wallpaper Engine"),
        ("wallpaper64", "Wallpaper Engine"), ("iCUE", "Corsair iCUE"), ("ArmouryCrate", "ASUS Armoury Crate"),
    ];

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(DiagnosticsRequest request, CancellationToken ct = default) =>
        Task.Run(() => Run(request, ct), ct);

    public IReadOnlyList<DiagnosticFinding> Run(DiagnosticsRequest r, CancellationToken ct = default)
    {
        var findings = new List<DiagnosticFinding>();
        var checks = new (string Category, Action<DiagnosticsRequest, List<DiagnosticFinding>> Run)[]
        {
            ("Requirements", CheckRequirements),
            ("Crashes", CheckCrashes),
            ("Graphics driver", CheckDriverResets),
            ("Graphics driver", CheckDriverAge),
            ("Hardware errors", CheckHardwareErrors),
            ("Runtimes", CheckRuntimes),
            ("Power", CheckPower),
            ("Windows settings", CheckGamingSettings),
            ("Memory", CheckMemory),
            ("Storage", CheckStorage),
            ("Thermals", CheckThermals),
            ("Background apps", CheckOverlays),
        };

        foreach (var (category, run) in checks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                run(r, findings);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                findings.Add(new(category, FindingSeverity.Info, "Check could not run", ex.Message, null));
            }
        }

        return findings.OrderByDescending(f => f.Severity).ToList();
    }

    private void CheckRequirements(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        if (r.Game is null || r.Specs is null) return;
        var v = advisor.Evaluate(r.Game, r.Specs);
        var notes = v.Notes.Count > 0 ? " " + string.Join(" ", v.Notes) : "";
        f.Add(v.Level switch
        {
            GameVerdictLevel.BelowMinimum => new("Requirements", FindingSeverity.Problem, $"Below {r.Game.Name}'s minimum spec",
                $"{v.Bottleneck}.{notes}", "See the Upgrades page for parts that would get this game running."),
            GameVerdictLevel.Minimum when v.Bottleneck.StartsWith("CPU") => new("Requirements", FindingSeverity.Warning, "CPU is below the recommended spec",
                $"Your GPU handles {v.SuggestedSettings}, but the CPU will limit frame rate in busy scenes.{notes}",
                "Lower CPU-heavy settings (crowd density, view distance, simulation quality) and close background apps."),
            GameVerdictLevel.Minimum => new("Requirements", FindingSeverity.Warning, $"Meets minimum spec only ({v.Bottleneck})",
                $"Suggested: {v.SuggestedSettings}.{notes}", $"Start at {v.SuggestedSettings} and lower resolution scale before other settings."),
            _ => new("Requirements", FindingSeverity.Info, $"Meets recommended spec - try {v.SuggestedSettings}", $"{v.Bottleneck}.{notes}", null),
        });
    }

    private void CheckCrashes(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var crashes = CrashLogScanner.ReadAppCrashes(r.LookbackDays);
        var exe = r.Exe;
        var gameExes = r.Game?.Executables ?? (exe is null ? [] : [exe]);
        var known = advisor.Games.SelectMany(g => g.Executables).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relevant = gameExes.Length > 0
            ? crashes.Where(c => gameExes.Any(e => c.App.Equals(e, StringComparison.OrdinalIgnoreCase))).ToList()
            : crashes.Where(c => known.Contains(c.App) || IsLikelyGame(c.App)).ToList();

        var label = r.Game?.Name ?? exe ?? "your games";
        if (relevant.Count == 0)
        {
            var wer = exe is null ? 0 : CrashLogScanner.CountWerReports(exe, r.LookbackDays);
            f.Add(wer > 0
                ? new("Crashes", FindingSeverity.Warning, $"{wer} crash report(s) for {exe}",
                    $"Windows Error Reporting saved {wer} report(s) in the last {r.LookbackDays} days, but no matching Event Log entries.",
                    "Verify the game's files and update the graphics driver.")
                : new("Crashes", FindingSeverity.Info, $"No crashes logged for {label}",
                    $"No Application Error or Application Hang events in the last {r.LookbackDays} days.", null));
            return;
        }

        foreach (var group in relevant.GroupBy(c => (c.App, c.Module, c.IsHang)).OrderByDescending(g => g.Count()).Take(gameExes.Length > 0 ? 5 : 8))
        {
            var latest = group.Max(c => c.Time);
            if (group.Key.IsHang)
            {
                f.Add(new("Crashes", FindingSeverity.Warning, $"{group.Key.App} stopped responding {group.Count()}x",
                    $"Last on {latest:g}. Hangs usually point to the GPU driver, a full drive or a stuck overlay.",
                    "Update the graphics driver, close overlays, and make sure the drive isn't nearly full."));
                continue;
            }
            var (cause, fix) = CrashLogScanner.Explain(group.Key.Module, group.First().ExceptionCode);
            f.Add(new("Crashes", FindingSeverity.Problem, $"{group.Key.App} crashed {group.Count()}x in {group.Key.Module}",
                $"Likely cause: {cause}. Exception 0x{group.First().ExceptionCode}, last on {latest:g}.", fix));
        }
    }

    /// <summary>Unreal Engine games ship as "Name-Win64-Shipping.exe", which covers many titles not in the catalog.</summary>
    private static bool IsLikelyGame(string exe) =>
        exe.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase);

    private static void CheckDriverResets(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var tdr = CrashLogScanner.CountSystemEvents("Display driver resets", "Display", [4101], r.LookbackDays);
        var nv = CrashLogScanner.CountSystemEvents("NVIDIA driver errors", "nvlddmkm", [13, 14, 153], r.LookbackDays);
        var dumps = CrashLogScanner.CountLiveKernelGpuReports(r.LookbackDays);
        var total = tdr.Count + nv.Count;
        if (total == 0 && dumps == 0)
        {
            f.Add(new("Graphics driver", FindingSeverity.Info, "No GPU driver resets", $"No display driver timeouts in the last {r.LookbackDays} days.", null));
            return;
        }
        var latest = new[] { tdr.Latest, nv.Latest }.Max();
        f.Add(new("Graphics driver", total >= 3 ? FindingSeverity.Problem : FindingSeverity.Warning,
            $"GPU driver stopped responding {Math.Max(total, dumps)}x",
            $"Windows reset the graphics driver (TDR){(latest is { } l ? $", most recently {l:g}" : "")}. Typical causes: unstable GPU overclock or undervolt, overheating, an aging PSU, or a driver bug.",
            "Remove GPU overclocks/undervolts, clean-install the driver, check GPU temperatures, and make sure each PCIe power connector uses its own cable."));
    }

    private static void CheckDriverAge(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        if (r.Profile?.PrimaryGpu is not { } gpu || gpu.DriverDate is not { } date) return;
        var age = (DateTime.Now - date).TotalDays;
        var link = gpu.Vendor switch
        {
            GpuVendor.Nvidia => "https://www.nvidia.com/Download/index.aspx",
            GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
            GpuVendor.Intel => "https://www.intel.com/content/www/us/en/download-center/home.html",
            _ => "your GPU maker's website",
        };
        if (age > 180)
            f.Add(new("Graphics driver", age > 365 ? FindingSeverity.Warning : FindingSeverity.Info,
                $"Graphics driver is {age / 30:F0} months old",
                $"{gpu.Name} driver {gpu.DriverVersion} from {date:d}. New games often need recent drivers for fixes and performance.",
                $"Download the latest driver from {link}."));
        else
            f.Add(new("Graphics driver", FindingSeverity.Info, "Graphics driver is recent", $"{gpu.DriverVersion} from {date:d}.", null));
    }

    private static void CheckHardwareErrors(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var fatal = CrashLogScanner.CountSystemEvents("Fatal hardware errors", "Microsoft-Windows-WHEA-Logger", [18, 20], r.LookbackDays);
        var corrected = CrashLogScanner.CountSystemEvents("Corrected hardware errors", "Microsoft-Windows-WHEA-Logger", [17, 19, 47], r.LookbackDays);
        var power = CrashLogScanner.CountSystemEvents("Unexpected shutdowns", "Microsoft-Windows-Kernel-Power", [41], r.LookbackDays);

        if (fatal.Count > 0)
            f.Add(new("Hardware errors", FindingSeverity.Problem, $"{fatal.Count} fatal hardware error(s)",
                "WHEA logged machine-check errors that crashed the system. This is almost always CPU, RAM or overclock instability.",
                "Disable XMP/EXPO, PBO and any CPU/GPU overclock, update the BIOS, then retest. If it continues, test RAM with MemTest86."));
        if (corrected.Count > 0)
            f.Add(new("Hardware errors", FindingSeverity.Warning, $"{corrected.Count} corrected hardware error(s)",
                "The CPU or PCIe bus corrected errors on its own. They're harmless in small numbers but often precede crashes under load.",
                "Check for an unstable undervolt or overclock, reseat the GPU, and update the BIOS and chipset drivers."));
        if (power.Count > 0)
            f.Add(new("Hardware errors", FindingSeverity.Warning, $"{power.Count} unexpected shutdown(s)",
                "The PC lost power or hard-reset without shutting down (Kernel-Power 41). Under gaming load this often means the PSU can't keep up.",
                "Check PSU wattage and age, use separate PCIe power cables, and watch temperatures."));
        if (fatal.Count + corrected.Count + power.Count == 0)
            f.Add(new("Hardware errors", FindingSeverity.Info, "No hardware errors", $"No WHEA or unexpected-shutdown events in the last {r.LookbackDays} days.", null));
    }

    private static void CheckRuntimes(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        static bool VcInstalled(string arch)
        {
            foreach (var path in new[] { $@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}", $@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}" })
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Installed") is int i && i == 1) return true;
            }
            return false;
        }

        var missing = new[] { "x64", "x86" }.Where(a => !VcInstalled(a)).ToList();
        f.Add(missing.Count > 0
            ? new("Runtimes", FindingSeverity.Warning, $"Visual C++ 2015-2022 runtime missing ({string.Join(", ", missing)})",
                "Most PC games need the Microsoft Visual C++ Redistributable and fail with vcruntime140.dll or msvcp140.dll errors without it.",
                "Install both from https://aka.ms/vs/17/release/vc_redist.x64.exe and https://aka.ms/vs/17/release/vc_redist.x86.exe.")
            : new("Runtimes", FindingSeverity.Info, "Visual C++ runtimes installed", "x64 and x86 redistributables are present.", null));

        var d3dx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3dx9_43.dll");
        if (!File.Exists(d3dx))
            f.Add(new("Runtimes", FindingSeverity.Info, "Legacy DirectX 9 runtime not installed",
                "Older games (roughly pre-2015) that report d3dx9_43.dll or xinput1_3.dll missing need the DirectX End-User Runtime.",
                "Install the DirectX End-User Runtime from https://www.microsoft.com/download/details.aspx?id=35 if an older game asks for it."));
    }

    private static void CheckPower(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var scheme = RunCapture("powercfg", "/getactivescheme");
        if (scheme.Contains(PowerSaverGuid, StringComparison.OrdinalIgnoreCase))
            f.Add(new("Power", FindingSeverity.Warning, "Power saver plan is active",
                "Power saver caps CPU clocks and hurts frame rates.", "Switch to Balanced or Best performance in Settings > System > Power."));
        else if (scheme.Length > 0)
        {
            var name = scheme.Split('(', ')') is { Length: >= 2 } parts ? parts[1] : "current";
            f.Add(new("Power", FindingSeverity.Info, $"Power plan: {name}", "Not throttled by the power plan.", null));
        }

        if (GetSystemPowerStatus(out var status) && status.ACLineStatus == 0)
            f.Add(new("Power", FindingSeverity.Warning, "Running on battery",
                "Laptops cut GPU and CPU power sharply on battery.", "Plug in the charger while gaming."));
    }

    private static void CheckGamingSettings(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        using (var gfx = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
        {
            var hags = gfx?.GetValue("HwSchMode") as int?;
            f.Add(new("Windows settings", FindingSeverity.Info,
                $"Hardware-accelerated GPU scheduling: {(hags == 2 ? "on" : hags == 1 ? "off" : "default")}",
                "Needed for DLSS Frame Generation; otherwise it rarely makes a difference.",
                hags == 1 ? "Turn it on in Settings > System > Display > Graphics if you use frame generation." : null));
        }

        using (var bar = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
        {
            if (bar?.GetValue("AutoGameModeEnabled") is int gm && gm == 0)
                f.Add(new("Windows settings", FindingSeverity.Info, "Game Mode is off",
                    "Game Mode keeps Windows Update and background tasks from interrupting games.", "Turn it on in Settings > Gaming > Game Mode."));
        }

        using var dvr = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
        using var capture = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
        if (dvr?.GetValue("GameDVR_Enabled") is int on && on == 1 && capture?.GetValue("HistoricalCaptureEnabled") is int hist && hist == 1)
            f.Add(new("Windows settings", FindingSeverity.Info, "Background recording is on",
                "Xbox Game Bar is continuously recording the last few minutes, which costs a little GPU time.",
                "Turn off 'Record what happened' in Settings > Gaming > Captures if you don't use it."));
    }

    private static void CheckMemory(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        using (var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"))
        {
            if (searcher.Get().Count == 0)
                f.Add(new("Memory", FindingSeverity.Warning, "Page file is disabled",
                    "Many games crash with out-of-memory errors without a page file, even with plenty of RAM.",
                    "Re-enable 'Automatically manage paging file size' in System Properties > Advanced > Performance."));
        }

        if (r.Profile?.Memory is { TotalBytes: > 0 } mem)
        {
            var availGb = mem.AvailableBytes / 1073741824.0;
            if (availGb < 3)
                f.Add(new("Memory", FindingSeverity.Warning, $"Only {availGb:F1} GB RAM free right now",
                    "Background apps are using most of your memory, which causes stutter when a game streams in assets.",
                    "Close browsers and launchers before playing, or add more RAM."));
            if (mem.Modules.Count == 1 && mem.SlotCount >= 2)
                f.Add(new("Memory", FindingSeverity.Warning, "RAM is running in single-channel mode",
                    "One memory stick halves memory bandwidth, which costs 10-30% FPS in CPU-heavy games.",
                    "Add a matching second stick, or replace it with a 2-stick kit."));
        }
    }

    private static void CheckStorage(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d is { DriveType: DriveType.Fixed, IsReady: true }))
        {
            var freeGb = d.AvailableFreeSpace / 1e9;
            var pct = (double)d.AvailableFreeSpace / d.TotalSize;
            var isSystem = d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase);
            if (freeGb < 15 || pct < 0.05)
                f.Add(new("Storage", isSystem ? FindingSeverity.Problem : FindingSeverity.Warning, $"Drive {d.Name.TrimEnd('\\')} is almost full ({freeGb:F0} GB free)",
                    isSystem ? "A nearly full system drive breaks updates, shader caches and the page file." : "Games on this drive can fail to patch or save.",
                    "Free up space with Storage Sense or move games to another drive."));
        }
        if (r.Specs is { SystemDriveKind: StorageKind.Hdd })
            f.Add(new("Storage", FindingSeverity.Warning, "Windows is on a hard drive",
                "Hard drives cause long loads and texture pop-in, and many current games require an SSD.",
                "Install the game (or Windows) on an SSD."));
    }

    private void CheckThermals(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var readings = sensors.Read();
        var temps = readings.Where(s => s is { Kind: SensorKind.Temperature, Value: not null }).ToList();
        var cpu = temps.Where(s => s.Source == SensorSource.Cpu && IsPackageOrCore(s.Name)).Select(s => s.Max ?? s.Value!.Value).DefaultIfEmpty().Max();
        var gpu = temps.Where(s => s.Source == SensorSource.Gpu && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Source == SensorSource.Gpu && s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Max ?? s.Value!.Value).DefaultIfEmpty().Max();

        if (cpu == 0 && gpu == 0)
        {
            f.Add(new("Thermals", FindingSeverity.Info, "Temperatures unavailable",
                sensors.IsElevated ? "No temperature sensors were readable." : "CPU temperature sensors need administrator rights.",
                sensors.IsElevated ? null : "Use 'Restart as administrator' on the Settings page, run the game, then rerun diagnostics."));
            return;
        }

        if (cpu >= 95)
            f.Add(new("Thermals", FindingSeverity.Problem, $"CPU reached {cpu:F0} °C", "At this temperature the CPU throttles and loses performance.",
                "Clean dust filters, check the cooler is seated and its fan spins, and consider a better cooler or fresh thermal paste."));
        else if (cpu >= 88)
            f.Add(new("Thermals", FindingSeverity.Warning, $"CPU reached {cpu:F0} °C", "Running hot; close to throttling.", "Improve airflow or cooler mounting."));
        else if (cpu > 0)
            f.Add(new("Thermals", FindingSeverity.Info, $"CPU peak {cpu:F0} °C", "Within normal range since the app started.", null));
        else if (!sensors.IsElevated)
            f.Add(new("Thermals", FindingSeverity.Info, "CPU temperature unavailable", "CPU temperature sensors need administrator rights.",
                "Use 'Restart as administrator' on the Settings page to include CPU temperatures."));

        if (gpu >= 90)
            f.Add(new("Thermals", FindingSeverity.Problem, $"GPU reached {gpu:F0} °C", "The graphics card is throttling.",
                "Clean the card's fans and heatsink, improve case airflow, or set a more aggressive fan curve."));
        else if (gpu >= 83)
            f.Add(new("Thermals", FindingSeverity.Warning, $"GPU reached {gpu:F0} °C", "Close to the throttling point for most cards.", "Improve case airflow or the fan curve."));
        else if (gpu > 0)
            f.Add(new("Thermals", FindingSeverity.Info, $"GPU peak {gpu:F0} °C", "Within normal range since the app started.", null));

        f.Add(new("Thermals", FindingSeverity.Info, "Peaks are measured since PcTool started",
            "For a real test, keep PcTool open while you play for a few minutes, then rerun diagnostics.", null));
    }

    private static bool IsPackageOrCore(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Core", StringComparison.OrdinalIgnoreCase);

    private static void CheckOverlays(DiagnosticsRequest r, List<DiagnosticFinding> f)
    {
        var running = Process.GetProcesses().Select(p => { using (p) return p.ProcessName; }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = Overlays.Where(o => running.Contains(o.Process)).Select(o => o.Name).Distinct().ToList();
        if (found.Count > 0)
            f.Add(new("Background apps", FindingSeverity.Info, $"Overlay or hooking apps running: {string.Join(", ", found)}",
                "Overlays inject into games and are a common cause of crashes, stutter and anti-cheat kicks.",
                "If the game misbehaves, close these or disable their in-game overlay and test again."));
    }

    private static string RunCapture(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        if (p is null) return "";
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return output;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
