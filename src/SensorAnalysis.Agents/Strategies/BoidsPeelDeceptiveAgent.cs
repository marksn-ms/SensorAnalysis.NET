using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Peel-adversary for the boids channel: actively splits/misaligns a local flock,
/// not a parker. No drain, no radio, no incident oracle, no <see cref="AgentInfo.AgentType"/>.
/// Privilege is no-drain + PlaceNear. Distinct from ProgressLoiter and CrowdOnlyDeceptive.
/// </summary>
/// <remarks>
/// Approach the nearest neighbor, then dart perpendicular to that neighbor's heading
/// for <see cref="PeelDurationTicks"/> (default 8 = HeadingChangeInterval). Cycle.
/// Does not Stay-park on cues. Peel duration is a world-timescale constant, not a ρ knob.
/// </remarks>
public sealed class BoidsPeelDeceptiveAgent : Agent, IBoidsPeelAttackTelemetry
{
    private int _peelLeft;
    private AgentAction _peelHeading = AgentAction.Stay;

    /// <summary>Creates a boids peel-adversary.</summary>
    public BoidsPeelDeceptiveAgent()
    {
        AgentType = "BoidsPeelDeceptive";
        Velocity = 5;
        ContributesToIncidentDrain = false;
    }

    /// <summary>
    /// Ticks of the dart-away phase. Default 8 = platform <c>HeadingChangeInterval</c>.
    /// Not fit to ρ.
    /// </summary>
    public int PeelDurationTicks { get; set; } = 8;

    /// <summary>Close enough to a neighbor to start a peel (r/3).</summary>
    public double EngageRadiusFactor { get; set; } = 1.0 / 3.0;

    /// <inheritdoc />
    public int IsPeeling => _peelLeft > 0 ? 1 : 0;

    /// <inheritdoc />
    public int LifetimePeelTicks { get; private set; }

    /// <summary>
    /// Approach nearest neighbor, then peel perpendicular to their heading. Ignores radio,
    /// AgentType, and incident oracles. Does not park.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        if (_peelLeft > 0)
        {
            _peelLeft--;
            LifetimePeelTicks++;
            return _peelHeading == AgentAction.Stay ? AgentAction.East : _peelHeading;
        }

        if (observation.NearbyAgents.Count == 0)
            return Steering.RandomAction(Rng);

        var nearest = observation.NearbyAgents
            .OrderBy(a => observation.DistanceTo(a.Position))
            .First();
        var dist = observation.DistanceTo(nearest.Position);
        if (dist <= SensorRange * EngageRadiusFactor)
        {
            var step = nearest.Heading.ToStep();
            // Perpendicular to neighbor heading (rotate 90°).
            var perp = new Vec2(-step.Y, step.X);
            if (perp.Length < 1e-6)
                perp = new Vec2(step.Y, -step.X);
            if (perp.Length < 1e-6)
                perp = observation.DisplacementBetween(nearest.Position, Vec2.Zero);
            _peelHeading = Steering.DirectionToward(perp);
            if (_peelHeading == AgentAction.Stay)
                _peelHeading = AgentAction.East;
            _peelLeft = Math.Max(1, PeelDurationTicks) - 1;
            LifetimePeelTicks++;
            return _peelHeading;
        }

        return Steering.DirectionToward(observation.DisplacementTo(nearest.Position));
    }
}

/// <summary>Factory for <see cref="BoidsPeelDeceptiveAgent"/>.</summary>
public sealed class BoidsPeelDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "BoidsPeelDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: no drain, no radio, no oracle; peels the nearest neighbor perpendicular to their heading.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new BoidsPeelDeceptiveAgent { Label = label ?? "BP" };
}
