using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class CompatibilityPage : Page
{
    public CompatibilityViewModel ViewModel { get; }

    public CompatibilityPage(CompatibilityViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
