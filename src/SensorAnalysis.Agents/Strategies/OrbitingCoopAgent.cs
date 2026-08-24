using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Sensing;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Honest squad searcher: personal-space exploration, orbit commitments at r/2,
/// bird-in-hand join, and smart-but-naive rebalancing while committed.
/// Tracks a list of <see cref="EventHypothesis"/> entries (sensor cues and radio rumors).
/// Missing a ping for a tick does not clear a belief; unconfirmed rumors decay back to search.
/// </summary>
public class OrbitingCoopAgent : Agent
{
    private readonly Random _rng;
    /// <summary>Ticks since last incident status broadcast (for cooldown).</summary>
    protected int TicksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    /// <summary>Local hypothesis id currently being orbited (null if none). Not a world incident id.</summary>
    protected int? CommittedIncidentId;
    /// <summary>Incident position currently being orbited (last estimate).</summary>
    protected Vec2 CommittedPosition;
    /// <summary>Whether an orbit-confirmed radio call has been sent for the current commitment.</summary>
    protected bool OrbitAnnounced;
    /// <summary>Incident reports overheard (agent id, claimed id) — legacy pairing for subclasses.</summary>
    protected readonly HashSet<(int AgentId, int IncidentId)> HeardReports = new();
    /// <summary>Claimed locations overheard this commitment (spatial; ids are untrusted).</summary>
    protected readonly List<(int AgentId, Vec2 Position)> HeardClaims = new();
    /// <summary>Ticks remaining before another switch/break is allowed.</summary>
    protected int SwitchCooldownRemaining;
    /// <summary>Suspected events (cues + rumors) this agent is tracking.</summary>
    protected readonly EventHypothesisMemory Hypotheses = new();

    /// <summary>Read-only view of the hypothesis list (tests / UI).</summary>
    public IReadOnlyList<EventHypothesis> TrackedEvents => Hypotheses.Items;

    /// <summary>Local track currently being pursued, if any.</summary>
    public int? CommittedTrackId => CommittedIncidentId;

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
        Hypotheses.AssociationRadius = Math.Max(12, SensorRange * 0.35);

        OnBeforeSearch(ctx, observation);

        if (SwitchCooldownRemaining > 0)
            SwitchCooldownRemaining--;

        Hypotheses.TickAge();
        IngestCues(observation);
        IngestRumors(ctx, observation);
        Hypotheses.DecayAndPrune();

        MaybeRebalanceOvercrowd(ctx, observation);
        MaybeRetarget(ctx, observation);
        MaybeCommitBest(ctx, observation);
        MaybeDropStaleCommitment(ctx);

        MaybeAnnounceOrbit(ctx, observation);
        MaybeBroadcastStatus(ctx, observation);

        OnAfterSearch(ctx, observation);

        var personalSpace = SensorRange * 0.5;
        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    /// <summary>Hook before hypothesis update (mitigated: gossip, probes, herding).</summary>
    protected virtual void OnBeforeSearch(WorldContext ctx, AgentObservation observation)
    {
    }

    /// <summary>Hook after radio/status, before motion (mitigated: silence).</summary>
    protected virtual void OnAfterSearch(WorldContext ctx, AgentObservation observation)
    {
    }

    /// <summary>Fold this tick's sensor cues into the hypothesis list.</summary>
    protected void IngestCues(AgentObservation observation)
    {
        var w = observation.WorldWidth;
        var h = observation.WorldHeight;
        foreach (var cue in observation.NearbyIncidents)
        {
            var strength = cue.Strength > 0 ? cue.Strength : 0.5;
            Hypotheses.IngestCue(cue.Position, strength, w, h);
        }
    }

