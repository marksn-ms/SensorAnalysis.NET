using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles infiltrator: no drain, no radio. Only tries to crowd honest peers
/// (close into their personal space). Mildly avoids other infiltrators.
/// </summary>
public sealed class CrowdOnlyDeceptiveAgent : Agent
{
    /// <summary>Creates a crowd-only deceptive agent.</summary>
    public CrowdOnlyDeceptiveAgent()
    {
        AgentType = "CrowdOnlyDeceptive";
        Velocity = 5;
        ContributesToIncidentDrain = false;
    }

    /// <summary>
    /// Closes on the nearest honest peer to crowd their personal space, with mild repulsion from other infiltrators.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        var personal = SensorRange * 0.5;

        var dogs = observation.NearbyAgents
            .Where(a => IsCrowdInfiltrator(a.AgentType))
            .ToList();
        if (dogs.Count > 0)
        {
            var nearestDog = dogs.OrderBy(a => observation.DistanceTo(a.Position)).First();
            if (observation.DistanceTo(nearestDog.Position) < personal * 0.9)
            {
                var away = observation.DisplacementBetween(nearestDog.Position, Vec2.Zero);
                return Steering.DirectionToward(away);
            }
        }

        var victims = observation.NearbyAgents
            .Where(a => !IsCrowdInfiltrator(a.AgentType))
            .ToList();
        if (victims.Count == 0)
            return LazyLoiter();

        var victim = victims.OrderBy(v => observation.DistanceTo(v.Position)).First();
        var dist = observation.DistanceTo(victim.Position);
        if (dist < personal * 0.35)
        {
            if (Rng.Next(3) == 0)
                return Steering.DirectionToward(observation.DisplacementTo(victim.Position));
            return AgentAction.Stay;
        }

        return Steering.DirectionToward(observation.DisplacementTo(victim.Position));
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

    private static bool IsCrowdInfiltrator(string agentType) =>
        agentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase) ||
        agentType.Equals("CrowdOnlyDeceptive", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Factory for <see cref="CrowdOnlyDeceptiveAgent"/>.</summary>
public sealed class CrowdOnlyDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "CrowdOnlyDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: no drain, no radio; only crowds honest peers into personal space.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new CrowdOnlyDeceptiveAgent { Label = label ?? "CD" };
}
