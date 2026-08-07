using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Honest squad searcher: personal-space exploration, orbit commitments at r/2,
/// bird-in-hand join, and smart-but-naive rebalancing while committed.
/// Remains responsive to stimulus after lock-on (over-cover break-off, switch to
/// under-covered sighted or reported work) so cooperation stays an attack surface.
/// Sparse radio on contact / orbit / break plus occasional status while monitoring.
/// </summary>
public class OrbitingCoopAgent : Agent
{
    private readonly Random _rng;
    /// <summary>Ticks since last incident status broadcast (for cooldown).</summary>
    protected int TicksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    /// <summary>Incident id currently being orbited (null if none).</summary>
    protected int? CommittedIncidentId;
    /// <summary>Incident position currently being orbited (last known).</summary>
    protected Vec2 CommittedPosition;
    /// <summary>Whether an orbit-confirmed radio call has been sent for the current commitment.</summary>
    protected bool OrbitAnnounced;
    /// <summary>Incident reports overheard (agent id, incident id).</summary>
    protected readonly HashSet<(int AgentId, int IncidentId)> HeardReports = new();
    /// <summary>Ticks remaining before another switch/break is allowed.</summary>
    protected int SwitchCooldownRemaining;

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

    /// <summary>Status rebroadcast cooldown while monitoring.</summary>
    public int ReportCooldown { get; set; } = 10;

    /// <summary>World-echo TTL.</summary>
    public int MessageEchoTtl { get; set; } = 40;

    /// <summary>How many nearer peers block a bird-in-hand join (default 2 ⇒ up to 3 orbiters).</summary>
    public int MaxOthersCloserToJoin { get; set; } = 2;

    /// <summary>Maximum agents that should stay on one event (default = MaxOthersCloserToJoin + 1).</summary>
    public int MaxOrbiters => MaxOthersCloserToJoin + 1;

    /// <summary>
    /// Minimum engagement advantage (others closer on current minus others closer on candidate)
    /// required to switch while committed. Default 2 ⇒ leave when current is crowded and candidate is free.
    /// </summary>
    public int SwitchAdvantage { get; set; } = 2;

    /// <summary>Ticks to wait after a switch/break before switching again (anti-thrash).</summary>
    public int SwitchCooldownTicks { get; set; } = 12;

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        var personalSpace = SensorRange * 0.5;

        if (SwitchCooldownRemaining > 0)
            SwitchCooldownRemaining--;

        IngestReports(observation);
        MaybeRebalanceOvercrowd(ctx, observation);
        MaybeSwitchFromSight(ctx, observation);
        MaybeSwitchFromReports(ctx, observation);
        MaybeCommitFromSight(ctx, observation);
        MaybeCommitFromReports(ctx, observation);
        MaybeAnnounceOrbit(ctx, observation);
        MaybeBroadcastStatus(ctx, observation);

        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;

