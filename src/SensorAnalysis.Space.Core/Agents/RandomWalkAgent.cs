using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.Core.Agents;

/// <summary>Coasts on inertia and applies a small random Δv every <see cref="Agent.HeadingChangeInterval"/> ticks.</summary>
public sealed class RandomWalkAgent : Agent
{
    /// <summary>Creates a random-walk weaver with a modest throttle.</summary>
    public RandomWalkAgent()
    {
        AgentType = "RandomWalk";
        ObjectType = "RandomWalk";
        Visual = VisualKind.Cone;
        HeadingChangeInterval = 28;
        MaxDeltaVPerTick = 0.04;
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        if (HeadingTicksRemaining <= 0)
        {
            HeadingTicksRemaining = HeadingChangeInterval;
            var kick = Vec3.RandomUnit(Rng) * (MaxDeltaVPerTick * (0.35 + 0.65 * Rng.NextDouble()));
            return new AgentAction(kick, kick);
        }

        HeadingTicksRemaining--;
        return AgentAction.Coast;
    }
}
