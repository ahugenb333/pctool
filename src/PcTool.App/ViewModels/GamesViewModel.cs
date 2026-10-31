using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcTool.App.Services;
using PcTool.Core.Models;
using PcTool.Games;

namespace PcTool.App.ViewModels;

public sealed record GameRow(GameVerdict Verdict)
{
    public string Name => Verdict.Game.Name;
    public string Meta => $"{Verdict.Game.Year} · {string.Join(", ", Verdict.Game.Genres)}";
    public GameVerdictLevel Level => Verdict.Level;
    public string LevelText => Verdict.Level switch
    {
        GameVerdictLevel.RecommendedPlus => "Runs great",
        GameVerdictLevel.Recommended => "Runs well",
        GameVerdictLevel.Minimum => "Playable",
        _ => "Below minimum",
    };
    public string Settings => Verdict.SuggestedSettings;
    public string Bottleneck => Verdict.Bottleneck;
    public string Notes => string.Join(" ", Verdict.Notes);
    public string Requirements =>
        $"Min: {Verdict.Game.Minimum.RamGb} GB RAM · Rec: {Verdict.Game.Recommended.RamGb} GB RAM, {Verdict.Game.Recommended.VramGb} GB VRAM, {Verdict.Game.Recommended.StorageGb} GB disk";
}

public sealed partial class GamesViewModel : ObservableObject
{
    private const string AllGenres = "All genres";
    private readonly GameAdvisor _advisor;
    private List<GameVerdict> _all = [];

    public AppState State { get; }
    public ObservableCollection<GameRow> Games { get; } = [];
    public IReadOnlyList<string> Genres { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedGenre = AllGenres;
    [ObservableProperty] private bool _onlyRunsWell;
    [ObservableProperty] private string _summary = "";

    public GamesViewModel(AppState state, GameAdvisor advisor)
    {
        State = state;
        _advisor = advisor;
        Genres = [AllGenres, .. advisor.Genres];
        state.SpecsChanged += (_, _) => Evaluate();
        Evaluate();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedGenreChanged(string value) => ApplyFilter();
    partial void OnOnlyRunsWellChanged(bool value) => ApplyFilter();

    private void Evaluate()
    {
        _all = State.Specs is { } s ? _advisor.EvaluateAll(s).ToList() : [];
        var great = _all.Count(v => v.Level >= GameVerdictLevel.Recommended);
        var playable = _all.Count(v => v.Level == GameVerdictLevel.Minimum);
        var below = _all.Count(v => v.Level == GameVerdictLevel.BelowMinimum);
        Summary = _all.Count == 0
            ? "Detecting hardware..."
            : $"{great} of {_all.Count} games meet the recommended spec, {playable} are playable at lower settings, {below} fall below the minimum.";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var rows = _all
            .Where(v => q.Length == 0 || v.Game.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(v => SelectedGenre == AllGenres || v.Game.Genres.Contains(SelectedGenre, StringComparer.OrdinalIgnoreCase))
            .Where(v => !OnlyRunsWell || v.Level >= GameVerdictLevel.Recommended)
            .OrderByDescending(v => v.Level)
            .ThenByDescending(v => v.Game.Year)
            .ThenBy(v => v.Game.Name);

        Games.Clear();
        foreach (var v in rows) Games.Add(new GameRow(v));
    }
}
