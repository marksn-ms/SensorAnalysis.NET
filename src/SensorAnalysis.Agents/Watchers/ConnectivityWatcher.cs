using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Counts agent–agent pairs within sensor range (a simple connectivity proxy).
/// </summary>
public sealed class ConnectivityWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>
    /// Creates a connectivity watcher that samples every <paramref name="sampleEveryTicks"/> ticks.
    /// </summary>
    /// <param name="sampleEveryTicks">Sampling interval in ticks. Values less than 1 are treated as 1.</param>
    public ConnectivityWatcher(int sampleEveryTicks = 1) =>
        _sampleEvery = Math.Max(1, sampleEveryTicks);

    /// <inheritdoc />
    public string Name => "Connectivity";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[] { "tick", "trial", "agent_edges", "avg_degree" };

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial) { }

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        if (tick % _sampleEvery != 0) return;

        var agents = world.Agents;
        int edges = 0;
        for (int i = 0; i < agents.Count; i++)
        for (int j = i + 1; j < agents.Count; j++)
        {
            var range = Math.Min(agents[i].SensorRange, agents[j].SensorRange);
            if (world.Distance(agents[i].Position, agents[j].Position) <= range)
                edges++;
        }

        var avgDegree = agents.Count == 0 ? 0 : 2.0 * edges / agents.Count;
        _rows.Add(new double[] { tick, trial, edges, avgDegree });
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial) { }

    /// <summary>Clears recorded metric rows.</summary>
    public void Dispose() => _rows.Clear();
}
