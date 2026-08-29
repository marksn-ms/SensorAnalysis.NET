using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Legacy-style cooperative agent that broadcasts incident claims and follows peer reports.
/// Prefer <see cref="CooperativeSearchAgent"/> for new trust experiments.
/// </summary>
public sealed class StigmergyAgent : Agent
{
    private int _ticksSinceReport;

    /// <summary>Creates an agent that transmits and follows incident report messages.</summary>
    public StigmergyAgent()
    {
        AgentType = "Stigmergy";
        Velocity = 5;
        ActionIntervalBase = 15;
        MessageEchoTtl = 40;
    }

    /// <summary>
    /// Steers toward the nearest sensed incident, else the nearest peer report, else a random heading.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            return Steering.DirectionToward(observation.DisplacementTo(nearest.Position));
        }

        var reports = observation.NearbyMessages
            .Select(m =>
            {
                var ok = IncidentMarker.TryParse(m.Payload, out _, out var claimedAbsolute);
                var claimed = observation.RelativeFromAbsolute(claimedAbsolute);
                return (Ok: ok, Claimed: claimed);
            })
            .Where(x => x.Ok)
            .OrderBy(x => observation.DistanceTo(x.Claimed))
            .ToList();

        if (reports.Count > 0)
            return Steering.DirectionToward(observation.DisplacementTo(reports[0].Claimed));

        return Steering.RandomAction(Rng);
    }

    /// <summary>
    /// Observes, transmits incident claims on a desynchronized interval, decides, and moves.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        _ticksSinceReport++;

        if (observation.NearbyIncidents.Count > 0 && _ticksSinceReport >= ActionInterval)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(nearest.Id, observation.AbsoluteFromRelative(nearest.Position)),
                ttl: MessageEchoTtl,
                toAgentId: null);
            _ticksSinceReport = 0;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }
}

/// <summary>Factory for <see cref="StigmergyAgent"/>.</summary>
public sealed class StigmergyAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "Stigmergy";

    /// <inheritdoc />
    public string Description => "Broadcasts incident claims; follows peer reports (legacy name).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new StigmergyAgent { Label = label ?? "ST" };
}
