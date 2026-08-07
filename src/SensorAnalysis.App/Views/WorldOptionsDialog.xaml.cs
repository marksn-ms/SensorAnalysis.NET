using System.Windows;

namespace SensorAnalysis.App.Views;

/// <summary>
/// Modal dialog for configuring a new simulation world (grid size, tile size, seed).
/// </summary>
public partial class WorldOptionsDialog : Window
{
    /// <summary>Creates the dialog with empty fields (caller should set <see cref="ViewModel"/>).</summary>
    public WorldOptionsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Bound options view model for the form fields.</summary>
    public WorldOptionsDialogViewModel ViewModel
    {
        get => (WorldOptionsDialogViewModel)DataContext;
        set => DataContext = value;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryValidate(out var error))
        {
            MessageBox.Show(this, error, "World options", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
