using System.Windows;
using SensorAnalysis.App.ViewModels;

namespace SensorAnalysis.App.Views;

/// <summary>Main application window hosting the simulation canvas and controls.</summary>
public partial class MainWindow : Window
{
    /// <summary>Initializes the window, view model, and canvas binding.</summary>
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            vm.Canvas = WorldView;
            vm.TryLoadStartupRecipe();
        };
        Closing += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.SaveAppSettings();
        };
    }
}
