using System.Windows;
using Velopack;

namespace SensorAnalysis.Space.App;

/// <summary>Application entry point for the volumetric SensorAnalysis.Space platform.</summary>
public partial class App : Application
{
    /// <summary>Velopack hooks run before WPF so install and update commands can exit quickly.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
