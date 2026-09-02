using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Measurement-only: Voronoi occupancy of active incidents by parkers vs a nonexistent
/// C3 offense channel. Makes the "C3 structurally dead on unsigned positions" claim
/// visible. Does not change Decide. Sample tick = -1 is the end-of-trial row.
/// </summary>
public sealed class LloydOccupancyWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a Lloyd occupancy watcher.</summary>
    public LloydOccupancyWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "LloydOccupancy";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial",
            "skeptic_count",
            "owned_cues_honest", "relinquished_cues_honest",
            "voronoi_owner_dec", "voronoi_owner_honest",
            "parkers_sitting", "offenses"
        };

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial) { }

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        if (tick % _sampleEvery != 0) return;
        _rows.Add(Sample(world, tick, trial));
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial) =>
        _rows.Add(Sample(world, tick: -1, trial));

    private static double[] Sample(SimWorld world, long tick, int trial)
    {
        var honest = world.Agents.OfType<LloydCoopAgent>().ToList();
        var parkers = world.Agents
            .Where(a => a.AgentType.Contains("LloydPark", StringComparison.OrdinalIgnoreCase)
                        || a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase)
                           && a is LloydParkDeceptiveAgent)
            .ToList();
        if (parkers.Count == 0)
            parkers = world.Agents.Where(a => a is LloydParkDeceptiveAgent).ToList();

        double owned = honest.Count == 0 ? 0 : honest.Sum(h => h.LastOwnedCueCount);
        double relinq = honest.Count == 0 ? 0 : honest.Sum(h => h.LastRelinquishedCueCount);

        double ownerDec = 0, ownerHon = 0;
        foreach (var inc in world.ActiveIncidents)
        {
            if (world.Agents.Count == 0) continue;
            var closest = world.Agents
                .OrderBy(a => world.Distance(a.Position, inc.Position))
                .First();
            if (closest is LloydParkDeceptiveAgent ||
                closest.AgentType.Contains("LloydPark", StringComparison.OrdinalIgnoreCase))
                ownerDec++;
            else
                ownerHon++;
        }

        double sitting = parkers.Count(p => p.LastAction == SensorAnalysis.Core.Domain.AgentAction.Stay);

        return new double[]
        {
            tick,
            trial,
            0, // no skeptic on this channel
            owned, relinq,
            ownerDec, ownerHon,
            sitting,
            0 // no offense channel
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
