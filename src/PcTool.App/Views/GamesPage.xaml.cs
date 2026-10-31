using System.Windows.Controls;
using PcTool.App.ViewModels;

namespace PcTool.App.Views;

public partial class GamesPage : Page
{
    public GamesViewModel ViewModel { get; }

    public GamesPage(GamesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}
