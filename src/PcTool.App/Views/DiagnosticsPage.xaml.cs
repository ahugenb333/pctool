using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
