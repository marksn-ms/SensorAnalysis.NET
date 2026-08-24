using System.Globalization;
using System.Text;
using SensorAnalysis.Sphere.Core.Simulation;

namespace SensorAnalysis.Sphere.Core.Serialization;

/// <summary>Exports spherical coverage watcher samples to a portable CSV file.</summary>
public static class SphericalMetricsExporter
{
    /// <summary>Writes coverage watcher samples as CSV at <paramref name="path"/>.</summary>
    public static void ExportCsv(string path, IEnumerable<SphericalMetricSample> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(samples);

        var csv = new StringBuilder("tick,coverage,agents,active_incidents,last_drain\r\n");
        foreach (var sample in samples)
        {
            csv.Append(sample.Tick).Append(',')
                .Append(sample.Coverage.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Agents).Append(',')
                .Append(sample.ActiveIncidents).Append(',')
                .Append(sample.LastDrain.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        File.WriteAllText(path, csv.ToString());
    }
}
