using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Samples clearance progress, mean mitigated trust, and cumulative disrepute
/// (penalty weight / offense counts) toward mitigated peers vs deceptive peers.
/// End-of-trial rows use tick = -1 for collation.
/// </summary>
public sealed class TrustTelemetryWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a trust telemetry watcher.</summary>
    public TrustTelemetryWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "TrustTel";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial", "active_incidents", "fraction_cleared",
            "mitigated_count", "mean_trust_vs_deceptive", "mean_trust_vs_mitigated",
            "disappointment_echoes",
            // Cumulative penalty weight (Beta increments) summed over all mitigated observers
            "disrepute_vs_mitigated", "disrepute_vs_deceptive",
            // Offense event counts by channel × target class
            "off_herding_mit", "off_silence_mit", "off_saturated_mit", "off_verify_mit", "off_gossip_mit",
            "off_herding_dec", "off_silence_dec", "off_saturated_dec", "off_verify_dec", "off_gossip_dec",
            // Penalty weight by channel × target class
            "w_herding_mit", "w_silence_mit", "w_saturated_mit", "w_verify_mit", "w_gossip_mit",
            "w_herding_dec", "w_silence_dec", "w_saturated_dec", "w_verify_dec", "w_gossip_dec"
        };

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<double>> Rows => _rows;

    private readonly Dictionary<int, int> _initial = new();

    /// <inheritdoc />
    public void OnTrialStart(SimWorld world, int trial) =>
        _initial[trial] = world.Incidents.Count;

    /// <inheritdoc />
    public void OnTick(SimWorld world, long tick, int trial)
    {
        if (tick % _sampleEvery != 0) return;
        _rows.Add(Sample(world, tick, trial));
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial) =>
        _rows.Add(Sample(world, tick: -1, trial));

    private double[] Sample(SimWorld world, long tick, int trial)
    {
        var initial = _initial.GetValueOrDefault(trial, world.Incidents.Count);
        var active = world.Incidents.Count(i => !i.IsDepleted);
        var cleared = initial == 0 ? 1.0 : (double)(initial - active) / initial;

        var mitigated = world.Agents.OfType<MitigatedOrbitAgent>().ToList();
        var mitigatedIds = mitigated.Select(m => m.Id).ToHashSet();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToHashSet();

        double meanTrustVsDeceptive = double.NaN;
        if (mitigated.Count > 0 && deceptiveIds.Count > 0)
        {
            meanTrustVsDeceptive = mitigated.Average(m =>
                deceptiveIds.Average(m.MeanTrustToward));
        }

        double meanTrustVsMitigated = double.NaN;
        if (mitigated.Count >= 2)
        {
            meanTrustVsMitigated = mitigated.Average(m =>
            {
                var peers = mitigated.Where(o => o.Id != m.Id).Select(o => o.Id).ToList();
                return peers.Average(m.MeanTrustToward);
            });
        }

        var disappointmentEchoes = world.Messages.Count(m =>
            m.Payload.StartsWith("disappointment:", StringComparison.Ordinal));

        var agg = AggregateDisrepute(mitigated, mitigatedIds, deceptiveIds);

        return new double[]
        {
            tick,
            trial,
            active,
            cleared,
            mitigated.Count,
            double.IsNaN(meanTrustVsDeceptive) ? -1 : meanTrustVsDeceptive,
            double.IsNaN(meanTrustVsMitigated) ? -1 : meanTrustVsMitigated,
            disappointmentEchoes,
            agg.DisreputeMit,
            agg.DisreputeDec,
            agg.OffHerdingMit, agg.OffSilenceMit, agg.OffSaturatedMit, agg.OffVerifyMit, agg.OffGossipMit,
            agg.OffHerdingDec, agg.OffSilenceDec, agg.OffSaturatedDec, agg.OffVerifyDec, agg.OffGossipDec,
            agg.WHerdingMit, agg.WSilenceMit, agg.WSaturatedMit, agg.WVerifyMit, agg.WGossipMit,
            agg.WHerdingDec, agg.WSilenceDec, agg.WSaturatedDec, agg.WVerifyDec, agg.WGossipDec
        };
    }

    private static Agg AggregateDisrepute(
        List<MitigatedOrbitAgent> mitigated,
        HashSet<int> mitigatedIds,
        HashSet<int> deceptiveIds)
    {
        var a = new Agg();
        foreach (var observer in mitigated)
        {
            foreach (var (targetId, bucket) in observer.DisreputeToward)
            {
                if (mitigatedIds.Contains(targetId) && targetId != observer.Id)
                {
                    a.DisreputeMit += bucket.TotalWeight;
                    a.OffHerdingMit += bucket.Herding;
                    a.OffSilenceMit += bucket.Silence;
                    a.OffSaturatedMit += bucket.Saturated;
                    a.OffVerifyMit += bucket.Verify;
                    a.OffGossipMit += bucket.Gossip;
                    a.WHerdingMit += bucket.HerdingWeight;
                    a.WSilenceMit += bucket.SilenceWeight;
                    a.WSaturatedMit += bucket.SaturatedWeight;
                    a.WVerifyMit += bucket.VerifyWeight;
                    a.WGossipMit += bucket.GossipWeight;
                }
                else if (deceptiveIds.Contains(targetId))
                {
                    a.DisreputeDec += bucket.TotalWeight;
                    a.OffHerdingDec += bucket.Herding;
                    a.OffSilenceDec += bucket.Silence;
                    a.OffSaturatedDec += bucket.Saturated;
                    a.OffVerifyDec += bucket.Verify;
                    a.OffGossipDec += bucket.Gossip;
                    a.WHerdingDec += bucket.HerdingWeight;
                    a.WSilenceDec += bucket.SilenceWeight;
                    a.WSaturatedDec += bucket.SaturatedWeight;
                    a.WVerifyDec += bucket.VerifyWeight;
                    a.WGossipDec += bucket.GossipWeight;
                }
            }
        }

        return a;
    }

    private struct Agg
    {
        public double DisreputeMit, DisreputeDec;
        public int OffHerdingMit, OffSilenceMit, OffSaturatedMit, OffVerifyMit, OffGossipMit;
        public int OffHerdingDec, OffSilenceDec, OffSaturatedDec, OffVerifyDec, OffGossipDec;
        public double WHerdingMit, WSilenceMit, WSaturatedMit, WVerifyMit, WGossipMit;
        public double WHerdingDec, WSilenceDec, WSaturatedDec, WVerifyDec, WGossipDec;
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
