using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class OverviewPage : Page
{
    public OverviewViewModel ViewModel { get; }

    public OverviewPage(OverviewViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.OnNavigatedTo();
        Unloaded += (_, _) => viewModel.OnNavigatedFrom();
    }
}
