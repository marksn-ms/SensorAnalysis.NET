using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Sensing;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// OrbitingCoopAgent is a naive, cooperative agent:  Assumes all the other agents encountered are honest.
/// Searches using random walk, but observes other agents' personal-space while exploring.
/// When an event is discovered, orbit commences at r/2 radius, but still tries to observe
/// personal space of other agents.  If there are already 3 agents orbiting the same event,
/// it will break off and return to searching.  If a message is heard (in range) about a new event, 
/// it will move to assist.  While responding, it will observe the personal space of other agents.
/// Thus, the modalities are: 1) exploring, 2) exploring/avoiding, 3) assisting, 4) assisting/avoiding, 
/// 5) orbiting, 6) orbiting/avoiding.  We could then consider avoiding as an aspect of the other 
/// modalities since it modifies each one.
/// </summary>
public class OrbitingCoopAgent : Agent
{
    /// <summary>Ticks since last incident status broadcast (for cooldown).</summary>
    protected int TicksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    /// <summary>Local hypothesis id currently being orbited (null if none). Not a world incident id.</summary>
    protected int? CommittedIncidentId;
    /// <summary>Incident position currently being orbited (last estimate, absolute world coords).</summary>
    protected Vec2 CommittedPosition;
    /// <summary>Whether an orbit-confirmed radio call has been sent for the current commitment.</summary>
    protected bool OrbitAnnounced;
    /// <summary>Incident reports overheard (agent id, claimed id) — legacy pairing for subclasses.</summary>
    protected readonly HashSet<(int AgentId, int IncidentId)> HeardReports = new();
    /// <summary>Claimed locations overheard this commitment (observer-relative; ids are untrusted).</summary>
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
    public OrbitingCoopAgent()
    {
        AgentType = "OrbitingCoop";
        Velocity = 5;
        ContributesToIncidentDrain = true;
        ActionIntervalBase = 10;
        MessageEchoTtl = 40;
    }

    /// <summary>Maximum agents that may share one event before yielding (default 3).</summary>
    public int MaxShareEvent { get; set; } = 3;

    /// <summary>How many nearer peers block a bird-in-hand join (<c>MaxShareEvent - 1</c>).</summary>
    protected int MaxOthersCloserToJoin => Math.Max(0, MaxShareEvent - 1);

    /// <summary>Ticks to wait after a break before re-evaluating overcrowding (anti-thrash).</summary>
    public int SwitchCooldownTicks { get; set; } = 12;

    /// <summary>
    /// Updates hypotheses, commitments, orbit/status broadcasts, and movement for one tick.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        Hypotheses.AssociationRadius = CooperativeRadii.AssociationRadius(SensorRange);

        OnBeforeSearch(ctx, observation);

        if (SwitchCooldownRemaining > 0)
            SwitchCooldownRemaining--;

        Hypotheses.TickAge();
        IngestCues(observation);
        IngestRumors(ctx, observation);
        Hypotheses.DecayAndPrune();

        MaybeRebalanceOvercrowd(ctx, observation);
        MaybeCommitBest(ctx, observation);
        MaybeDropStaleCommitment(ctx);

        MaybeAnnounceOrbit(ctx, observation);
        MaybeBroadcastStatus(ctx, observation);

        OnAfterSearch(ctx, observation);

