using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Watchers;

/// <summary>
/// Measurement-only: unsigned pheromone mass and deposits vs a nonexistent C3 channel.
/// The field has no speaker id; there is no peer to distrust. Sample tick = -1 is
/// the end-of-trial row.
/// </summary>
public sealed class PheromoneFieldWatcher : IWatcher
{
    private readonly List<IReadOnlyList<double>> _rows = new();
    private readonly int _sampleEvery;

    /// <summary>Creates a pheromone-field watcher.</summary>
    public PheromoneFieldWatcher(int sampleEveryTicks = 5)
    {
        _sampleEvery = Math.Max(1, sampleEveryTicks);
    }

    /// <inheritdoc />
    public string Name => "PheromoneField";

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames { get; } =
        new[]
        {
            "tick", "trial",
            "skeptic_count",
            "field_mass",
            "deposits_honest", "mass_honest",
            "deposits_deceptive", "mass_deceptive",
            "last_sample_honest", "last_grad_honest",
            "offenses"
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
        var tel = world.Agents.OfType<IPheromoneTelemetry>().ToList();
        var deceptiveIds = world.Agents
            .Where(a => a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase)
                        || a.AgentType.Contains("Fake", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToHashSet();

        double depH = 0, massH = 0, depD = 0, massD = 0;
        double sampleH = 0, gradH = 0;
        int nH = 0;
        foreach (var t in tel)
        {
            if (deceptiveIds.Contains(t.Id))
            {
                depD += t.LifetimeDeposits;
                massD += t.LifetimeDepositedMass;
            }
            else
            {
                depH += t.LifetimeDeposits;
                massH += t.LifetimeDepositedMass;
                if (!double.IsNaN(t.LastSample))
                {
                    sampleH += t.LastSample;
                    nH++;
                }
                gradH += t.LastGradientLength;
            }
        }

        return new double[]
        {
            tick,
            trial,
            0, // no skeptic: unsigned field, no peer to distrust
            world.Pheromones.TotalMass,
            depH, massH,
            depD, massD,
            nH == 0 ? -1 : sampleH / nH,
            tel.Count == 0 ? 0 : gradH,
            0 // no offense channel
        };
    }

    /// <inheritdoc />
    public void Dispose() => _rows.Clear();
}
