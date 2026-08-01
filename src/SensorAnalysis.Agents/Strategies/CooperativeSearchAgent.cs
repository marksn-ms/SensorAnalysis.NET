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
    private readonly Random _rng;
    private int _ticksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _exploreTicksRemaining;

    /// <summary>Creates a cooperative search agent.</summary>
    /// <param name="seed">Optional RNG seed for deterministic exploration.</param>
    public CooperativeSearchAgent(int? seed = null)
    {
        AgentType = "CooperativeSearch";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = true;
    }

    /// <summary>Minimum ticks between incident report transmissions.</summary>
    public int ReportCooldown { get; set; } = 12;

    /// <summary>TTL for the optional world echo of a transmission (UI/metrics).</summary>
    public int MessageEchoTtl { get; set; } = 50;

    /// <summary>How long to persist an exploration heading before resampling.</summary>
    public int ExplorePersistTicks { get; set; } = 8;

    /// <inheritdoc />
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
                IncidentMarker.TryParse(m.Payload, out _, out var claimed);
                return (Msg: m, Claimed: claimed);
            })
            .OrderBy(x => observation.DistanceTo(x.Claimed))
            .ToList();

        if (reports.Count > 0)
        {
            var best = reports[0];
            return Steering.DirectionToward(observation.DisplacementTo(best.Claimed));
        }

        return NextExploreAction();
    }

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);

        if (observation.NearbyIncidents.Count > 0 && _ticksSinceReport >= ReportCooldown)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(nearest.Id, nearest.Position),
                ttl: MessageEchoTtl,
                toAgentId: null);
            _ticksSinceReport = 0;
        }
        else
        {
            _ticksSinceReport++;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    private AgentAction NextExploreAction()
    {
        if (_exploreTicksRemaining <= 0 || _exploreHeading == AgentAction.Stay)
        {
            _exploreHeading = Steering.RandomAction(_rng);
            _exploreTicksRemaining = ExplorePersistTicks;
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
