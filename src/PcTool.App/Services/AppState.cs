using CommunityToolkit.Mvvm.ComponentModel;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Data;

namespace PcTool.App.Services;

/// <summary>Shared state for all pages: the detected profile, the resolved system specs and the latest benchmark.</summary>
public sealed partial class AppState : ObservableObject
{
    private readonly IHardwareProfiler _profiler;
    private readonly SystemSpecsBuilder _specsBuilder;
    private readonly IPerformanceTierSource _tiers;

    public SettingsStore Settings { get; }
    public BenchmarkHistory Benchmarks { get; }

    public IReadOnlyList<ComparePreset> ComparePresetOptions { get; } = Core.ComparePresets.All;

    [ObservableProperty] private HardwareProfile? _profile;
    [ObservableProperty] private SystemSpecs? _specs;
    [ObservableProperty] private CompareScale _compareScale;
    [ObservableProperty] private bool _isProfiling;
    [ObservableProperty] private string? _profileError;

    public event EventHandler? SpecsChanged;

    public AppState(
        IHardwareProfiler profiler,
        SystemSpecsBuilder specsBuilder,
        IPerformanceTierSource tiers,
        SettingsStore settings,
        BenchmarkHistory benchmarks)
    {
        _profiler = profiler;
        _specsBuilder = specsBuilder;
        _tiers = tiers;
        Settings = settings;
        Benchmarks = benchmarks;
        _compareScale = CompareScale.FromPreset(Core.ComparePresets.Get(settings.Current.ComparePresetId), tiers);
    }

    public string ComparePresetId
    {
        get => Settings.Current.ComparePresetId;
        set
        {
            var id = string.IsNullOrWhiteSpace(value) ? Core.ComparePresets.DefaultId : value;
            if (Settings.Current.ComparePresetId == id) return;
            Settings.Current.ComparePresetId = id;
            Settings.Save();
            RebuildCompareScale();
            OnPropertyChanged(nameof(ComparePresetId));
            SpecsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RefreshProfileAsync()
    {
        if (IsProfiling) return;
        IsProfiling = true;
        ProfileError = null;
        try
        {
            Profile = await _profiler.CaptureAsync();
            RebuildSpecs();
        }
        catch (Exception ex)
        {
            ProfileError = ex.Message;
        }
        finally
        {
            IsProfiling = false;
        }
    }

    /// <summary>Re-resolves specs after the profile, the user overrides or the latest benchmark change.</summary>
    public void RebuildSpecs()
    {
        if (Profile is null) return;
        Specs = _specsBuilder.Build(Profile, Settings.Current.System, Benchmarks.Latest);
        RebuildCompareScale();
        SpecsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildCompareScale() =>
        CompareScale = CompareScale.FromPreset(Core.ComparePresets.Get(Settings.Current.ComparePresetId), _tiers);
}
