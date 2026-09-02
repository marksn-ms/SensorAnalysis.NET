namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Measurement-only view of unsigned pheromone deposit/sense. No speaker id on the field.
/// </summary>
public interface IPheromoneTelemetry
{
    /// <summary>Agent id.</summary>
    int Id { get; }

    /// <summary>Lifetime deposits this agent made (unsigned; the field does not record who).</summary>
    int LifetimeDeposits { get; }

    /// <summary>Mass this agent added over the trial.</summary>
    double LifetimeDepositedMass { get; }

    /// <summary>Last sampled field value at the agent (NaN if never).</summary>
    double LastSample { get; }

    /// <summary>Length of the last local gradient used for steering.</summary>
    double LastGradientLength { get; }
}
