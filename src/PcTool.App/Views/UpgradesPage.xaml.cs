using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class UpgradesPage : Page
{
    public UpgradesViewModel ViewModel { get; }

    public UpgradesPage(UpgradesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
