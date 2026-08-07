using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Mitigated orbiting searcher: honest squad tactics plus private Beta–Bernoulli reputation.
/// Mitigations (direct experience only — no forgeable “name the villain” stimulus):
/// <list type="bullet">
/// <item>Verify followed claims; timeout / empty arrival hurts sender trust.</item>
/// <item>Silence near a true event (narrow: only after we have announced contact/orbit).</item>
/// <item>Herding intrusion: peer keeps closing after we yield → trust hit; we stop yielding
/// to low-trust peers (personal-space filter).</item>
/// <item>Saturated claims: urging a full station → ignore + trust hit (not on-station status).</item>
/// </list>
/// Repeat offenders draw escalating penalties; first offenses are soft so teammates can recover.
/// </summary>
public sealed class MitigatedOrbitAgent : OrbitingCoopAgent
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private readonly Dictionary<int, int> _offenseCount = new();
    private readonly Dictionary<int, double> _prevPeerDist = new();
    private readonly Dictionary<int, int> _intrusionTicks = new();
    private readonly HashSet<int> _silenceAccusedThisCommitment = new();
    private readonly Dictionary<int, int> _coPresentTicks = new();
    private PendingProbe? _pending;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;

    /// <summary>Creates a mitigated orbiting agent.</summary>
    public MitigatedOrbitAgent(int? seed = null) : base(seed)
    {
        AgentType = "MitigatedOrbit";
    }

    /// <summary>Minimum mean trust to follow radio claims and to yield personal space.</summary>
    public double TrustThreshold { get; set; } = 0.38;

    /// <summary>Ticks to verify a followed claim.</summary>
    public int VerifyWindowTicks { get; set; } = 40;

    /// <summary>How close to a claim counts as arrival for verification.</summary>
    public double VerifyRadius { get; set; } = 50;

    /// <summary>Beta prior alpha (with PriorBeta ⇒ prior mean 0.5).</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Beta prior beta.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <summary>Base weight for a first offense (escalates as  base·2^(n-1) on repeats).</summary>
    public double FirstOffenseWeight { get; set; } = 0.4;

    /// <summary>Ticks a peer must share the event without reporting before silence blame.</summary>
    public int SilenceGraceTicks { get; set; } = 22;

    /// <summary>Ticks of persistent closing-after-yield before a herding intrusion offense.</summary>
    public int HerdingGraceTicks { get; set; } = 10;

    /// <summary>Mean trust toward a peer (prior if unknown).</summary>
    public double MeanTrustToward(int agentId) => MeanTrust(agentId);

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        ProcessGossip(ctx, observation);
        ResolvePending(observation);
        ObserveHerdingIntrusions(observation);

        if (SwitchCooldownRemaining > 0)
            SwitchCooldownRemaining--;

        var personalSpace = SensorRange * 0.5;
        IngestReports(observation);
        MaybeRebalanceOvercrowd(ctx, observation);
        MaybeSwitchFromSight(ctx, observation);
        MaybeSwitchFromReports(ctx, observation);
        MaybeCommitFromSight(ctx, observation);
        MaybeCommitFromReports(ctx, observation);
        MaybeAnnounceOrbit(ctx, observation);
        MaybeBroadcastStatus(ctx, observation);
        DetectSilence(ctx, observation);

        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;

        if (CommittedIncidentId is int id &&
            !ctx.World.ActiveIncidents.Any(i => i.Id == id) &&
            observation.NearbyIncidents.All(i => i.Id != id))
        {
            BreakOff(ctx, id);
            _silenceAccusedThisCommitment.Clear();
            _coPresentTicks.Clear();
        }
    }

    /// <inheritdoc />
    protected override bool ShouldFollowReport(MessageInfo msg, Vec2 claimed)
    {
        if (msg.FromAgentId is not int sid)
            return false;
        return MeanTrust(sid) >= TrustThreshold;
    }

    /// <inheritdoc />
    protected override void OnFollowedReport(MessageInfo msg, Vec2 claimed, int incidentId)
    {
        if (msg.FromAgentId is not int sid)
            return;
        _pending = new PendingProbe(sid, claimed, 0, incidentId);
    }

    /// <inheritdoc />
    protected override void MaybeCommitFromReports(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;

        foreach (var msg in observation.NearbyMessages)
        {
            if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out var claimed))
                continue;
            if (kind == RadioClaimKind.Break) continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            var othersCloser = CountOthersCloser(observation, claimed);
            if (othersCloser >= MaxOthersCloserToJoin)
            {
                // Saturated claim from a remote reporter — ignore and mark offense.
                if (msg.FromAgentId is int sid && !IsSelfOnStationStatus(msg, sid, iid))
                    RegisterOffense(sid);
                continue;
            }

            Commit(ctx, iid, claimed, announceContact: true);
            OnFollowedReport(msg, claimed, iid);
            ArmPendingProbe(observation.Tick);
            return;
        }
    }

    /// <inheritdoc />
    protected override void MaybeSwitchFromReports(WorldContext ctx, AgentObservation observation)
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
            if (claimEngagement >= MaxOthersCloserToJoin)
            {
                if (msg.FromAgentId is int sid && !IsSelfOnStationStatus(msg, sid, iid))
                    RegisterOffense(sid);
                continue;
            }

            if (currentEngagement - claimEngagement < SwitchAdvantage)
                continue;

            SwitchTo(ctx, iid, claimed, announceContact: true, reportSenderId: msg.FromAgentId);
            ArmPendingProbe(observation.Tick);
            return;
        }
    }

    /// <summary>
    /// Explore while yielding personal space only to trusted peers.
    /// Distrusted agents can still be sensed for intrusion scoring, but do not steer us.
    /// </summary>
    protected override AgentAction ChooseAction(AgentObservation observation, double personalSpace)
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

        var trustedNeighbors = observation.NearbyAgents
            .Where(a => MeanTrust(a.Id) >= TrustThreshold)
            .ToList();

        return Steering.ExploreWithPersonalSpace(
            observation.SelfPosition,
            _exploreHeading,
            _explorePersist,
            trustedNeighbors,
            personalSpace,
            observation.WorldWidth,
            observation.WorldHeight,
            Rng,
            out _exploreHeading,
            out _explorePersist);
    }

    /// <summary>
    /// Parking-lot rule: if we are separating/yielding and a peer keeps closing into
    /// personal space, that is herding-like intrusion → escalating reputation hit.
    /// </summary>
    private void ObserveHerdingIntrusions(AgentObservation observation)
    {
        var personal = SensorRange * 0.5;
        var seen = new HashSet<int>();

        foreach (var peer in observation.NearbyAgents)
        {
            seen.Add(peer.Id);
            var dist = observation.DistanceTo(peer.Position);
            var prev = _prevPeerDist.GetValueOrDefault(peer.Id, dist);
            _prevPeerDist[peer.Id] = dist;

            if (dist >= personal)
            {
                _intrusionTicks[peer.Id] = 0;
                continue;
            }

            // Closing while already inside our space after we had more clearance.
            var closing = dist + 1e-6 < prev;
            if (closing)
                _intrusionTicks[peer.Id] = _intrusionTicks.GetValueOrDefault(peer.Id) + 1;
            else if (dist > prev + 0.5)
                _intrusionTicks[peer.Id] = 0;

            if (_intrusionTicks.GetValueOrDefault(peer.Id) >= HerdingGraceTicks)
            {
                RegisterOffense(peer.Id);
                _intrusionTicks[peer.Id] = 0; // require sustained re-offense to escalate again
            }
        }

        foreach (var id in _prevPeerDist.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                _prevPeerDist.Remove(id);
                _intrusionTicks.Remove(id);
            }
        }
    }

    private void ArmPendingProbe(long tick)
    {
        if (_pending is { DeadlineTick: 0 } probe)
            _pending = probe with { DeadlineTick = tick + VerifyWindowTicks };
    }

    private void ResolvePending(AgentObservation observation)
    {
        if (_pending is null) return;
        var probe = _pending.Value;
        if (probe.DeadlineTick == 0)
            return;

        var confirmed = observation.NearbyIncidents.Any(i =>
            Vec2.ToroidalDistance(i.Position, probe.Claimed, observation.WorldWidth, observation.WorldHeight) <= VerifyRadius);

        if (confirmed)
        {
            RegisterSuccess(probe.SenderId);
            _pending = null;
            return;
        }

        var nearClaim = observation.DistanceTo(probe.Claimed) <= VerifyRadius;
        if (nearClaim && observation.NearbyIncidents.Count == 0)
        {
            RegisterOffense(probe.SenderId);
            _pending = null;
            return;
        }

        if (observation.Tick >= probe.DeadlineTick)
        {
            RegisterOffense(probe.SenderId);
            _pending = null;
        }
    }

    private void DetectSilence(WorldContext ctx, AgentObservation observation)
    {
        // Only after we have ourselves announced this commitment (reduces false peer blame).
        if (!OrbitAnnounced || CommittedIncidentId is not int iid) return;
        if (observation.NearbyIncidents.All(i => i.Id != iid)) return;

        var present = new HashSet<int>();
        foreach (var peer in observation.NearbyAgents)
        {
            var peerInRangeOfEvent = Vec2.ToroidalDistance(
                peer.Position, CommittedPosition, observation.WorldWidth, observation.WorldHeight) <= SensorRange * 0.6;
            if (!peerInRangeOfEvent) continue;
            present.Add(peer.Id);
            _coPresentTicks[peer.Id] = _coPresentTicks.GetValueOrDefault(peer.Id) + 1;

            if (HeardReports.Contains((peer.Id, iid))) continue;
            if (_coPresentTicks[peer.Id] < SilenceGraceTicks) continue;
            if (!_silenceAccusedThisCommitment.Add(peer.Id)) continue;

            RegisterOffense(peer.Id);
            // Authenticated disappointment: evidence-tied, not a free-form blacklist broadcast.
            ctx.World.Transmit(
                this,
                IncidentMarker.FormatDisappointment(peer.Id, $"silence:{iid}"),
                MessageEchoTtl);
        }

        foreach (var id in _coPresentTicks.Keys.ToList())
        {
            if (!present.Contains(id))
                _coPresentTicks.Remove(id);
        }
    }

    private void ProcessGossip(WorldContext ctx, AgentObservation observation)
    {
        foreach (var msg in observation.NearbyMessages)
        {
            if (!IncidentMarker.TryParseDisappointment(msg.Payload, out var accused, out _))
                continue;
            if (msg.FromAgentId is not int sid)
                continue;

            var sender = ctx.World.FindAgent(sid);
            if (sender is null ||
                !IncidentMarker.DisappointmentAuthorTypes.Contains(sender.AgentType))
                continue;
            // Soft weight: gossip corroborates but does not escalate like direct offense.
            UpdateTrust(accused, success: false, weight: FirstOffenseWeight * 0.5);
        }
    }

    /// <summary>
    /// Escalating penalty: first offense soft (teammate recoverable); repeats grow as 2^(n-1).
    /// </summary>
    private void RegisterOffense(int senderId)
    {
        var n = _offenseCount.GetValueOrDefault(senderId) + 1;
        _offenseCount[senderId] = n;
        var weight = FirstOffenseWeight * Math.Pow(2, n - 1);
        UpdateTrust(senderId, success: false, weight: weight);
    }

    private void RegisterSuccess(int senderId)
    {
        // Successful verify restores trust and decays offense streak.
        if (_offenseCount.GetValueOrDefault(senderId) > 0)
            _offenseCount[senderId] = Math.Max(0, _offenseCount[senderId] - 1);
        UpdateTrust(senderId, success: true, weight: 1);
    }

    private static bool IsSelfOnStationStatus(MessageInfo msg, int senderId, int incidentId)
    {
        // Orbit/status from someone already committed to that id is legitimate monitoring chatter.
        if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out _))
            return false;
        return iid == incidentId &&
               (kind == RadioClaimKind.Orbit || kind == RadioClaimKind.Status);
    }

    private double MeanTrust(int senderId)
    {
        var t = GetTrust(senderId);
        return t.Alpha / (t.Alpha + t.Beta);
    }

    private void UpdateTrust(int senderId, bool success, double weight = 1)
    {
        var t = GetTrust(senderId);
        _trust[senderId] = success
            ? t with { Alpha = t.Alpha + weight }
            : t with { Beta = t.Beta + weight };
    }

    private BetaTrust GetTrust(int senderId)
    {
        if (_trust.TryGetValue(senderId, out var t)) return t;
        t = new BetaTrust(PriorAlpha, PriorBeta);
        _trust[senderId] = t;
        return t;
    }

    private readonly record struct BetaTrust(double Alpha, double Beta);

    private readonly record struct PendingProbe(
        int SenderId, Vec2 Claimed, long DeadlineTick, int IncidentId);
}

/// <summary>Factory for <see cref="MitigatedOrbitAgent"/>.</summary>
public sealed class MitigatedOrbitAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "MitigatedOrbit";

    /// <inheritdoc />
    public string Description =>
        "Orbiting coop + private reputation (verify, silence, herding intrusion, saturated claims).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new MitigatedOrbitAgent { Label = label ?? "MO" };
}
