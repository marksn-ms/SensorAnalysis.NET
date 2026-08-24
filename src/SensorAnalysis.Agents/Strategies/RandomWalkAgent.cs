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
    // Compass ring matching AgentAction: N, NE, E, SE, S, SW, W, NW.
    private static readonly AgentAction[] Compass =
    [
        AgentAction.North,
        AgentAction.NorthEast,
        AgentAction.East,
        AgentAction.SouthEast,
        AgentAction.South,
        AgentAction.SouthWest,
        AgentAction.West,
        AgentAction.NorthWest
    ];

    private readonly Random _rng;
    private int _heading; // index into Compass

    /// <summary>
    /// Creates a correlated random-walk agent.
    /// </summary>
    /// <param name="seed">Optional RNG seed for deterministic walks; otherwise non-deterministic.</param>
    public RandomWalkAgent(int? seed = null)
    {
        AgentType = "RandomWalk";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _heading = _rng.Next(Compass.Length);
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        // Intentionally ignore observation — this is the non-sensing floor.
        _ = observation;

        var roll = _rng.NextDouble();
        // Cumulative masses (must sum to 1):
        // forward 0.62, ±45° 0.15×2, ±90° 0.035×2, ±135° 0.004×2, reverse 0.002, stay 0.004
        int delta;
        if (roll < 0.62)
            delta = 0;
        else if (roll < 0.77)
            delta = 1;
        else if (roll < 0.92)
            delta = -1;
        else if (roll < 0.955)
            delta = 2;
        else if (roll < 0.99)
            delta = -2;
        else if (roll < 0.994)
            delta = 3;
        else if (roll < 0.998)
            delta = -3;
        else if (roll < 0.999)
            delta = 4; // reverse
        else
            return AgentAction.Stay;

        _heading = ((_heading + delta) % Compass.Length + Compass.Length) % Compass.Length;
        return Compass[_heading];
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
