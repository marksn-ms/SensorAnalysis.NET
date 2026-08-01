using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Samples clearance progress plus mean mitigated-agent trust toward deceptive-typed peers,
/// supporting "root out then recover" narratives over time.
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
            "mitigated_count", "mean_trust_vs_deceptive", "disappointment_echoes"
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

        var initial = _initial.GetValueOrDefault(trial, world.Incidents.Count);
        var active = world.Incidents.Count(i => !i.IsDepleted);
        var cleared = initial == 0 ? 1.0 : (double)(initial - active) / initial;

        var mitigated = world.Agents.OfType<MitigatedOrbitAgent>().ToList();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToList();

        double meanTrust = double.NaN;
        if (mitigated.Count > 0 && deceptiveIds.Count > 0)
        {
            meanTrust = mitigated.Average(m =>
                deceptiveIds.Average(m.MeanTrustToward));
        }

        var disappointmentEchoes = world.Messages.Count(m =>
            m.Payload.StartsWith("disappointment:", StringComparison.Ordinal));

        _rows.Add(new double[]
        {
            tick,
            trial,
            active,
            cleared,
            mitigated.Count,
            double.IsNaN(meanTrust) ? -1 : meanTrust,
            disappointmentEchoes
        });
    }

    /// <inheritdoc />
    public void OnTrialEnd(SimWorld world, int trial) { }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
