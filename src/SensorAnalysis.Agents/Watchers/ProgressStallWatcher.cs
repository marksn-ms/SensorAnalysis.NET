using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Measurement-only: co-presence at a station vs progress-offense fire for
/// <see cref="IProgressStallTelemetry"/> skeptics (ProgressAwareMitigated).
/// Proves whether C3, if any, came from local-progress evidence.
/// Does not change agent Decide. Sample tick = -1 is the end-of-trial row.
/// </summary>
public sealed class ProgressStallWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a progress-stall watcher.</summary>
    public ProgressStallWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "ProgressStall";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial",
            "skeptic_count", "grace_ticks",
            "mean_trust_vs_deceptive", "mean_trust_vs_squad",
            "copresent_ticks_dec", "copresent_ticks_squad",
            "peers_on_station_now_dec", "peers_on_station_now_squad",
            "progress_offenses_dec", "progress_offenses_squad",
            "offense_windows", "last_intensity"
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
        var skeptics = world.Agents.OfType<IProgressStallTelemetry>().ToList();
        var skepticIds = skeptics.Select(s => s.Id).ToHashSet();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase)
                        || a.AgentType.Contains("Loiter", StringComparison.OrdinalIgnoreCase))
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

        double copresentDec = 0, copresentSquad = 0;
        double onNowDec = 0, onNowSquad = 0;
        double offDec = 0, offSquad = 0;
        double windows = 0;
        double intensitySum = 0;
        int intensityN = 0;
        int grace = skeptics.Count > 0 ? skeptics[0].ProgressGraceTicks : 0;

        foreach (var s in skeptics)
        {
            windows += s.LifetimeProgressOffenseWindows;
            if (!double.IsNaN(s.LastSampledIntensity))
            {
                intensitySum += s.LastSampledIntensity;
                intensityN++;
            }
            foreach (var (id, ticks) in s.LifetimeCopresentTicks)
            {
                if (deceptiveIds.Contains(id)) copresentDec += ticks;
                else if (skepticIds.Contains(id) && id != s.Id) copresentSquad += ticks;
            }
            foreach (var (id, flag) in s.CurrentlyOnStation)
            {
                if (flag == 0) continue;
                if (deceptiveIds.Contains(id)) onNowDec += 1;
                else if (skepticIds.Contains(id) && id != s.Id) onNowSquad += 1;
            }
            foreach (var id in deceptiveIds)
                offDec += s.ProgressOffenseCountToward(id);
            foreach (var id in skepticIds)
            {
                if (id == s.Id) continue;
                offSquad += s.ProgressOffenseCountToward(id);
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
            copresentDec, copresentSquad,
            onNowDec, onNowSquad,
            offDec, offSquad,
            windows,
            intensityN == 0 ? -1 : intensitySum / intensityN
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
