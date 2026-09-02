using System.Windows;

namespace SensorAnalysis.Space.App.Views;

/// <summary>Modal dialog for constructing a volumetric world.</summary>
public partial class WorldOptionsDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public WorldOptionsDialog()
    {
        InitializeComponent();
        ViewModel = new WorldOptionsDialogViewModel();
    }

    /// <summary>Gets or sets the bound options view model.</summary>
    public WorldOptionsDialogViewModel ViewModel
    {
        get => (WorldOptionsDialogViewModel)DataContext;
        set => DataContext = value;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuild(out _, out _, out var error))
        {
            MessageBox.Show(this, error, "World options", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
