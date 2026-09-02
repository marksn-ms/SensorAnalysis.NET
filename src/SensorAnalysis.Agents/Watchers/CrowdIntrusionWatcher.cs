using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Measurement-only: personal-space occupancy vs herding-offense fire for
/// <see cref="ICrowdIntrusionTelemetry"/> skeptics (MitigatedOrbit and CrowdAwareMitigated).
/// Makes the "dead P channel" claim visible: ticks inside personal space vs offenses.
/// Does not change agent Decide. Sample tick = -1 is the end-of-trial row.
/// </summary>
public sealed class CrowdIntrusionWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a crowd-intrusion watcher.</summary>
    public CrowdIntrusionWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "CrowdIntrusion";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial",
            "skeptic_count", "grace_ticks",
            "mean_trust_vs_deceptive", "mean_trust_vs_squad",
            "inside_ps_ticks_dec", "closing_ticks_dec",
            "inside_ps_ticks_squad", "closing_ticks_squad",
            "current_intrusion_sum_dec", "current_intrusion_max_dec",
            "peers_inside_now_dec", "peers_inside_now_squad",
            "herding_offenses_dec", "herding_offenses_squad"
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
        var skeptics = world.Agents.OfType<ICrowdIntrusionTelemetry>().ToList();
        var skepticIds = skeptics.Select(s => s.Id).ToHashSet();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase))
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

        double insideDec = 0, closingDec = 0, insideSquad = 0, closingSquad = 0;
        double curSumDec = 0, curMaxDec = 0;
        double insideNowDec = 0, insideNowSquad = 0;
        double offDec = 0, offSquad = 0;
        int grace = skeptics.Count > 0 ? skeptics[0].HerdingGraceTicks : 0;

        foreach (var s in skeptics)
        {
            foreach (var (id, ticks) in s.LifetimeInsidePersonalSpaceTicks)
            {
                if (deceptiveIds.Contains(id)) insideDec += ticks;
                else if (skepticIds.Contains(id) && id != s.Id) insideSquad += ticks;
            }
            foreach (var (id, ticks) in s.LifetimeClosingTicks)
            {
                if (deceptiveIds.Contains(id)) closingDec += ticks;
                else if (skepticIds.Contains(id) && id != s.Id) closingSquad += ticks;
            }
            foreach (var (id, ticks) in s.CurrentIntrusionTicks)
            {
                if (!deceptiveIds.Contains(id)) continue;
                curSumDec += ticks;
                if (ticks > curMaxDec) curMaxDec = ticks;
            }
            foreach (var (id, flag) in s.CurrentlyInsidePersonalSpace)
            {
                if (flag == 0) continue;
                if (deceptiveIds.Contains(id)) insideNowDec += 1;
                else if (skepticIds.Contains(id) && id != s.Id) insideNowSquad += 1;
            }
            foreach (var id in deceptiveIds)
                offDec += s.HerdingOffenseCountToward(id);
            foreach (var id in skepticIds)
            {
                if (id == s.Id) continue;
                offSquad += s.HerdingOffenseCountToward(id);
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
            insideDec, closingDec,
            insideSquad, closingSquad,
            curSumDec, curMaxDec,
            insideNowDec, insideNowSquad,
            offDec, offSquad
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
