using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using SensorAnalysis.Distribution;
using Velopack;
using Velopack.Sources;

namespace SensorAnalysis.Updates;

/// <summary>Checks one installed product for a newer stable release and applies it on request.</summary>
public static class ProductUpdates
{
    /// <summary>
    /// When set to a directory, the update check reads that folder instead of GitHub.
    /// The directory must contain the product's <c>releases.{channel}.json</c> feed.
    /// </summary>
    public const string LocalFeedVariable = "SENSORANALYSIS_UPDATE_PATH";

    private static int _busy;
    private static ProductCatalog? _catalog;

    /// <summary>Startup checks only interactive launches. Recipe and capture arguments skip the prompt.</summary>
    public static bool ShouldOfferOnStartup()
    {
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.StartsWith("--velo", StringComparison.OrdinalIgnoreCase))
                continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asks the user to install a newer version when one exists.
    /// Unattended startup failures stay silent. The Help menu passes <paramref name="interactive"/> true.
    /// </summary>
    public static async Task OfferAsync(string productId, Window owner, bool interactive)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        try
        {
            var catalog = LoadCatalog();
            var product = catalog.Require(productId);
            var manager = CreateManager(catalog, product);
            try
            {
                if (!manager.IsInstalled)
                {
                    if (interactive)
                        Inform(owner, product.Title, "Updates are available after the application is installed with its Setup program.");
                    return;
                }

                var update = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
                if (update is null)
                {
                    if (interactive)
                        Inform(owner, product.Title, $"You have the latest version ({manager.CurrentVersion}).");
                    return;
                }

                var target = update.TargetFullRelease.Version.ToString();
                var answer = MessageBox.Show(
                    owner,
                    $"{product.Title} {target} is available. The application will close and restart to install it.",
                    product.Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (answer != MessageBoxResult.Yes)
                    return;

                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    await manager.DownloadUpdatesAsync(update).ConfigureAwait(true);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }

                manager.ApplyUpdatesAndRestart(update);
            }
            finally
            {
                (manager as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex}");
            if (interactive)
                Inform(owner, "Update", "Could not check for updates. " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static UpdateManager CreateManager(ProductCatalog catalog, ProductEntry product)
    {
        var local = Environment.GetEnvironmentVariable(LocalFeedVariable);
        if (!string.IsNullOrWhiteSpace(local))
            return new UpdateManager(new SimpleFileSource(new DirectoryInfo(local)));

        return new UpdateManager(new ProductReleaseSource(catalog.Repository, product.Id, product.PackId));
    }

    private static ProductCatalog LoadCatalog()
    {
        if (_catalog is not null)
            return _catalog;

        var assembly = typeof(ProductUpdates).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource =>
            resource.EndsWith("products.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The product manifest is not embedded.");
        using var reader = new StreamReader(stream);
        var temp = Path.Combine(Path.GetTempPath(), "sensoranalysis-products.json");
        File.WriteAllText(temp, reader.ReadToEnd());
        _catalog = ProductCatalog.Load(temp) ?? throw new InvalidOperationException("The product manifest is empty.");
        return _catalog;
    }

    private static void Inform(Window owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
