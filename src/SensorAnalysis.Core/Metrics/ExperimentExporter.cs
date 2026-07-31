using System.Globalization;
using System.Text.Json;

namespace SensorAnalysis.Core.Metrics;

/// <summary>
/// Utilities to export experiment metadata and collected metrics to disk (JSON + CSV).
/// </summary>
public static class ExperimentExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Export the provided experiment manifest and watcher metrics to the specified directory.
    /// This writes two files:
    /// - "run-meta.json" : human-friendly JSON representation of <see cref="ExperimentManifest"/>.
    /// - "metrics.csv"   : CSV with a column block for each watcher and one row per recorded tick.
    /// </summary>
    /// <param name="directory">Directory where export files will be created. The directory will be created if it does not exist.</param>
    /// <param name="manifest">Experiment manifest describing the run.</param>
    /// <param name="watchers">Collection of watchers that produced metric columns and rows.</param>
    public static void Export(string directory, ExperimentManifest manifest, IEnumerable<IWatcher> watchers)
    {
        if (directory is null) throw new ArgumentNullException(nameof(directory));
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (watchers is null) throw new ArgumentNullException(nameof(watchers));

        Directory.CreateDirectory(directory);

        var metaPath = Path.Combine(directory, "run-meta.json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(manifest, Options));

        var csvPath = Path.Combine(directory, "metrics.csv");
        using var writer = new StreamWriter(csvPath);

        var watcherList = watchers.ToList();

        // Header: each watcher contributes its column names prefixed by watcher name to avoid collisions.
        var headerCells = watcherList.SelectMany(w => w.ColumnNames.Select(name => $"{w.Name}.{name}")).ToList();
        writer.WriteLine(string.Join(",", headerCells));

        // Rows: write values per tick; missing values are emitted as empty strings.
        var rowCount = watcherList.Count == 0 ? 0 : watcherList.Max(w => w.Rows.Count);
        for (int i = 0; i < rowCount; i++)
        {
            var cells = new List<string>();
            foreach (var w in watcherList)
            {
                if (i < w.Rows.Count)
                {
                    foreach (var v in w.Rows[i])
                        cells.Add(v.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    // Pad with empty cells when a watcher has fewer rows.
                    foreach (var _ in w.ColumnNames)
                        cells.Add("");
                }
            }
            writer.WriteLine(string.Join(",", cells));
        }
    }
}

/// <summary>Metadata describing one experimental run (one seed / one trial set).</summary>
public sealed class ExperimentManifest
{
    /// <summary>Experiment identifier or name.</summary>
    public string ExperimentName { get; set; } = "experiment";

    /// <summary>UTC timestamp when the experiment started.</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the experiment finished.</summary>
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>Base RNG seed used for the experiment.</summary>
    public int Seed { get; set; }

    /// <summary>Number of trials executed for this experiment.</summary>
    public int Trials { get; set; }

    /// <summary>Number of simulation ticks executed per trial.</summary>
    public int TicksPerTrial { get; set; }

    /// <summary>Title of the world used for the experiment.</summary>
    public string WorldTitle { get; set; } = "";

    /// <summary>Number of tile columns in the simulated world.</summary>
    public int TileColumns { get; set; }

    /// <summary>Number of tile rows in the simulated world.</summary>
    public int TileRows { get; set; }

    /// <summary>Number of agents deployed in the experiment.</summary>
    public int AgentCount { get; set; }

    /// <summary>Number of incidents present in the experiment.</summary>
    public int IncidentCount { get; set; }

    /// <summary>Array of agent type names present in the run.</summary>
    public string[] AgentTypes { get; set; } = Array.Empty<string>();

    /// <summary>Optional notes about the experiment run.</summary>
    public string Notes { get; set; } = "";
}
