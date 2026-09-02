using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Local-progress infiltrator: no drain, no radio, no incident oracle.
/// Loiters in the sensed event disc (sit once close). Does not read
/// <see cref="AgentInfo.AgentType"/> or <c>ContributesToIncidentDrain</c> of peers
/// (the latter is not on the observation). Privilege is no-drain + PlaceNear.
/// </summary>
public sealed class ProgressLoiterDeceptiveAgent : Agent
{
    /// <summary>Creates a progress-loiter deceptive agent.</summary>
    public ProgressLoiterDeceptiveAgent()
    {
        AgentType = "ProgressLoiterDeceptive";
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

/// <summary>Factory for <see cref="ProgressLoiterDeceptiveAgent"/>.</summary>
public sealed class ProgressLoiterDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "ProgressLoiterDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: no drain, no radio, no oracle; loiters in the sensed event disc.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new ProgressLoiterDeceptiveAgent { Label = label ?? "PL" };
}
