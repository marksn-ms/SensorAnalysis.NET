using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Metrics;

/// <summary>
/// Observes the world each tick (or at sample intervals) and records metrics
/// for later export. Classic SensorAnalysis "Watcher" concept.
/// </summary>
public interface IWatcher : IDisposable
{
    /// <summary>Short identifier used as a metrics column prefix and for display.</summary>
    string Name { get; }

    /// <summary>Called once at the start of a trial.</summary>
    void OnTrialStart(SimWorld world, int trial);

    /// <summary>Called after each tick (or every N ticks, depending on implementation).</summary>
    void OnTick(SimWorld world, long tick, int trial);

    /// <summary>Called at the end of a trial.</summary>
    void OnTrialEnd(SimWorld world, int trial);

    /// <summary>Column headers for CSV export.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>All recorded rows (each row is one sample).</summary>
    IReadOnlyList<IReadOnlyList<double>> Rows { get; }
}
