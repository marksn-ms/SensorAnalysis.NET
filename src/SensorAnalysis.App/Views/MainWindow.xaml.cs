using System.Windows;
using SensorAnalysis.App.ViewModels;
using SensorAnalysis.Distribution;
using SensorAnalysis.Updates;

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
        Loaded += async (_, _) =>
        {
            vm.Canvas = WorldView;
            vm.TryLoadStartupRecipe();
            if (ProductUpdates.ShouldOfferOnStartup())
                await ProductUpdates.OfferAsync(ProductIds.SensorAnalysis, this, interactive: false);
        };
        Closing += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.SaveAppSettings();
        };
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        await ProductUpdates.OfferAsync(ProductIds.SensorAnalysis, this, interactive: true);
}
