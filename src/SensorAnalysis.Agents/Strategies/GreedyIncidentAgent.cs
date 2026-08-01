using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Moves toward the nearest sensed incident; wanders randomly when none are visible.
/// Useful baseline for coverage / search experiments.
/// </summary>
public sealed class GreedyIncidentAgent : Agent
{
    private readonly Random _rng;

    /// <summary>
    /// Creates a greedy incident-seeking agent.
    /// </summary>
    /// <param name="seed">Optional RNG seed for deterministic wandering; otherwise non-deterministic.</param>
    public GreedyIncidentAgent(int? seed = null)
    {
        AgentType = "GreedyIncident";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count == 0)
        {
            var values = Enum.GetValues<AgentAction>();
            return values[_rng.Next(values.Length)];
        }

        var nearest = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceTo(i.Position))
            .First();

        var delta = observation.DisplacementTo(nearest.Position);
        return DirectionToward(delta);
    }

    private static AgentAction DirectionToward(Vec2 delta)
    {
        // Quantize to 8-connected grid
        var ax = Math.Abs(delta.X);
        var ay = Math.Abs(delta.Y);
        if (ax < 1e-6 && ay < 1e-6) return AgentAction.Stay;

        var sx = Math.Sign(delta.X);
        var sy = Math.Sign(delta.Y);

        if (ax > ay * 2) return sx > 0 ? AgentAction.East : AgentAction.West;
        if (ay > ax * 2) return sy > 0 ? AgentAction.South : AgentAction.North;

        if (sx > 0 && sy > 0) return AgentAction.SouthEast;
        if (sx > 0 && sy < 0) return AgentAction.NorthEast;
        if (sx < 0 && sy > 0) return AgentAction.SouthWest;
        if (sx < 0 && sy < 0) return AgentAction.NorthWest;
        return AgentAction.Stay;
    }
}

/// <summary>Factory for <see cref="GreedyIncidentAgent"/> instances.</summary>
public sealed class GreedyIncidentAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "GreedyIncident";

    /// <inheritdoc />
    public string Description => "Moves toward the nearest visible incident.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new GreedyIncidentAgent { Label = label ?? "GI" };
}
