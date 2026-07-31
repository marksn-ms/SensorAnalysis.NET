using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>Chooses a random direction each tick (baseline strategy).</summary>
public sealed class RandomWalkAgent : Agent
{
    private readonly Random _rng;

    /// <summary>
    /// Creates a random-walk agent.
    /// </summary>
    /// <param name="seed">Optional RNG seed for deterministic walks; otherwise non-deterministic.</param>
    public RandomWalkAgent(int? seed = null)
    {
        AgentType = "RandomWalk";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        var values = Enum.GetValues<AgentAction>();
        return values[_rng.Next(values.Length)];
    }
}

/// <summary>Factory for <see cref="RandomWalkAgent"/> instances.</summary>
public sealed class RandomWalkAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "RandomWalk";

    /// <inheritdoc />
    public string Description => "Moves in a random direction each tick.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new RandomWalkAgent { Label = label ?? "RW" };
}
