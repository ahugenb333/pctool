using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcTool.App.Services;
using PcTool.Compatibility;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.App.ViewModels;

public sealed record PartRow(Part Part, CompatibilityReport Report, decimal? PriceUsd)
{
    public string Name => Part.DisplayName;
    public string Category => Part.Category.ToString();
    public CheckStatus Overall => Report.Overall;
    public string OverallText => Report.Overall switch
    {
        CheckStatus.Pass => "Compatible",
        CheckStatus.Warn => "Compatible with caveats",
        CheckStatus.Fail => "Not compatible",
        _ => "Couldn't fully verify",
    };
    public string Price => PriceUsd is { } p ? $"~${p:0}" : "";
    public string Specs => Part switch
    {
        CpuPart c => $"{c.Cores}C/{c.Threads}T · {c.BoostGhz:0.0} GHz · {c.Socket} · {c.TdpW} W",
        GpuPart g => $"{g.VramGb} GB · {g.TgpW} W · {(g.LengthMm > 0 ? $"{g.LengthMm:F0} mm" : "length n/a")}",
        MotherboardPart m => $"{m.Socket} · {m.Chipset} · {m.FormFactor} · {m.RamType} · {m.M2Slots}x M.2",
        RamPart r => $"{r.CapacityGb} GB ({r.ModuleCount} modules) · {r.RamType}-{r.SpeedMtps} CL{r.CasLatency}",
        StoragePart s => $"{s.CapacityGb} GB · {s.Interface} · {s.ReadMbps} MB/s read",
        PsuPart p => $"{p.WattageW} W · {p.Efficiency}",
        CasePart k => $"{k.FormFactor} · GPU up to {k.MaxGpuLengthMm} mm",
        _ => "",
    };
}

public sealed partial class CompatibilityViewModel : ObservableObject
{
    private const string AllCategories = "All parts";
    private readonly CompatibilityEngine _engine;
    private readonly IPartsCatalog _parts;
    private readonly IPriceSource _prices;
    private List<PartRow> _all = [];

    public AppState State { get; }
    public IReadOnlyList<string> Categories { get; } = [AllCategories, .. Enum.GetNames<PartCategory>()];
    public ObservableCollection<PartRow> Parts { get; } = [];
    public string DataNotice { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedCategory = AllCategories;
    [ObservableProperty] private bool _onlyCompatible;
    [ObservableProperty] private PartRow? _selectedPart;

    public CompatibilityViewModel(AppState state, CompatibilityEngine engine, IPartsCatalog parts, IPriceSource prices)
    {
        State = state;
        _engine = engine;
        _parts = parts;
        _prices = prices;
        DataNotice = "Parts data: " + string.Join("; ", parts.Attributions.Select(a => $"{a.Name} ({a.License})")) + $". Prices: {prices.Description}.";
        state.SpecsChanged += (_, _) => Evaluate();
        Evaluate();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnOnlyCompatibleChanged(bool value) => ApplyFilter();

    private void Evaluate()
    {
        _all = State.Specs is { } s
            ? _parts.Parts.Select(p => new PartRow(p, _engine.Check(p, s), _prices.GetPriceUsd(p.Id))).ToList()
            : [];
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var rows = _all
            .Where(r => SelectedCategory == AllCategories || r.Part.Category.ToString() == SelectedCategory)
            .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Specs.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(r => !OnlyCompatible || r.Report.Fits)
            .OrderBy(r => r.Part.Category)
            .ThenBy(r => r.Overall == CheckStatus.Fail)
            .ThenBy(r => r.Name);

        var selectedId = SelectedPart?.Part.Id;
        Parts.Clear();
        foreach (var r in rows) Parts.Add(r);
        SelectedPart = Parts.FirstOrDefault(p => p.Part.Id == selectedId) ?? Parts.FirstOrDefault();
    }
}
