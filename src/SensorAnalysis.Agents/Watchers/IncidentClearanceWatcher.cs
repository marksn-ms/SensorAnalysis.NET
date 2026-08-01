using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Tracks incident-clearance progress: active count, fraction cleared, and first all-clear tick.
/// Emits one summary row per trial at trial end (plus optional per-tick samples).
/// </summary>
public sealed class IncidentClearanceWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;
    private readonly Dictionary<int, long?> _allClearTick = new();
    private readonly Dictionary<int, int> _initialIncidents = new();

    /// <summary>
    /// Creates an incident-clearance watcher.
    /// </summary>
    /// <param name="sampleEveryTicks">Per-tick sampling interval; use a large value to mostly rely on end-of-trial rows.</param>
    public IncidentClearanceWatcher(int sampleEveryTicks = 1)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "Clearance";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial", "active_incidents", "fraction_cleared",
            "all_clear", "all_clear_tick", "initial_incidents"
        };

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial)
    {
        _initialIncidents[trial] = world.Incidents.Count;
        _allClearTick[trial] = null;
    }

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        var initial = _initialIncidents.GetValueOrDefault(trial, world.Incidents.Count);
        var active = world.Incidents.Count(i => !i.IsDepleted);
        var cleared = initial == 0 ? 1.0 : (double)(initial - active) / initial;
        if (active == 0 && _allClearTick.GetValueOrDefault(trial) is null)
            _allClearTick[trial] = tick;

        if (tick % _sampleEvery != 0) return;

        _rows.Add(new double[]
        {
            tick,
            trial,
            active,
            cleared,
            active == 0 ? 1 : 0,
            _allClearTick.GetValueOrDefault(trial) ?? -1,
            initial
        });
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial)
    {
        var initial = _initialIncidents.GetValueOrDefault(trial, world.Incidents.Count);
        var active = world.Incidents.Count(i => !i.IsDepleted);
        var cleared = initial == 0 ? 1.0 : (double)(initial - active) / initial;
        var clearTick = _allClearTick.GetValueOrDefault(trial);
        // Sentinel end-of-trial row with tick = -1 for easy collation.
        _rows.Add(new double[]
        {
            -1,
            trial,
            active,
            cleared,
            active == 0 ? 1 : 0,
            clearTick ?? -1,
            initial
        });
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
