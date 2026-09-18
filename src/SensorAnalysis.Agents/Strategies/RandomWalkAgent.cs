using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Correlated random walk baseline: ignores sensing, radio, peers, and events.
/// Each tick re-samples a heading that usually continues forward, sometimes
/// turns slightly, rarely turns sharply, and almost never reverses or stays.
/// </summary>
public sealed class RandomWalkAgent : Agent
{
    private int _heading;
    private bool _headingInitialized;

    /// <summary>Creates a correlated random-walk agent.</summary>
    public RandomWalkAgent()
    {
        AgentType = "RandomWalk";
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

/// <summary>Factory for <see cref="RandomWalkAgent"/> instances.</summary>
public sealed class RandomWalkAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "RandomWalk";

    /// <inheritdoc />
    public string Description =>
        "Correlated random walk: ignores cues/radio; prefers continuing forward.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new RandomWalkAgent { Label = label ?? "RW" };
}
