using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTool.App.Services;
using PcTool.Core;
using PcTool.Core.Models;
using PcTool.Diagnostics;

namespace PcTool.App.ViewModels;

public sealed record GameOption(GameEntry? Game)
{
    public override string ToString() => Game?.Name ?? "General check (no specific game)";
}

public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly GameDiagnostics _diagnostics;

    public AppState State { get; }
    public IReadOnlyList<GameOption> GameOptions { get; }
    public IReadOnlyList<int> LookbackOptions { get; } = [3, 7, 14, 30];
    public ObservableCollection<DiagnosticFinding> Findings { get; } = [];

    [ObservableProperty] private GameOption _selectedGame;
    [ObservableProperty] private string _executableName = "";
    [ObservableProperty] private int _lookbackDays = 14;
    [ObservableProperty] private string _summary = "Pick a game (or run a general check) and press Run diagnostics.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    public DiagnosticsViewModel(AppState state, GameDiagnostics diagnostics, IGameCatalog games)
    {
        State = state;
        _diagnostics = diagnostics;
        GameOptions = [new GameOption(null), .. games.Games.OrderBy(g => g.Name).Select(g => new GameOption(g))];
        _selectedGame = GameOptions[0];
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        Summary = "Reading event logs, settings and sensors...";
        try
        {
            var request = new DiagnosticsRequest(SelectedGame.Game, ExecutableName, State.Specs, State.Profile, LookbackDays);
            var findings = await _diagnostics.RunAsync(request);
            Findings.Clear();
            foreach (var f in findings) Findings.Add(f);

            var problems = findings.Count(f => f.Severity == FindingSeverity.Problem);
            var warnings = findings.Count(f => f.Severity == FindingSeverity.Warning);
            Summary = problems + warnings == 0
                ? "No problems found. If a game still misbehaves, verify its files in the launcher and try again with overlays closed."
                : $"{problems} problem(s) and {warnings} warning(s) found. Start with the red items at the top.";
        }
        catch (Exception ex)
        {
            Summary = $"Diagnostics failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRun() => !IsRunning;
}
