using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PcTool.App.Services;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Hardware;
using Wpf.Ui.Controls;

namespace PcTool.App.ViewModels;

public sealed record SpecRow(string Label, string Value);

public sealed record SpecSection(string Title, SymbolRegular Icon, string Headline, string? Score, IReadOnlyList<SpecRow> Rows);

public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly ISensorMonitor _sensors;
    private readonly DispatcherTimer _sensorTimer;

    public AppState State { get; }
    public ObservableCollection<SpecSection> Sections { get; } = [];
    public ObservableCollection<SensorReading> Sensors { get; } = [];

    [ObservableProperty] private string _sensorNote = "";
    [ObservableProperty] private string? _statusMessage;

    public OverviewViewModel(AppState state, ISensorMonitor sensors)
    {
        State = state;
        _sensors = sensors;
        state.SpecsChanged += (_, _) => Rebuild();
        _sensorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sensorTimer.Tick += async (_, _) => await RefreshSensorsAsync();
        Rebuild();
    }

    public void OnNavigatedTo()
    {
        _sensorTimer.Start();
        _ = RefreshSensorsAsync();
    }

    public void OnNavigatedFrom() => _sensorTimer.Stop();

    [RelayCommand]
    private Task RefreshAsync() => State.RefreshProfileAsync();

    [RelayCommand]
    private void Export()
    {
        if (State.Profile is null) return;
        var dialog = new SaveFileDialog
        {
            FileName = $"pctool-profile-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}.json",
            Filter = "JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, ProfileExporter.ToJson(State.Profile));
        StatusMessage = $"Saved to {dialog.FileName}";
    }

    [RelayCommand]
    private void CopySummary()
    {
        var sb = new StringBuilder();
        foreach (var s in Sections)
        {
            sb.AppendLine($"{s.Title}: {s.Headline}");
            foreach (var r in s.Rows) sb.AppendLine($"  {r.Label}: {r.Value}");
        }
        Clipboard.SetText(sb.ToString());
        StatusMessage = "Specs copied to the clipboard.";
    }

    private async Task RefreshSensorsAsync()
    {
        var readings = await Task.Run(() =>
        {
            try { return _sensors.Read(); }
            catch { return (IReadOnlyList<SensorReading>)[]; }
        });

        var shown = readings
            .Where(r => r.Value is not null && r.Source is SensorSource.Cpu or SensorSource.Gpu or SensorSource.Storage)
            .Where(r => r.Kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Clock or SensorKind.Power or SensorKind.Fan)
            .Where(r => r.Kind != SensorKind.Load || r.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && r.Source == SensorSource.Gpu)
            .Where(r => r.Kind != SensorKind.Clock || r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && !r.Name.Contains('#'))
            .OrderBy(r => r.Source).ThenBy(r => r.Kind).ThenBy(r => r.Name)
            .Take(24)
            .ToList();

        Sensors.Clear();
        foreach (var r in shown) Sensors.Add(r);
        SensorNote = _sensors.IsElevated
            ? "Live readings, refreshed every 2 seconds."
            : "Running without administrator rights: CPU temperatures and some clocks are hidden. Restart as administrator from Settings to see them.";
    }

    private void Rebuild()
    {
        Sections.Clear();
        if (State.Profile is not { } p || State.Specs is not { } s) return;

        var idx = State.CompareScale;
        var cpuScore = s.CpuTier is not null || s.CpuScoreFromBenchmark
            ? $"Single {idx.IndexCpuSingle(s.CpuSingleScore):F0} · Multi {idx.IndexCpuMulti(s.CpuMultiScore):F0} · Gaming {idx.IndexCpuGaming(s.CpuGamingScore):F0} vs {idx.Preset.Label}"
            : "Not in the tier database - run a benchmark";
        Sections.Add(new("Processor", SymbolRegular.DeveloperBoard24, p.Cpu.Name, cpuScore,
        [
            new("Cores / threads", $"{p.Cpu.Cores} / {p.Cpu.Threads}"),
            new("Clock", $"{p.Cpu.MaxClockMhz / 1000.0:F1} GHz"),
            new("Cache", $"L2 {p.Cpu.L2CacheKb / 1024.0:0.#} MB · L3 {p.Cpu.L3CacheKb / 1024.0:0.#} MB"),
            new("Socket", string.IsNullOrEmpty(s.Socket) ? "Unknown" : s.Socket),
        ]));

        foreach (var g in p.Gpus.OrderBy(g => g.IsIntegrated))
        {
            var primary = g == p.PrimaryGpu;
            var score = primary
                ? s.GpuTier is not null || s.GpuScoreFromBenchmark
                    ? $"Score {idx.IndexGpu(s.GpuScore):F0} vs {idx.Preset.Label} · {(g.IsIntegrated ? "shared memory" : $"{g.VramGb:F0} GB VRAM")} · {s.GpuTier?.Upscaling switch { null or "" => "no upscaler", var u => u }}"
                    : "Not in the tier database - run a benchmark"
                : null;
            Sections.Add(new(primary ? "Graphics" : "Graphics (secondary)", SymbolRegular.Video24, g.Name, score,
            [
                new("VRAM", g.IsIntegrated ? "Shared system memory" : $"{g.VramGb:F0} GB"),
                new("Driver", $"{g.DriverVersion}{(g.DriverDate is { } d ? $" ({d:d})" : "")}"),
                new("Display", g.CurrentWidth > 0 ? $"{g.CurrentWidth}x{g.CurrentHeight} @ {g.RefreshRate} Hz" : "-"),
            ]));
        }

        var m = p.Memory;
        Sections.Add(new("Memory", SymbolRegular.Ram20, $"{s.RamGb} GB {(m.Type == MemoryType.Unknown ? "" : m.Type.ToString())}", null,
        [
            new("Speed", m.SpeedMtps > 0 ? $"{m.SpeedMtps} MT/s" : "Unknown"),
            new("Slots", m.SlotCount > 0 ? $"{m.UsedSlots} of {m.SlotCount} used" : $"{m.UsedSlots} modules"),
            new("Max supported", s.MaxRamGb > 0 ? $"{s.MaxRamGb} GB" : "Unknown"),
            new("Modules", string.Join(", ", m.Modules.Select(x => $"{x.CapacityBytes / 1073741824} GB {x.Manufacturer} {x.PartNumber}".Trim()))),
        ]));

        foreach (var d in p.Storage.OrderByDescending(d => d.IsSystemDrive))
        {
            var kind = d.Kind switch { StorageKind.Nvme => "NVMe SSD", StorageKind.SataSsd => "SATA SSD", StorageKind.Hdd => "Hard drive", _ => d.BusType };
            Sections.Add(new(d.IsSystemDrive ? "Storage (Windows)" : "Storage", SymbolRegular.HardDrive20, d.Model, null,
            [
                new("Type", kind),
                new("Size", $"{d.SizeGb:F0} GB"),
                new("Volumes", string.Join(", ", d.Volumes.Select(v => $"{v.Letter} {v.FreeBytes / 1e9:F0} GB free of {v.SizeBytes / 1e9:F0}"))),
            ]));
        }

        var mb = p.Motherboard;
        Sections.Add(new("Motherboard", SymbolRegular.Board24, $"{mb.Manufacturer} {mb.Product}".Trim(), null,
        [
            new("Chipset", s.Chipset?.Name ?? "Unknown - set it in Settings"),
            new("PCIe (GPU slot)", s.PcieGen is { } gen ? $"Gen {gen}" : "Unknown"),
            new("BIOS", $"{mb.BiosVendor} {mb.BiosVersion}{(mb.BiosDate is { } bd ? $" ({bd:d})" : "")}"),
        ]));

        Sections.Add(new("Windows", SymbolRegular.Window24, p.Os.Name, null,
        [
            new("Version", $"{p.Os.Version} (build {p.Os.Build})"),
            new("Computer", p.MachineName),
        ]));
    }
}
