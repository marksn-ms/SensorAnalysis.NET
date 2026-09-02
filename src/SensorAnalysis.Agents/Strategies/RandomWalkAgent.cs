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
    // Sixteen-point compass ring in clockwise order starting at north.
    private static readonly AgentAction[] Compass = AgentActionExtensions.MovementDirections;

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

        if (!_headingInitialized)
        {
            _heading = Rng.Next(Compass.Length);
            _headingInitialized = true;
        }

        var roll = Rng.NextDouble();
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
            delta = Compass.Length / 2; // reverse
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
