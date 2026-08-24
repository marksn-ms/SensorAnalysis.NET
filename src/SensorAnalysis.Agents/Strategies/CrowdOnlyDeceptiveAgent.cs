using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles infiltrator: no drain, no radio. Only tries to crowd honest peers
/// (close into their personal space). Mildly avoids other infiltrators.
/// </summary>
public sealed class CrowdOnlyDeceptiveAgent : Agent
{
    private readonly Random _rng;

    /// <summary>Creates a crowd-only deceptive agent.</summary>
    public CrowdOnlyDeceptiveAgent(int? seed = null)
    {
        AgentType = "CrowdOnlyDeceptive";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = true;
        ContributesToIncidentDrain = false;
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        var personal = SensorRange * 0.5;
        var w = observation.WorldWidth;
        var h = observation.WorldHeight;

        // Mild repulsion from other infiltrators to avoid hip-lock.
        var dogs = observation.NearbyAgents
            .Where(a => IsCrowdInfiltrator(a.AgentType))
            .ToList();
        if (dogs.Count > 0)
        {
            var nearestDog = dogs.OrderBy(a => observation.DistanceTo(a.Position)).First();
            if (observation.DistanceTo(nearestDog.Position) < personal * 0.9)
            {
                var away = Vec2.ToroidalDisplacement(
                    nearestDog.Position, observation.SelfPosition, w, h);
                return Steering.DirectionToward(away);
            }
        }

        var victims = observation.NearbyAgents
            .Where(a => !IsCrowdInfiltrator(a.AgentType))
            .ToList();
        if (victims.Count == 0)
            return LazyLoiter();

        // Close on the nearest honest peer — goal is to enter their personal space.
        var victim = victims.OrderBy(v => observation.DistanceTo(v.Position)).First();
        var dist = observation.DistanceTo(victim.Position);
        if (dist < personal * 0.35)
        {
            // Already deep inside their space: drift with them / stay close.
            if (_rng.Next(3) == 0)
                return Steering.DirectionToward(observation.DisplacementTo(victim.Position));
            return AgentAction.Stay;
        }

        return Steering.DirectionToward(observation.DisplacementTo(victim.Position));
    }

    private AgentAction LazyLoiter()
    {
        if (_rng.Next(4) != 0)
            return AgentAction.Stay;
        AgentAction action;
        do
        {
            action = Steering.RandomAction(_rng);
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
