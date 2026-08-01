using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Honest squad searcher: personal-space exploration, orbit commitments at r/2,
/// bird-in-hand join (locally: commit if fewer than two others are already closer),
/// and truthful incident broadcasts. Does not use reputation.
/// </summary>
public class OrbitingCoopAgent : Agent
{
    private readonly Random _rng;
    protected int TicksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    protected int? CommittedIncidentId;
    protected Vec2 CommittedPosition;
    protected readonly HashSet<(int AgentId, int IncidentId)> HeardReports = new();

    /// <summary>Creates an orbiting cooperative agent.</summary>
    public OrbitingCoopAgent(int? seed = null)
    {
        AgentType = "OrbitingCoop";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = true;
        ContributesToIncidentDrain = true;
    }

    /// <summary>Protected RNG for subclasses.</summary>
    protected Random Rng => _rng;

    /// <summary>Report transmit cooldown.</summary>
    public int ReportCooldown { get; set; } = 10;

    /// <summary>World-echo TTL.</summary>
    public int MessageEchoTtl { get; set; } = 40;

    /// <summary>How many nearer peers block a bird-in-hand join (default 2 ⇒ up to 3 orbiters).</summary>
    public int MaxOthersCloserToJoin { get; set; } = 2;

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        var personalSpace = SensorRange * 0.5;

        IngestReports(observation);
        MaybeCommitFromSight(observation);
        MaybeCommitFromReports(observation);
        MaybeBroadcast(ctx, observation);

        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Clamp(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;

        if (CommittedIncidentId is int id &&
            observation.NearbyIncidents.All(i => i.Id != id) &&
            !ctx.World.ActiveIncidents.Any(i => i.Id == id))
        {
            CommittedIncidentId = null;
        }
    }

    /// <summary>Record overheard incident reports (who claimed which id).</summary>
    protected virtual void IngestReports(AgentObservation observation)
    {
        foreach (var msg in observation.NearbyMessages)
        {
            if (!IncidentMarker.TryParse(msg.Payload, out var iid, out _) || msg.FromAgentId is not int sid)
                continue;
            HeardReports.Add((sid, iid));
        }
    }

    /// <summary>Commit to a visible incident (finder behavior).</summary>
    protected virtual void MaybeCommitFromSight(AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;
        if (observation.NearbyIncidents.Count == 0) return;

        var nearest = observation.NearbyIncidents
            .OrderBy(i => Vec2.Distance(observation.SelfPosition, i.Position))
            .First();
        Commit(nearest.Id, nearest.Position);
    }

    /// <summary>Bird-in-hand: join a reported event if few others are already closer.</summary>
    protected virtual void MaybeCommitFromReports(AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;

        foreach (var msg in observation.NearbyMessages)
        {
            if (!IncidentMarker.TryParse(msg.Payload, out var iid, out var claimed))
                continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            var othersCloser = observation.NearbyAgents.Count(a =>
                Vec2.Distance(a.Position, claimed) + 1e-6 <
                Vec2.Distance(observation.SelfPosition, claimed));
            if (othersCloser >= MaxOthersCloserToJoin)
                continue;

            Commit(iid, claimed);
            return;
        }
    }

    /// <summary>Honest agents trust all incident reports.</summary>
    protected virtual bool ShouldFollowReport(MessageInfo msg, Vec2 claimed) => true;

    /// <summary>Broadcast while committed and the event is still visible.</summary>
    protected virtual void MaybeBroadcast(WorldContext ctx, AgentObservation observation)
    {
        TicksSinceReport++;
        if (CommittedIncidentId is not int id || TicksSinceReport < ReportCooldown)
            return;

        var sensed = observation.NearbyIncidents.Where(i => i.Id == id).ToList();
        if (sensed.Count == 0)
            return;

        var pos = sensed[0].Position;
        CommittedPosition = pos;
        ctx.World.Transmit(this, IncidentMarker.Format(id, pos), MessageEchoTtl);
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Steer: orbit if committed, else personal-space explore.</summary>
    protected virtual AgentAction ChooseAction(AgentObservation observation, double personalSpace)
    {
        if (CommittedIncidentId is not null)
        {
            var orbitRadius = SensorRange * 0.5;
            var center = observation.NearbyIncidents
                .FirstOrDefault(i => i.Id == CommittedIncidentId.Value);
            var target = center.Id == CommittedIncidentId.Value ? center.Position : CommittedPosition;
            return Steering.OrbitClockwise(observation.SelfPosition, target, orbitRadius);
        }

        return Steering.ExploreWithPersonalSpace(
            observation.SelfPosition,
            _exploreHeading,
            _explorePersist,
            observation.NearbyAgents,
            personalSpace,
            _rng,
            out _exploreHeading,
            out _explorePersist);
    }

    /// <summary>Commit to orbiting an incident claim.</summary>
    protected void Commit(int incidentId, Vec2 position)
    {
        CommittedIncidentId = incidentId;
        CommittedPosition = position;
    }
}

/// <summary>Factory for <see cref="OrbitingCoopAgent"/>.</summary>
public sealed class OrbitingCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "OrbitingCoop";

    /// <inheritdoc />
    public string Description =>
        "Honest orbit/personal-space search with bird-in-hand joins and truthful broadcasts.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new OrbitingCoopAgent { Label = label ?? "OC" };
}
