using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PcTool.App.Services;
using PcTool.App.ViewModels;
using PcTool.App.Views;
using PcTool.Benchmarks;
using PcTool.Compatibility;
using PcTool.Core;
using PcTool.Data;
using PcTool.Diagnostics;
using PcTool.Games;
using PcTool.Hardware;
using PcTool.Upgrades;

namespace PcTool.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Page to open first, from <c>--page Games</c> (the view name without the "Page" suffix).</summary>
    public static string? StartPage { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var pageArg = Array.IndexOf(e.Args, "--page");
        if (pageArg >= 0 && pageArg + 1 < e.Args.Length) StartPage = e.Args[pageArg + 1];
        DispatcherUnhandledException += OnUnhandledException;

        var services = new ServiceCollection();

        var catalogs = EmbeddedCatalogs.Load();
        services.AddSingleton<IPerformanceTierSource>(catalogs.Tiers);
        services.AddSingleton<IChipsetCatalog>(catalogs.Chipsets);
        services.AddSingleton<IGameCatalog>(catalogs.Games);
        services.AddSingleton<IPartsCatalog>(catalogs.Parts);
        services.AddSingleton<IPriceSource>(catalogs.Prices);

        services.AddSingleton<IHardwareProfiler, WindowsHardwareProfiler>();
        services.AddSingleton<ISensorMonitor, LibreSensorMonitor>();
        services.AddSingleton<SystemSpecsBuilder>();
        services.AddSingleton<BenchmarkSuite>();
        services.AddSingleton<CompatibilityEngine>();
        services.AddSingleton<GameAdvisor>();
        services.AddSingleton<GameDiagnostics>();
        services.AddSingleton<UpgradeAdvisor>();

        services.AddSingleton<SettingsStore>();
        services.AddSingleton<BenchmarkHistory>();
        services.AddSingleton<AppState>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<OverviewPage>();
        services.AddSingleton<BenchmarksViewModel>();
        services.AddSingleton<BenchmarksPage>();
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<GamesPage>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<DiagnosticsPage>();
        services.AddSingleton<CompatibilityViewModel>();
        services.AddSingleton<CompatibilityPage>();
        services.AddSingleton<UpgradesViewModel>();
        services.AddSingleton<UpgradesPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();

        Services = services.BuildServiceProvider();

        GpuStressHub.SessionAvailable += () =>
        {
            if (!GpuStressHub.ShowWindowDuringBenchmark) return;
            Current.Dispatcher.Invoke(() =>
                GpuArenaPreviewWindow.ShowSingle(Services.GetRequiredService<MainWindow>(), closeWhenBenchmarkEnds: true));
        };

        GpuStressHub.BenchmarkGpuStressFinished += () =>
            Current.Dispatcher.Invoke(GpuArenaPreviewWindow.CloseBenchmarkPreview);

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "PcTool error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
