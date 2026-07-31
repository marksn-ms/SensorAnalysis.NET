using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Metrics;

/// <summary>
/// Tracks fraction of tiles covered at least once, active incidents remaining,
/// agent count, and average agent reward.
/// </summary>
public sealed class CoverageWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>
    /// Creates a coverage watcher that samples every <paramref name="sampleEveryTicks"/> ticks.
    /// </summary>
    /// <param name="sampleEveryTicks">Sampling interval in ticks. Values less than 1 are treated as 1.</param>
    public CoverageWatcher(int sampleEveryTicks = 1)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "Coverage";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[] { "tick", "trial", "coverage", "active_incidents", "agents", "avg_reward", "messages" };

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial) { }

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        if (tick % _sampleEvery != 0) return;

        var tiles = world.Tiles;
        int covered = 0, total = 0;
        for (int c = 0; c < tiles.GetLength(0); c++)
        for (int r = 0; r < tiles.GetLength(1); r++)
        {
            total++;
            if (tiles[c, r].LastCoveredTick >= 0)
                covered++;
        }

        var activeIncidents = world.Incidents.Count(i => !i.IsDepleted);
        var avgReward = world.Agents.Count == 0
            ? 0
            : world.Agents.Average(a => a.TotalReward);

        _rows.Add(new double[]
        {
            tick,
            trial,
            total == 0 ? 0 : (double)covered / total,
            activeIncidents,
            world.Agents.Count,
            avgReward,
            world.Messages.Count
        });
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial) { }

    /// <summary>Clears recorded metric rows.</summary>
    public void Dispose() => _rows.Clear();
}
