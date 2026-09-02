using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Lloyd-channel infiltrator: no drain, no radio, no incident oracle.
/// Parks on the nearest sensed cue so it Voronoi-owns that event (occupancy spoilage).
/// Does not read <see cref="AgentInfo.AgentType"/>. Privilege is no-drain + PlaceNear.
/// Distinct from ProgressLoiter (progress family) and CrowdOnlyDeceptive (P family).
/// </summary>
public sealed class LloydParkDeceptiveAgent : Agent
{
    /// <summary>Creates a Lloyd-park deceptive agent.</summary>
    public LloydParkDeceptiveAgent()
    {
        AgentType = "LloydParkDeceptive";
        Velocity = 5;
        ContributesToIncidentDrain = false;
    }

    /// <summary>
    /// Approaches the nearest sensed cue and stays in the disc. Ignores radio and peer types.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        if (observation.NearbyIncidents.Count == 0)
            return LazyLoiter();

        var nearest = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceTo(i.Position))
            .First();
        if (observation.DistanceTo(nearest.Position) <= SensorRange * 0.35)
            return AgentAction.Stay;
        return Steering.DirectionToward(observation.DisplacementTo(nearest.Position));
    }

    private AgentAction LazyLoiter()
    {
        if (Rng.Next(4) != 0)
            return AgentAction.Stay;
        AgentAction action;
        do
        {
            action = Steering.RandomAction(Rng);
        } while (action == AgentAction.Stay);
        return action;
    }
}

/// <summary>Factory for <see cref="LloydParkDeceptiveAgent"/>.</summary>
public sealed class LloydParkDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "LloydParkDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: no drain, no radio, no oracle; parks on a sensed cue to steal its Voronoi cell.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new LloydParkDeceptiveAgent { Label = label ?? "LP" };
}
