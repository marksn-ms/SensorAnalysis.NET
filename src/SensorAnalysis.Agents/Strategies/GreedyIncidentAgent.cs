using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Moves toward the nearest sensed incident; wanders randomly when none are visible.
/// Useful baseline for coverage / search experiments.
/// </summary>
public sealed class GreedyIncidentAgent : Agent
{
    /// <summary>Creates a greedy incident-seeking agent.</summary>
    public GreedyIncidentAgent()
    {
        AgentType = "GreedyIncident";
    }

    /// <summary>Steers toward the nearest visible incident, otherwise picks a random direction.</summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count == 0)
            return Steering.RandomActionIncludingStay(Rng);

        var nearest = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceTo(i.Position))
            .First();

        var delta = observation.DisplacementTo(nearest.Position);
        return Steering.DirectionToward(delta);
    }
}

/// <summary>Factory for <see cref="GreedyIncidentAgent"/>.</summary>
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
