using System.Windows;
using Velopack;

namespace SensorAnalysis.App;

/// <summary>WPF application entry point for the SensorAnalysis GUI.</summary>
public partial class App : Application
{
    /// <summary>
    /// Velopack handles install and update hooks before WPF starts.
    /// Those hooks must stay ahead of any window so an update can exit quickly.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
