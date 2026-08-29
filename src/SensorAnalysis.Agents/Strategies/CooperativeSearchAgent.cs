using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Honest cooperative searcher: exploit visible incidents, broadcast claimed locations via
/// radio, follow peer reports, otherwise explore with directional persistence.
/// </summary>
public sealed class CooperativeSearchAgent : Agent
{
    private int _ticksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _exploreTicksRemaining;

    /// <summary>Creates a cooperative search agent.</summary>
    public CooperativeSearchAgent()
    {
        AgentType = "CooperativeSearch";
        Velocity = 5;
        ActionIntervalBase = 12;
    }

    /// <summary>
    /// Steers toward the nearest sensed incident, else the nearest peer report, else a persistent random heading.
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
            .Where(m => IncidentMarker.TryParse(m.Payload, out _, out _))
            .Select(m =>
            {
                IncidentMarker.TryParse(m.Payload, out _, out var claimedAbsolute);
                return (Msg: m, ClaimedRelative: observation.RelativeFromAbsolute(claimedAbsolute));
            })
            .OrderBy(x => observation.DistanceTo(x.ClaimedRelative))
            .ToList();

        if (reports.Count > 0)
        {
            var best = reports[0];
            return Steering.DirectionToward(observation.DisplacementTo(best.ClaimedRelative));
        }

        return NextExploreAction();
    }

    /// <summary>
    /// Observes, transmits incident claims on a desynchronized interval, decides, and moves.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);

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
        else
        {
            _ticksSinceReport++;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    private AgentAction NextExploreAction()
    {
        if (_exploreTicksRemaining <= 0 || _exploreHeading == AgentAction.Stay)
        {
            _exploreHeading = Steering.RandomAction(Rng);
            _exploreTicksRemaining = HeadingChangeInterval;
        }

        _exploreTicksRemaining--;
        return _exploreHeading;
    }
}

/// <summary>Factory for <see cref="CooperativeSearchAgent"/>.</summary>
public sealed class CooperativeSearchAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "CooperativeSearch";

    /// <inheritdoc />
    public string Description =>
        "Honest searcher: broadcasts incident claims, follows peer reports, explores with persistence.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new CooperativeSearchAgent { Label = label ?? "CS" };
}