    /// <summary>Treat radio claims as rumors (same memory as sensing).</summary>
    protected virtual void IngestRumors(WorldContext ctx, AgentObservation observation)
    {
        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out var claimed))
                continue;
            if (kind == RadioClaimKind.Break)
                continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            if (msg.FromAgentId is int sid)
            {
                HeardReports.Add((sid, iid));
                HeardClaims.Add((sid, claimed));
            }

            var othersCloser = CountOthersCloser(observation, claimed);
            if (othersCloser >= MaxOthersCloserToJoin)
            {
                OnSaturatedRumor(msg, claimed);
                continue;
            }

            Hypotheses.IngestRumor(
                claimed,
                msg.FromAgentId,
                observation.WorldWidth,
                observation.WorldHeight,
                RumorTrustWeight(msg));
        }
    }

    /// <summary>Trust scaling for a rumor (honest = 1).</summary>
    protected virtual double RumorTrustWeight(MessageInfo msg) => 1;

    /// <summary>Saturated pile-on claim (mitigated marks an offense).</summary>
    protected virtual void OnSaturatedRumor(MessageInfo msg, Vec2 claimed)
    {
    }

    /// <summary>
    /// If more than <see cref="MaxOrbiters"/> agents are on-station, or the pack is
    /// tightly clumped, yield and resume exploration (keeps ~120° spacing intent).
    /// </summary>
    protected virtual void MaybeRebalanceOvercrowd(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int id) return;
        if (SwitchCooldownRemaining > 0) return;
        if (Hypotheses.Get(id) is null) return;

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
            tightlyClumped = nearestPeer < orbitRadius * 0.4 &&
                             ShouldYieldStation(observation, center, stationRadius);
        }

        if (!overcrowded && !tightlyClumped)
            return;

        BreakOff(ctx, id);
        SwitchCooldownRemaining = SwitchCooldownTicks;
    }

    /// <summary>While committed, switch to a less-engaged hypothesis when advantage is clear.</summary>
    protected virtual void MaybeRetarget(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int currentId) return;
        if (SwitchCooldownRemaining > 0) return;
        var current = Hypotheses.Get(currentId);
        if (current is null) return;

        var currentEngagement = CountOthersCloser(observation, current.Estimate);

        EventHypothesis? best = null;
        var bestEngagement = int.MaxValue;
        foreach (var h in Hypotheses.Items)
        {
            if (h.LocalId == currentId) continue;
            if (h.Confidence < Hypotheses.SuspectThreshold) continue;
            var eng = CountOthersCloser(observation, h.Estimate);
            if (eng >= MaxOthersCloserToJoin) continue;
            if (eng >= bestEngagement) continue;
            bestEngagement = eng;
            best = h;
        }

        if (best is null) return;
        if (currentEngagement - bestEngagement < SwitchAdvantage) return;

        SwitchTo(ctx, best, announceContact: true, reportSenderId: best.LastInformantId);
    }

    /// <summary>Commit to the strongest plausible hypothesis when free.</summary>
    protected virtual void MaybeCommitBest(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is not null)
            return;

        foreach (var h in Hypotheses.Items.OrderByDescending(x => x.Confidence))
        {
            if (h.Confidence < Hypotheses.SuspectThreshold)
                continue;
            if (CountOthersCloser(observation, h.Estimate) >= MaxOthersCloserToJoin)
                continue;

            Commit(ctx, h, announceContact: true);
            if (h.LastSource == HypothesisSource.Rumor && h.LastInformantId is int sid)
            {
                var synthetic = new MessageInfo(
                    Id: -1,
                    Position: h.Estimate,
                    FromType: string.Empty,
                    FromAgentId: sid,
                    ToAgentId: null,
                    Payload: RadioNet.FormatContact(h.LocalId, h.Estimate),
                    TtlRemaining: MessageEchoTtl);
                OnFollowedReport(synthetic, h.Estimate, h.LocalId);
            }

            return;
        }
    }

    /// <summary>Drop commitment when the track was pruned (rumor never confirmed).</summary>
    protected void MaybeDropStaleCommitment(WorldContext ctx)
    {
        if (CommittedIncidentId is not int id)
            return;
        if (Hypotheses.Get(id) is not null)
            return;
        BreakOff(ctx, id);
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
        var track = Hypotheses.Get(id);
        if (track is null) return;

        var orbitRadius = SensorRange * 0.5;
        var target = ResolveCommittedCenter(observation);
        if (observation.DistanceTo(target) > orbitRadius * 1.15)
            return;

        var confident = track.Confidence >= Hypotheses.OrbitThreshold;
        var cued = AnyCueNear(observation, target, orbitRadius);
        if (!confident && !cued)
            return;

        CommittedPosition = target;
        ctx.World.Transmit(this, RadioNet.FormatOrbit(id, CommittedPosition), MessageEchoTtl);
        OrbitAnnounced = true;
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Sparse status while monitoring and a cue still supports the estimate.</summary>
    protected virtual void MaybeBroadcastStatus(WorldContext ctx, AgentObservation observation)
    {
        TicksSinceReport++;
        if (CommittedIncidentId is not int id || TicksSinceReport < ReportCooldown)
            return;
        var track = Hypotheses.Get(id);
        if (track is null)
            return;

        var target = ResolveCommittedCenter(observation);
        if (!AnyCueNear(observation, target, SensorRange * 0.6) &&
            track.LastSource != HypothesisSource.Sensor)
            return;

        CommittedPosition = target;
        ctx.World.Transmit(this, IncidentMarker.Format(id, target), MessageEchoTtl);
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Steer: approach/orbit a suspected event, else personal-space explore.</summary>
    protected virtual AgentAction ChooseAction(AgentObservation observation, double personalSpace)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is { } track)
        {
            var orbitRadius = SensorRange * 0.5;
            var target = ResolveCommittedCenter(observation);
            var dist = observation.DistanceTo(target);
            if (track.Confidence >= Hypotheses.OrbitThreshold || dist <= orbitRadius * 1.2)
            {
                return Steering.OrbitClockwise(
                    observation.SelfPosition, target, orbitRadius,
                    observation.WorldWidth, observation.WorldHeight);
            }

            return Steering.DirectionToward(observation.DisplacementTo(target));
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

    /// <summary>Commit to a hypothesis and optionally radio first contact.</summary>
    protected void Commit(WorldContext ctx, EventHypothesis track, bool announceContact)
    {
        CommittedIncidentId = track.LocalId;
        CommittedPosition = track.Estimate;
        OrbitAnnounced = false;
        TicksSinceReport = ReportCooldown;
        if (announceContact)
        {
            ctx.World.Transmit(this, RadioNet.FormatContact(track.LocalId, track.Estimate), MessageEchoTtl);
            HeardReports.Add((Id, track.LocalId));
            TicksSinceReport = 0;
        }
    }

    /// <summary>Break current commitment (if any) and commit to a new hypothesis.</summary>
    protected void SwitchTo(
        WorldContext ctx,
        EventHypothesis track,
        bool announceContact,
        int? reportSenderId)
    {
        if (CommittedIncidentId is int oldId && oldId != track.LocalId)
            BreakOff(ctx, oldId);

        Commit(ctx, track, announceContact);
        SwitchCooldownRemaining = SwitchCooldownTicks;

        if (reportSenderId is int sid)
        {
            var synthetic = new MessageInfo(
                Id: -1,
                Position: track.Estimate,
                FromType: string.Empty,
                FromAgentId: sid,
                ToAgentId: null,
                Payload: RadioNet.FormatContact(track.LocalId, track.Estimate),
                TtlRemaining: MessageEchoTtl);
            OnFollowedReport(synthetic, track.Estimate, track.LocalId);
        }
    }

    /// <summary>Clear commitment and announce break.</summary>
    protected virtual void BreakOff(WorldContext ctx, int incidentId)
    {
        CommittedIncidentId = null;
        OrbitAnnounced = false;
        HeardClaims.Clear();
        ctx.World.Transmit(this, RadioNet.FormatBreak(incidentId), MessageEchoTtl);
    }

    /// <summary>Last estimate of the current commitment, nudged by a nearby cue if any.</summary>
    protected Vec2 ResolveCommittedCenter(AgentObservation observation)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is { } track)
        {
            var near = NearestCue(observation, track.Estimate, SensorRange * 0.5);
            if (near is { } cue)
            {
                track.Estimate = cue;
                CommittedPosition = cue;
                return cue;
            }

            CommittedPosition = track.Estimate;
            return track.Estimate;
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
        return closerOrEqualPeers + 1 > MaxOrbiters;
    }

    /// <summary>True when a cue this tick lands near <paramref name="center"/>.</summary>
    protected static bool AnyCueNear(AgentObservation observation, Vec2 center, double radius) =>
        observation.NearbyIncidents.Any(c =>
            Vec2.ToroidalDistance(c.Position, center, observation.WorldWidth, observation.WorldHeight) <= radius);

    private static Vec2? NearestCue(AgentObservation observation, Vec2 center, double radius)
    {
        Vec2? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var c in observation.NearbyIncidents)
        {
            var d = Vec2.ToroidalDistance(
                c.Position, center, observation.WorldWidth, observation.WorldHeight);
            if (d <= radius && d < bestD)
            {
                bestD = d;
                best = c.Position;
            }
        }

        return best;
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
