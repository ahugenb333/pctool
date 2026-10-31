using System.Windows;
using PcTool.App.Services;
using PcTool.App.Views;
using Wpf.Ui.Appearance;

namespace PcTool.App;

public partial class MainWindow
{
    private readonly AppState _state;

    public MainWindow(IServiceProvider services, AppState state)
    {
        _state = state;
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        RootNavigation.SetServiceProvider(services);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var startPage = App.StartPage is { } name ? typeof(OverviewPage).Assembly.GetType($"PcTool.App.Views.{name}Page", false, true) : null;
        RootNavigation.Navigate(startPage ?? typeof(OverviewPage));
        await _state.RefreshProfileAsync();
    }
}
