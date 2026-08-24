using System.Windows;

namespace SensorAnalysis.Sphere.App.Views;

/// <summary>Modal dialog for constructing a spherical geodesic world.</summary>
public partial class SphereWorldOptionsDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public SphereWorldOptionsDialog()
    {
        InitializeComponent();
        ViewModel = new SphereWorldOptionsDialogViewModel();
    }

    /// <summary>Bound options view model.</summary>
    public SphereWorldOptionsDialogViewModel ViewModel
    {
        get => (SphereWorldOptionsDialogViewModel)DataContext;
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
