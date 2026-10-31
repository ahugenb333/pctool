using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTool.App.Services;
using PcTool.Core;
using PcTool.Hardware;

namespace PcTool.App.ViewModels;

public sealed record NoticeRow(string Name, string License, string Url, string Notice);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly ISensorMonitor _sensors;

    public AppState State { get; }
    public IReadOnlyList<string> Chipsets { get; }
    public IReadOnlyList<string> CaseSizes { get; } = ["", "ATX", "mATX", "ITX"];
    public IReadOnlyList<NoticeRow> DataNotices { get; }
    public IReadOnlyList<NoticeRow> LibraryNotices { get; } =
    [
        new("WPF-UI", "MIT", "https://github.com/lepoco/wpfui", "Fluent controls and window chrome."),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet", "MVVM source generators."),
        new("Hardware.Info", "MIT", "https://github.com/Jinjinov/Hardware.Info", "WMI hardware inventory."),
        new("LibreHardwareMonitorLib", "MPL-2.0", "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor",
            "Sensor readings. Used unmodified as a separate library, with its MPL-2.0 dependencies BlackSharp, DiskInfoToolkit and RAMSPDToolkit; source is available at the link above."),
        new("HIDSharp", "Apache-2.0", "https://software.seekye.com/hidsharp", "USB HID access used by LibreHardwareMonitor."),
        new("Vortice.Windows", "MIT", "https://github.com/amerkoleci/Vortice.Windows", "DXGI and Direct3D 11 bindings for VRAM detection and the GPU benchmark."),
        new(".NET runtime and Microsoft.Extensions", "MIT", "https://github.com/dotnet/runtime", "Runtime, dependency injection, WMI and Event Log access."),
    ];

    [ObservableProperty] private string _chipset = "";
    [ObservableProperty] private string _psuWatts = "";
    [ObservableProperty] private string _caseFormFactor = "";
    [ObservableProperty] private string _caseMaxGpuLength = "";
    [ObservableProperty] private string _freeM2Slots = "";
    [ObservableProperty] private string? _savedMessage;

    public string DetectedChipset => State.Profile is null ? "" :
        State.Specs?.Chipset is { } c && string.IsNullOrEmpty(_settings.Current.System.Chipset)
            ? $"Detected: {c.Name}"
            : "Not detected from the board name. Prebuilt PCs often hide it; check the manual or the sticker on the board.";

    public bool IsElevated => _sensors.IsElevated;
    public string ElevationText => IsElevated
        ? "Running as administrator: all sensors are available."
        : "Running without administrator rights. CPU temperatures and some clocks need elevation.";
    public string PawnIoText => LibreSensorMonitor.IsPawnIoInstalled
        ? "PawnIO driver is installed, so low-level sensors work when elevated."
        : "The PawnIO driver isn't installed. Some CPU temperature sensors need it, even when running as administrator (get it from pawnio.eu).";
    public string DataFolder => SettingsStore.DataDirectory;

    public SettingsViewModel(AppState state, SettingsStore settings, ISensorMonitor sensors, IChipsetCatalog chipsets, IPartsCatalog parts)
    {
        State = state;
        _settings = settings;
        _sensors = sensors;
        Chipsets = ["", .. chipsets.All.Select(c => c.Name).OrderBy(n => n)];
        DataNotices = parts.Attributions.Select(a => new NoticeRow(a.Name, a.License, a.Url, a.Notice)).ToList();
        state.SpecsChanged += (_, _) => OnPropertyChanged(nameof(DetectedChipset));
        Load();
    }

    private void Load()
    {
        var o = _settings.Current.System;
        Chipset = o.Chipset ?? "";
        PsuWatts = o.PsuWatts?.ToString(CultureInfo.CurrentCulture) ?? "";
        CaseFormFactor = o.CaseFormFactor ?? "";
        CaseMaxGpuLength = o.CaseMaxGpuLengthMm?.ToString(CultureInfo.CurrentCulture) ?? "";
        FreeM2Slots = o.FreeM2Slots?.ToString(CultureInfo.CurrentCulture) ?? "";
    }

    [RelayCommand]
    private void Save()
    {
        var o = _settings.Current.System;
        o.Chipset = string.IsNullOrWhiteSpace(Chipset) ? null : Chipset.Trim();
        o.PsuWatts = ParseInt(PsuWatts);
        o.CaseFormFactor = string.IsNullOrWhiteSpace(CaseFormFactor) ? null : CaseFormFactor;
        o.CaseMaxGpuLengthMm = ParseInt(CaseMaxGpuLength);
        o.FreeM2Slots = ParseInt(FreeM2Slots, allowZero: true);
        _settings.Save();
        State.RebuildSpecs();
        SavedMessage = "Saved. Compatibility checks and upgrade recommendations now use these values.";
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // The user declined the UAC prompt; keep running unelevated.
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static int? ParseInt(string s, bool allowZero = false) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v) && (v > 0 || allowZero && v == 0) ? v : null;
}
