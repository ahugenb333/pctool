using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PcTool.App.Services;
using PcTool.Compatibility;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Upgrades;

namespace PcTool.App.ViewModels;

public sealed record GoalOption(UpgradeGoal Goal, string Label)
{
    public override string ToString() => Label;
}

public sealed record RatioRow(string Component, double Ratio)
{
    public string Text => $"{Ratio:P0}";
    public double Bar => Math.Clamp(Ratio / 2, 0, 1) * 100;
    public CheckStatus Status => Ratio >= 1 ? CheckStatus.Pass : Ratio >= 0.8 ? CheckStatus.Warn : CheckStatus.Fail;
}

public sealed record CandidateRow(string Tier, UpgradeCandidate Candidate)
{
    public string Title => Candidate.Title;
    public string Gain => $"+{Candidate.GainPercent:0}%";
    public string Price => Candidate.PriceUsd is { } p ? $"~${p:0}" : "price n/a";
    public string Value => Candidate.GainPer100Usd is { } v ? $"{v:0.0}% per $100" : "";
    public string Reason => Candidate.Reason;
    public string PartsList => string.Join(" + ", Candidate.Parts.Select(p => p.DisplayName));
    public string Unlocks => Candidate.GamesUnlocked.Count == 0 ? "" : "Unlocks: " + string.Join(", ", Candidate.GamesUnlocked.Take(6)) + (Candidate.GamesUnlocked.Count > 6 ? $" and {Candidate.GamesUnlocked.Count - 6} more" : "");
    public CheckStatus Status => Candidate.Overall;
    public string Caveats => string.Join(" ", Candidate.Compatibility.SelectMany(r => r.Checks).Where(c => c.Status is CheckStatus.Warn or CheckStatus.Unknown).Select(c => c.Message).Distinct());
}

public sealed partial class UpgradesViewModel : ObservableObject
{
    private readonly UpgradeAdvisor _advisor;
    private readonly SettingsStore _settings;
    private bool _loading = true;

    public AppState State { get; }
    public IReadOnlyList<GoalOption> Goals { get; } =
    [
        new(UpgradeGoal.Gaming1080p, "1080p gaming"),
        new(UpgradeGoal.Gaming1440p, "1440p gaming"),
        new(UpgradeGoal.Gaming4K, "4K gaming"),
        new(UpgradeGoal.Productivity, "Productivity (rendering, compiling, video)"),
        new(UpgradeGoal.SpecificGame, "A specific game"),
    ];
    public IReadOnlyList<GameEntry> GameList { get; }
    public ObservableCollection<RatioRow> Ratios { get; } = [];
    public ObservableCollection<CandidateRow> Picks { get; } = [];
    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    [ObservableProperty] private GoalOption _selectedGoal;
    [ObservableProperty] private GameEntry? _selectedGame;
    [ObservableProperty] private string _budget;
    [ObservableProperty] private string _bottleneckSummary = "";
    [ObservableProperty] private string _priceNote = "";
    [ObservableProperty] private bool _isLaptop;

    public bool IsGameGoal => SelectedGoal.Goal == UpgradeGoal.SpecificGame;

    public UpgradesViewModel(AppState state, UpgradeAdvisor advisor, SettingsStore settings, IGameCatalog games)
    {
        State = state;
        _advisor = advisor;
        _settings = settings;
        GameList = games.Games.OrderBy(g => g.Name).ToList();
        var s = settings.Current;
        _selectedGoal = Goals.FirstOrDefault(g => g.Goal.ToString() == s.UpgradeGoal) ?? Goals[1];
        _budget = s.UpgradeBudgetUsd.ToString("0", CultureInfo.InvariantCulture);
        _selectedGame = GameList.FirstOrDefault();
        state.SpecsChanged += (_, _) => Recalculate();
        _loading = false;
        Recalculate();
    }

    partial void OnSelectedGoalChanged(GoalOption value)
    {
        OnPropertyChanged(nameof(IsGameGoal));
        Persist();
        Recalculate();
    }

    partial void OnSelectedGameChanged(GameEntry? value) => Recalculate();

    partial void OnBudgetChanged(string value)
    {
        Persist();
        Recalculate();
    }

    private decimal BudgetValue => decimal.TryParse(Budget, NumberStyles.Number, CultureInfo.CurrentCulture, out var b) ? b : 0;

    private void Persist()
    {
        if (_loading) return;
        _settings.Current.UpgradeGoal = SelectedGoal.Goal.ToString();
        _settings.Current.UpgradeBudgetUsd = BudgetValue;
        _settings.Save();
    }

    private void Recalculate()
    {
        if (_loading || State.Specs is not { } s) return;
        IsLaptop = CompatibilityEngine.IsLaptop(s);
        var plan = _advisor.Recommend(s, new UpgradeOptions(SelectedGoal.Goal, BudgetValue, SelectedGame?.Id));

        BottleneckSummary = plan.Bottleneck.Summary;
        PriceNote = $"Prices: {plan.PriceSource}. Gains are estimates from relative performance tiers, not measured FPS.";

        Ratios.Clear();
        foreach (var (component, ratio) in plan.Bottleneck.Ratios) Ratios.Add(new RatioRow(component, ratio));

        Picks.Clear();
        if (plan.Good is { } good) Picks.Add(new CandidateRow("Best value", good));
        if (plan.Better is { } better) Picks.Add(new CandidateRow("Balanced", better));
        if (plan.Best is { } best) Picks.Add(new CandidateRow("Biggest jump", best));

        Candidates.Clear();
        foreach (var c in plan.Candidates.Take(30)) Candidates.Add(new CandidateRow("", c));
    }
}