        var personalSpace = CooperativeRadii.PersonalSpace(SensorRange);
        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
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
        foreach (var cue in observation.NearbyIncidents)
        {
            var strength = cue.Strength > 0 ? cue.Strength : 0.5;
            Hypotheses.IngestCue(cue.Position, strength, observation);
        }
    }

    /// <summary>Treat radio claims as rumors (same memory as sensing).</summary>
    protected virtual void IngestRumors(WorldContext ctx, AgentObservation observation)
    {
        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out var claimedAbsolute))
                continue;
            if (kind == RadioClaimKind.Break)
                continue;
            var claimed = observation.RelativeFromAbsolute(claimedAbsolute);
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
                observation,
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
    /// If more than <see cref="MaxShareEvent"/> agents are on-station, or the pack is
    /// tightly clumped, yield and resume exploration (keeps ~120° spacing intent).
    /// </summary>
    protected virtual void MaybeRebalanceOvercrowd(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int id) return;
        if (SwitchCooldownRemaining > 0) return;
        if (Hypotheses.Get(id) is null) return;

        var orbitRadius = CooperativeRadii.OrbitRadius(SensorRange);
        var stationRadius = orbitRadius * 1.2;
        var center = RelativeCommittedCenter(observation);
        var onStation = CountAgentsNear(observation, center, stationRadius);
        var total = onStation + 1;

        var overcrowded = total > MaxShareEvent && ShouldYieldStation(observation, center, stationRadius);
        var tightlyClumped = false;
        if (!overcrowded && total >= 2)
        {
            var nearestPeer = observation.NearbyAgents
                .Where(a => observation.DistanceBetween(a.Position, center) <= stationRadius)
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

    /// <summary>Commit to the strongest plausible hypothesis when free.</summary>
    protected virtual void MaybeCommitBest(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is not null)
            return;

        foreach (var h in Hypotheses.Items.OrderByDescending(x => x.Confidence))
        {
            if (h.Confidence < Hypotheses.SuspectThreshold)
                continue;
            var targetRelative = observation.RelativeFromAbsolute(h.Estimate);
            if (CountOthersCloser(observation, targetRelative) >= MaxOthersCloserToJoin)
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
                OnFollowedReport(synthetic, targetRelative, h.LocalId);
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
    protected virtual bool ShouldFollowReport(MessageInfo msg, Vec2 claimedRelative) => true;

    /// <summary>Hook for mitigated agents to start a verification probe after following a report.</summary>
    protected virtual void OnFollowedReport(MessageInfo msg, Vec2 claimedRelative, int incidentId)
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

        var orbitRadius = CooperativeRadii.OrbitRadius(SensorRange);
        var target = RelativeCommittedCenter(observation);
        if (observation.DistanceTo(target) > orbitRadius * 1.15)
            return;

        var confident = track.Confidence >= Hypotheses.OrbitThreshold;
        var cued = AnyCueNear(observation, target, orbitRadius);
        if (!confident && !cued)
            return;

        CommittedPosition = track.Estimate;
        ctx.World.Transmit(this, RadioNet.FormatOrbit(id, CommittedPosition), MessageEchoTtl);
        OrbitAnnounced = true;
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Sparse status while monitoring and a cue still supports the estimate.</summary>
    protected virtual void MaybeBroadcastStatus(WorldContext ctx, AgentObservation observation)
    {
        TicksSinceReport++;
        if (CommittedIncidentId is not int id || TicksSinceReport < ActionInterval)
            return;
        var track = Hypotheses.Get(id);
        if (track is null)
            return;

        var targetRelative = RelativeCommittedCenter(observation);
        if (!AnyCueNear(observation, targetRelative, SensorRange * 0.6) &&
            track.LastSource != HypothesisSource.Sensor)
            return;

        CommittedPosition = track.Estimate;
        ctx.World.Transmit(this, IncidentMarker.Format(id, CommittedPosition), MessageEchoTtl);
        HeardReports.Add((Id, id));
        TicksSinceReport = 0;
    }

    /// <summary>Steer: approach/orbit a suspected event, else personal-space explore.</summary>
    protected virtual AgentAction ChooseAction(AgentObservation observation, double personalSpace)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is { } track)
        {
            var orbitRadius = CooperativeRadii.OrbitRadius(SensorRange);
            var target = RelativeCommittedCenter(observation);
            var dist = observation.DistanceTo(target);
            var neighbors = NeighborsForPersonalSpace(observation);
            AgentAction preferred;
            if (track.Confidence >= Hypotheses.OrbitThreshold || dist <= orbitRadius * 1.2)
            {
                preferred = Steering.OrbitClockwise(target, orbitRadius, observation);
            }
            else
            {
                preferred = Steering.DirectionToward(observation.DisplacementTo(target));
            }

            return Steering.RespectPersonalSpace(
                preferred,
                neighbors,
                personalSpace,
                Velocity,
                observation,
                peer => ShouldYieldPersonalSpace(observation, peer, target, personalSpace));
        }

        return Steering.ExploreWithPersonalSpace(
            _exploreHeading,
            _explorePersist,
            NeighborsForPersonalSpace(observation),
            personalSpace,
            Velocity,
            observation,
            Rng,
            HeadingChangeInterval,
            out _exploreHeading,
            out _explorePersist);
    }

    /// <summary>Peers considered for personal-space yielding (all nearby agents for naive).</summary>
    protected virtual IReadOnlyList<AgentInfo> NeighborsForPersonalSpace(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <summary>
    /// True when this agent should slow/stay so <paramref name="peer"/> can keep the preferred task motion.
    /// Farther from the event yields to closer; equal range ties break on agent id.
    /// </summary>
    protected virtual bool ShouldYieldPersonalSpace(
        AgentObservation observation,
        AgentInfo peer,
        Vec2 eventCenterRelative,
        double personalSpace)
    {
        if (observation.DistanceTo(peer.Position) >= personalSpace)
            return false;

        var selfToEvent = observation.DistanceTo(eventCenterRelative);
        var peerToEvent = observation.DistanceBetween(peer.Position, eventCenterRelative);
        if (peerToEvent + 1e-6 < selfToEvent)
            return true;
        if (selfToEvent + 1e-6 < peerToEvent)
            return false;
        return Id > peer.Id;
    }

    /// <summary>Commit to a hypothesis and optionally radio first contact.</summary>
    protected void Commit(WorldContext ctx, EventHypothesis track, bool announceContact)
    {
        CommittedIncidentId = track.LocalId;
        CommittedPosition = track.Estimate;
        OrbitAnnounced = false;
        TicksSinceReport = ActionInterval;
        if (announceContact)
        {
            ctx.World.Transmit(this, RadioNet.FormatContact(track.LocalId, track.Estimate), MessageEchoTtl);
            HeardReports.Add((Id, track.LocalId));
            TicksSinceReport = 0;
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

    /// <summary>
    /// Smoothed commitment estimate in observer-relative coords.
    /// Uses <see cref="EventHypothesis.Estimate"/> (updated by cue/rumor ingest), not raw per-tick cues.
    /// </summary>
    protected Vec2 RelativeCommittedCenter(AgentObservation observation)
    {
        if (CommittedIncidentId is int id && Hypotheses.Get(id) is { } track)
        {
            CommittedPosition = track.Estimate;
            return observation.RelativeFromAbsolute(track.Estimate);
        }

        return observation.RelativeFromAbsolute(CommittedPosition);
    }

    /// <summary>Absolute last estimate of the current commitment (for subclasses).</summary>
    protected Vec2 ResolveCommittedCenter(AgentObservation observation)
    {
        var relative = RelativeCommittedCenter(observation);
        return CommittedIncidentId is not null
            ? CommittedPosition
            : observation.AbsoluteFromRelative(relative);
    }

    /// <summary>Peers closer to <paramref name="targetRelative"/> than self (bird-in-hand engagement).</summary>
    protected static int CountOthersCloser(AgentObservation observation, Vec2 targetRelative)
    {
        var selfDist = observation.DistanceTo(targetRelative);
        return observation.NearbyAgents.Count(a =>
            observation.DistanceBetween(a.Position, targetRelative) + 1e-6 < selfDist);
    }

    /// <summary>Peers within <paramref name="radius"/> of <paramref name="centerRelative"/>.</summary>
    protected static int CountAgentsNear(AgentObservation observation, Vec2 centerRelative, double radius) =>
        observation.NearbyAgents.Count(a =>
            observation.DistanceBetween(a.Position, centerRelative) <= radius);

    /// <summary>
    /// True when self is not among the <see cref="MaxShareEvent"/> closest agents to the event
    /// (so we should yield the station).
    /// </summary>
    protected bool ShouldYieldStation(AgentObservation observation, Vec2 centerRelative, double stationRadius)
    {
        var selfDist = observation.DistanceTo(centerRelative);
        var closerOrEqualPeers = observation.NearbyAgents.Count(a =>
        {
            var d = observation.DistanceBetween(a.Position, centerRelative);
            return d <= stationRadius && d <= selfDist + 1e-6;
        });
        return closerOrEqualPeers + 1 > MaxShareEvent;
    }

    /// <summary>True when a cue this tick lands near <paramref name="centerRelative"/>.</summary>
    protected static bool AnyCueNear(AgentObservation observation, Vec2 centerRelative, double radius) =>
        observation.NearbyIncidents.Any(c =>
            observation.DistanceBetween(c.Position, centerRelative) <= radius);
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
