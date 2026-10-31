using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class BenchmarksPage : Page
{
    public BenchmarksViewModel ViewModel { get; }

    public BenchmarksPage(BenchmarksViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