        if (CommittedIncidentId is int id &&
            observation.NearbyIncidents.All(i => i.Id != id) &&
            !ctx.World.ActiveIncidents.Any(i => i.Id == id))
        {
            BreakOff(ctx, id);
        }
    }

    /// <summary>Record overheard claims (contact / orbit / status).</summary>
    protected virtual void IngestReports(AgentObservation observation)
    {
        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out _, out var iid, out _) ||
                msg.FromAgentId is not int sid)
                continue;
            HeardReports.Add((sid, iid));
        }
    }

    /// <summary>
    /// If more than <see cref="MaxOrbiters"/> agents are on-station, or the pack is
    /// tightly clumped, yield and resume exploration (keeps ~120° spacing intent).
    /// </summary>
    protected virtual void MaybeRebalanceOvercrowd(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int id) return;
        if (SwitchCooldownRemaining > 0) return;

        var orbitRadius = SensorRange * 0.5;
        var stationRadius = orbitRadius * 1.2;
        var center = ResolveCommittedCenter(observation);
        var onStation = CountAgentsNear(observation, center, stationRadius);
        var total = onStation + 1;

        var overcrowded = total > MaxOrbiters && ShouldYieldStation(observation, center, stationRadius);
        var tightlyClumped = false;
        if (!overcrowded && total >= 2)
        {
            var nearestPeer = observation.NearbyAgents
                .Where(a => Vec2.ToroidalDistance(
                    a.Position, center, observation.WorldWidth, observation.WorldHeight) <= stationRadius)
                .Select(a => observation.DistanceTo(a.Position))
                .DefaultIfEmpty(double.MaxValue)
                .Min();
            // Inside ~0.4 of orbit radius ⇒ stacked, not spaced at ~120°.
            tightlyClumped = nearestPeer < orbitRadius * 0.4 &&
                             ShouldYieldStation(observation, center, stationRadius);
        }

        if (!overcrowded && !tightlyClumped)
            return;

        BreakOff(ctx, id);
        SwitchCooldownRemaining = SwitchCooldownTicks;
    }

    /// <summary>
    /// While committed, switch to a visible under-covered incident when engagement
    /// advantage clearly favors leaving the current event.
    /// </summary>
    protected virtual void MaybeSwitchFromSight(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int currentId) return;
        if (SwitchCooldownRemaining > 0) return;

        var currentCenter = ResolveCommittedCenter(observation);
        var currentEngagement = CountOthersCloser(observation, currentCenter);

        IncidentInfo? best = null;
        var bestEngagement = int.MaxValue;
        foreach (var inc in observation.NearbyIncidents)
        {
            if (inc.Id == currentId) continue;
            var eng = CountOthersCloser(observation, inc.Position);
            if (eng >= bestEngagement) continue;
            bestEngagement = eng;
            best = inc;
        }

        if (best is null) return;
        if (currentEngagement - bestEngagement < SwitchAdvantage) return;

        SwitchTo(ctx, best.Value.Id, best.Value.Position, announceContact: true, reportSenderId: null);
    }

    /// <summary>
    /// Naive radio response: join when free, or switch while committed if the claim
    /// looks less engaged than the current event (opens the decoy attack surface).
    /// </summary>
    protected virtual void MaybeSwitchFromReports(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is null) return;
        if (SwitchCooldownRemaining > 0) return;

        var currentCenter = ResolveCommittedCenter(observation);
        var currentEngagement = CountOthersCloser(observation, currentCenter);

        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out var claimed))
                continue;
            if (kind == RadioClaimKind.Break) continue;
            if (CommittedIncidentId == iid) continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            var claimEngagement = CountOthersCloser(observation, claimed);
            if (currentEngagement - claimEngagement < SwitchAdvantage)
                continue;
            // Also require bird-in-hand: do not pile onto an already full claim.
            if (claimEngagement >= MaxOthersCloserToJoin)
                continue;

            SwitchTo(ctx, iid, claimed, announceContact: true, reportSenderId: msg.FromAgentId);
            return;
        }
    }

    /// <summary>Finder: commit to a visible incident when not already committed.</summary>
    protected virtual void MaybeCommitFromSight(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;
        if (observation.NearbyIncidents.Count == 0) return;

        var nearest = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceTo(i.Position))
            .First();
        Commit(ctx, nearest.Id, nearest.Position, announceContact: true);
    }

    /// <summary>Bird-in-hand: join a reported event if few others are already closer.</summary>
    protected virtual void MaybeCommitFromReports(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;

        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out var claimed))
                continue;
            // Joining on break makes no sense; contact/orbit/status are actionable.
            if (kind == RadioClaimKind.Break) continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            var othersCloser = CountOthersCloser(observation, claimed);
            if (othersCloser >= MaxOthersCloserToJoin)
                continue;

            Commit(ctx, iid, claimed, announceContact: true);
            OnFollowedReport(msg, claimed, iid);
            return;
        }
    }

    /// <summary>Honest agents trust all incident reports.</summary>
    protected virtual bool ShouldFollowReport(MessageInfo msg, Vec2 claimed) => true;

    /// <summary>Hook for mitigated agents to start a verification probe after following a report.</summary>
    protected virtual void OnFollowedReport(MessageInfo msg, Vec2 claimed, int incidentId)
    {
    }

    /// <summary>
    /// Once within ~r/2 of the commitment, announce orbit confirmation (once per commitment).
    /// </summary>
    protected virtual void MaybeAnnounceOrbit(WorldContext ctx, AgentObservation observation)
    {
        if (OrbitAnnounced || CommittedIncidentId is not int id) return;

        var orbitRadius = SensorRange * 0.5;
        var sensed = observation.NearbyIncidents.FirstOrDefault(i => i.Id == id);
        var target = sensed.Id == id ? sensed.Position : CommittedPosition;
        if (observation.DistanceTo(target) > orbitRadius * 1.15)
            return;

        if (sensed.Id == id)
            CommittedPosition = sensed.Position;

        ctx.World.Transmit(this, RadioNet.FormatOrbit(id, CommittedPosition), MessageEchoTtl);
        OrbitAnnounced = true;
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Sparse status while monitoring and the event is still visible.</summary>
    protected virtual void MaybeBroadcastStatus(WorldContext ctx, AgentObservation observation)
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
            return Steering.OrbitClockwise(
                observation.SelfPosition, target, orbitRadius,
                observation.WorldWidth, observation.WorldHeight);
        }

        return Steering.ExploreWithPersonalSpace(
            observation.SelfPosition,
            _exploreHeading,
            _explorePersist,
            observation.NearbyAgents,
            personalSpace,
            observation.WorldWidth,
            observation.WorldHeight,
            _rng,
            out _exploreHeading,
            out _explorePersist);
    }

    /// <summary>Commit to an incident and optionally radio first contact.</summary>
    protected void Commit(WorldContext ctx, int incidentId, Vec2 position, bool announceContact)
    {
        CommittedIncidentId = incidentId;
        CommittedPosition = position;
        OrbitAnnounced = false;
        TicksSinceReport = ReportCooldown; // allow prompt status after orbit if needed
        if (announceContact)
        {
            ctx.World.Transmit(this, RadioNet.FormatContact(incidentId, position), MessageEchoTtl);
            HeardReports.Add((Id, incidentId));
            TicksSinceReport = 0;
        }
    }

    /// <summary>Break current commitment (if any) and commit to a new claim.</summary>
    protected void SwitchTo(
        WorldContext ctx,
        int incidentId,
        Vec2 position,
        bool announceContact,
        int? reportSenderId)
    {
        if (CommittedIncidentId is int oldId && oldId != incidentId)
            BreakOff(ctx, oldId);

        Commit(ctx, incidentId, position, announceContact);
        SwitchCooldownRemaining = SwitchCooldownTicks;

        if (reportSenderId is int sid)
        {
            // Synthesize a message-shaped follow so mitigated can start a probe.
            var synthetic = new MessageInfo(
                Id: -1,
                Position: position,
                FromType: string.Empty,
                FromAgentId: sid,
                ToAgentId: null,
                Payload: RadioNet.FormatContact(incidentId, position),
                TtlRemaining: MessageEchoTtl);
            OnFollowedReport(synthetic, position, incidentId);
        }
    }

    /// <summary>Clear commitment and announce break.</summary>
    protected void BreakOff(WorldContext ctx, int incidentId)
    {
        CommittedIncidentId = null;
        OrbitAnnounced = false;
        ctx.World.Transmit(this, RadioNet.FormatBreak(incidentId), MessageEchoTtl);
    }

    /// <summary>Last known / sensed center of the current commitment.</summary>
    protected Vec2 ResolveCommittedCenter(AgentObservation observation)
    {
        if (CommittedIncidentId is int id)
        {
            var sensed = observation.NearbyIncidents.FirstOrDefault(i => i.Id == id);
            if (sensed.Id == id)
                return sensed.Position;
        }

        return CommittedPosition;
    }

    /// <summary>Peers closer to <paramref name="target"/> than self (bird-in-hand engagement).</summary>
    protected static int CountOthersCloser(AgentObservation observation, Vec2 target)
    {
        var selfDist = observation.DistanceTo(target);
        return observation.NearbyAgents.Count(a =>
            Vec2.ToroidalDistance(a.Position, target, observation.WorldWidth, observation.WorldHeight) + 1e-6 <
            selfDist);
    }

    /// <summary>Peers within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    protected static int CountAgentsNear(AgentObservation observation, Vec2 center, double radius) =>
        observation.NearbyAgents.Count(a =>
            Vec2.ToroidalDistance(a.Position, center, observation.WorldWidth, observation.WorldHeight) <= radius);

    /// <summary>
    /// True when self is not among the <see cref="MaxOrbiters"/> closest agents to the event
    /// (so we should yield the station).
    /// </summary>
    protected bool ShouldYieldStation(AgentObservation observation, Vec2 center, double stationRadius)
    {
        var selfDist = observation.DistanceTo(center);
        var closerOrEqualPeers = observation.NearbyAgents.Count(a =>
        {
            var d = Vec2.ToroidalDistance(a.Position, center, observation.WorldWidth, observation.WorldHeight);
            return d <= stationRadius && d <= selfDist + 1e-6;
        });
        // Peers closer-or-equal + self; if more than MaxOrbiters would remain with us, yield.
        return closerOrEqualPeers + 1 > MaxOrbiters;
    }
}

/// <summary>Factory for <see cref="OrbitingCoopAgent"/>.</summary>
public sealed class OrbitingCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "OrbitingCoop";

    /// <inheritdoc />
    public string Description =>
        "Honest orbit/personal-space search with bird-in-hand joins, rebalance, and naive switch-on-stimulus.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new OrbitingCoopAgent { Label = label ?? "OC" };
}
