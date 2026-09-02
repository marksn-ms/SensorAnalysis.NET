using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Measurement-only: peel-phase occupancy vs heading-mismatch offense fire for
/// <see cref="IBoidsPeelTelemetry"/> skeptics. Sample tick = -1 is the end-of-trial row.
/// </summary>
public sealed class BoidsPeelWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a boids-peel watcher.</summary>
    public BoidsPeelWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "BoidsPeel";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial",
            "skeptic_count", "grace_ticks",
            "mean_trust_vs_deceptive", "mean_trust_vs_squad",
            "peelers_peeling_now", "peel_ticks_lifetime",
            "anti_align_ticks_dec", "anti_align_ticks_squad",
            "peel_offenses_dec", "peel_offenses_squad"
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
        var skeptics = world.Agents.OfType<IBoidsPeelTelemetry>().ToList();
        var skepticIds = skeptics.Select(s => s.Id).ToHashSet();
        var peelers = world.Agents.OfType<IBoidsPeelAttackTelemetry>().ToList();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase)
                        || a.AgentType.Contains("Peel", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToHashSet();

        double meanTrustDec = double.NaN;
        double meanTrustSquad = double.NaN;
        if (skeptics.Count > 0 && deceptiveIds.Count > 0)
            meanTrustDec = skeptics.Average(s => deceptiveIds.Average(s.MeanTrustToward));
        if (skeptics.Count >= 2)
        {
            meanTrustSquad = skeptics.Average(s =>
            {
                var peers = skeptics.Where(o => o.Id != s.Id).Select(o => o.Id).ToList();
                return peers.Count == 0 ? double.NaN : peers.Average(s.MeanTrustToward);
            });
        }

        double peelingNow = peelers.Sum(p => p.IsPeeling);
        double peelLife = peelers.Sum(p => p.LifetimePeelTicks);
        double antiDec = 0, antiSquad = 0, offDec = 0, offSquad = 0;
        int grace = skeptics.Count > 0 ? skeptics[0].PeelGraceTicks : 0;

        foreach (var s in skeptics)
        {
            foreach (var (id, ticks) in s.LifetimeAntiAlignTicks)
            {
                if (deceptiveIds.Contains(id)) antiDec += ticks;
                else if (skepticIds.Contains(id) && id != s.Id) antiSquad += ticks;
            }

            foreach (var id in deceptiveIds)
                offDec += s.PeelOffenseCountToward(id);
            foreach (var id in skepticIds)
            {
                if (id == s.Id) continue;
                offSquad += s.PeelOffenseCountToward(id);
            }
        }

        return new double[]
        {
            tick,
            trial,
            skeptics.Count,
            grace,
            double.IsNaN(meanTrustDec) ? -1 : meanTrustDec,
            double.IsNaN(meanTrustSquad) ? -1 : meanTrustSquad,
            peelingNow, peelLife,
            antiDec, antiSquad,
            offDec, offSquad
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
