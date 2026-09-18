using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Correlated random walk that never drains incidents and never orbits.
/// Same motion as <see cref="RandomWalkAgent"/>; the world skips this type when
/// applying incident drain. Used as a presence-only control against infiltrators.
/// </summary>
public sealed class RandomWalkNoDrainAgent : Agent
{
    private int _heading;
    private bool _headingInitialized;

    /// <summary>Creates a non-draining correlated random-walk agent.</summary>
    public RandomWalkNoDrainAgent()
    {
        AgentType = "RandomWalkNoDrain";
        ContributesToIncidentDrain = false;
    }

    /// <summary>
    /// Ignores observations and performs a correlated random walk on the compass ring.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        _ = observation;
        return Steering.CorrelatedWalk(Rng, ref _heading, ref _headingInitialized);
    }
}

/// <summary>Factory for <see cref="RandomWalkNoDrainAgent"/> instances.</summary>
public sealed class RandomWalkNoDrainAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "RandomWalkNoDrain";

    /// <inheritdoc />
    public string Description =>
        "Correlated random walk that does not drain incidents (presence-only control).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new RandomWalkNoDrainAgent { Label = label ?? "RWND" };
}
