using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// MitigatedOrbitAgent is a skeptical, cooperative agent:  Assumes all the other agents encountered are honest until proven otherwise.
/// Searches using random walk, but observes other agents' personal-space while exploring.
/// When an event is discovered, orbit commences at r/2 radius, but still tries to observe
/// personal space of other agents.  If there are already 3 agents orbiting the same event,
/// it will break off and return to searching.  If a message is heard (in range) about a new event, 
/// it will move to assist.  While responding, it will observe the personal space of other agents.
/// Thus, the modalities are: 1) exploring, 2) exploring/avoiding, 3) assisting, 4) assisting/avoiding, 
/// 5) orbiting, 6) orbiting/avoiding.  We could then consider avoiding as an aspect of the other 
/// modalities since it modifies each one.  Along the way, if trust is lost for another agent,
/// it will ignore its presence, meaning it will not be considered in avoidance calculation, and
/// it will not move to assist when the agent reports a new event.  It can continue to lose trust, however.
/// As time passes, trust can be regained, but trust is lost easily and regained slowly.  Trust is
/// considered by direct observation only, not by gossip.
/// Note: if this is problematic, we could set trust regain to be zero or add gossip messages.
/// </summary>
public sealed class MitigatedOrbitAgent : OrbitingCoopAgent, ICrowdIntrusionTelemetry
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private readonly Dictionary<int, int> _offenseCount = new();
    private readonly Dictionary<int, DisreputeBucket> _disreputeToward = new();
    private readonly Dictionary<int, double> _prevPeerDist = new();
    private readonly Dictionary<int, int> _intrusionTicks = new();
    private readonly Dictionary<int, int> _lifetimeInside = new();
    private readonly Dictionary<int, int> _lifetimeClosing = new();
    private readonly Dictionary<int, int> _currentlyInside = new();
    private readonly HashSet<int> _silenceAccusedThisCommitment = new();
    private readonly Dictionary<int, int> _coPresentTicks = new();
    private PendingProbe? _pending;

    /// <summary>Creates a mitigated orbiting agent.</summary>
    public MitigatedOrbitAgent()
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

    /// <summary>
    /// Cumulative penalty weight applied toward each peer (Beta increases), keyed by agent id.
    /// Used by <see cref="Watchers.TrustTelemetryWatcher"/> to separate intramural vs infiltrator damage.
    /// </summary>
    public IReadOnlyDictionary<int, DisreputeBucket> DisreputeToward => _disreputeToward;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> CurrentIntrusionTicks => _intrusionTicks;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> LifetimeInsidePersonalSpaceTicks => _lifetimeInside;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> LifetimeClosingTicks => _lifetimeClosing;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, int> CurrentlyInsidePersonalSpace => _currentlyInside;

    /// <inheritdoc />
    public int HerdingOffenseCountToward(int targetId) =>
        _disreputeToward.TryGetValue(targetId, out var b) ? b.Herding : 0;

    /// <summary>Total penalty weight this agent has applied (all peers, all channels).</summary>
    public double TotalDisreputeWeight
    {
        get
        {
            double sum = 0;
            foreach (var b in _disreputeToward.Values)
                sum += b.TotalWeight;
            return sum;
        }
    }

    /// <inheritdoc />
    protected override void OnBeforeSearch(WorldContext ctx, AgentObservation observation)
    {
        ProcessGossip(ctx, observation);
        ResolvePending(observation);
        ObserveHerdingIntrusions(observation);
    }

    /// <inheritdoc />
    protected override void OnAfterSearch(WorldContext ctx, AgentObservation observation)
    {
        ArmPendingProbe(observation.Tick);
        DetectSilence(ctx, observation);
    }

    /// <inheritdoc />
    protected override void BreakOff(WorldContext ctx, int incidentId)
    {
        base.BreakOff(ctx, incidentId);
        _silenceAccusedThisCommitment.Clear();
        _coPresentTicks.Clear();
    }

    /// <inheritdoc />
    protected override double RumorTrustWeight(MessageInfo msg)
    {
        if (msg.FromAgentId is not int sid)
            return 0;
        return MeanTrust(sid) >= TrustThreshold ? 1 : 0;
    }

    /// <inheritdoc />
    protected override bool ShouldFollowReport(MessageInfo msg, Vec2 claimedRelative)
    {
        if (msg.FromAgentId is not int sid)
            return false;
        return MeanTrust(sid) >= TrustThreshold;
    }

    /// <inheritdoc />
    protected override void OnFollowedReport(MessageInfo msg, Vec2 claimedRelative, int incidentId)
    {
        if (msg.FromAgentId is not int sid)
            return;
        _pending = new PendingProbe(sid, claimedRelative, 0, incidentId);
    }

    /// <inheritdoc />
    protected override void OnSaturatedRumor(MessageInfo msg, Vec2 claimed)
    {
        if (msg.FromAgentId is not int sid)
            return;
        if (!RadioNet.TryParseClaim(msg.Payload, out var kind, out var iid, out _))
            return;
        if (!IsSelfOnStationStatus(msg, sid, iid) && kind != RadioClaimKind.Orbit)
            RegisterOffense(sid, ReputationOffenseKind.Saturated);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<AgentInfo> NeighborsForPersonalSpace(AgentObservation observation) =>
        observation.NearbyAgents
            .Where(a => MeanTrust(a.Id) >= TrustThreshold)
            .ToList();

    /// <summary>
    /// Parking-lot rule: if we are separating/yielding and a peer keeps closing into
    /// personal space, that is herding-like intrusion → escalating reputation hit.
    /// </summary>
    private void ObserveHerdingIntrusions(AgentObservation observation)
    {
        var personal = CooperativeRadii.PersonalSpace(SensorRange);
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
                _currentlyInside[peer.Id] = 0;
                continue;
            }

            _currentlyInside[peer.Id] = 1;
            _lifetimeInside[peer.Id] = _lifetimeInside.GetValueOrDefault(peer.Id) + 1;

            // Closing while already inside our space after we had more clearance.
            var closing = dist + 1e-6 < prev;
            if (closing)
            {
                _intrusionTicks[peer.Id] = _intrusionTicks.GetValueOrDefault(peer.Id) + 1;
                _lifetimeClosing[peer.Id] = _lifetimeClosing.GetValueOrDefault(peer.Id) + 1;
            }
            else if (dist > prev + 0.5)
                _intrusionTicks[peer.Id] = 0;

            if (_intrusionTicks.GetValueOrDefault(peer.Id) >= HerdingGraceTicks)
            {
                RegisterOffense(peer.Id, ReputationOffenseKind.Herding);
                _intrusionTicks[peer.Id] = 0; // require sustained re-offense to escalate again
            }
        }

        foreach (var id in _prevPeerDist.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                _prevPeerDist.Remove(id);
                _intrusionTicks.Remove(id);
                _currentlyInside.Remove(id);
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

        var cueNearClaim = observation.NearbyIncidents.Any(i =>
            observation.DistanceBetween(i.Position, probe.Claimed) <= VerifyRadius);

        if (cueNearClaim)
        {
            RegisterSuccess(probe.SenderId);
            _pending = null;
            return;
        }

        var nearClaim = observation.DistanceTo(probe.Claimed) <= VerifyRadius;
        var ticksNear = probe.TicksNear + (nearClaim ? 1 : 0);
        _pending = probe with { TicksNear = ticksNear };

        // A single empty ping is not evidence of absence. Fail only after lingering
        // near the claim for the verify window with no cue at all.
        if (nearClaim && ticksNear >= VerifyWindowTicks)
        {
            RegisterOffense(probe.SenderId, ReputationOffenseKind.Verify);
            _pending = null;
            return;
        }

        if (observation.Tick >= probe.DeadlineTick)
        {
            RegisterOffense(probe.SenderId, ReputationOffenseKind.Verify);
            _pending = null;
        }
    }

    private void DetectSilence(WorldContext ctx, AgentObservation observation)
    {
        // Only after we have ourselves announced this commitment (reduces false peer blame).
        if (!OrbitAnnounced || CommittedIncidentId is not int iid) return;
        var center = RelativeCommittedCenter(observation);
        var station = SensorRange * 0.6;
        if (observation.DistanceTo(center) > station &&
            !AnyCueNear(observation, center, station))
            return;

        var present = new HashSet<int>();
        foreach (var peer in observation.NearbyAgents)
        {
            var peerInRangeOfEvent = observation.DistanceBetween(peer.Position, center) <= SensorRange * 0.6;
            if (!peerInRangeOfEvent) continue;
            present.Add(peer.Id);
            _coPresentTicks[peer.Id] = _coPresentTicks.GetValueOrDefault(peer.Id) + 1;

            var reportedNear = HeardClaims.Any(c =>
                c.AgentId == peer.Id &&
                observation.DistanceBetween(c.Position, center) <= station)
                || HeardReports.Contains((peer.Id, iid));
            if (reportedNear) continue;
            if (_coPresentTicks[peer.Id] < SilenceGraceTicks) continue;
            if (!_silenceAccusedThisCommitment.Add(peer.Id)) continue;

            RegisterOffense(peer.Id, ReputationOffenseKind.Silence);
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
            var soft = FirstOffenseWeight * 0.5;
            UpdateTrust(accused, success: false, weight: soft);
            AccrueDisrepute(accused, ReputationOffenseKind.Gossip, soft);
        }
    }

    /// <summary>
    /// Escalating penalty: first offense soft (teammate recoverable); repeats grow as 2^(n-1).
    /// </summary>
    private void RegisterOffense(int senderId, ReputationOffenseKind kind)
    {
        var n = _offenseCount.GetValueOrDefault(senderId) + 1;
        _offenseCount[senderId] = n;
        var weight = FirstOffenseWeight * Math.Pow(2, n - 1);
        UpdateTrust(senderId, success: false, weight: weight);
        AccrueDisrepute(senderId, kind, weight);
    }

    private void AccrueDisrepute(int targetId, ReputationOffenseKind kind, double weight)
    {
        _disreputeToward.TryGetValue(targetId, out var bucket);
        _disreputeToward[targetId] = bucket.Add(kind, weight);
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
        int SenderId, Vec2 Claimed, long DeadlineTick, int IncidentId, int TicksNear = 0);
}

/// <summary>Channels that increase private Beta (disrepute) on <see cref="MitigatedOrbitAgent"/>.</summary>
public enum ReputationOffenseKind
{
    /// <summary>Peer kept closing inside personal space after yield.</summary>
    Herding = 0,
    /// <summary>Co-present at announced event without reporting.</summary>
    Silence = 1,
    /// <summary>Urged a saturated / full station.</summary>
    Saturated = 2,
    /// <summary>Followed claim failed verification.</summary>
    Verify = 3,
    /// <summary>Soft weight from authenticated disappointment gossip.</summary>
    Gossip = 4,
    /// <summary>Co-present at a station with no extra local cue-intensity shrink.</summary>
    Progress = 5
}

/// <summary>Per-peer cumulative penalty counts and weights by <see cref="ReputationOffenseKind"/>.</summary>
public readonly struct DisreputeBucket
{
    /// <summary>Total penalty weight (sum of Beta increments).</summary>
    public double TotalWeight { get; init; }

    /// <summary>Offense event counts by kind (index = <see cref="ReputationOffenseKind"/>).</summary>
    public int Herding { get; init; }
    /// <inheritdoc cref="Herding"/>
    public int Silence { get; init; }
    /// <inheritdoc cref="Herding"/>
    public int Saturated { get; init; }
    /// <inheritdoc cref="Herding"/>
    public int Verify { get; init; }
    /// <inheritdoc cref="Herding"/>
    public int Gossip { get; init; }
    /// <inheritdoc cref="Herding"/>
    public int Progress { get; init; }

    /// <summary>Penalty weight by kind.</summary>
    public double HerdingWeight { get; init; }
    /// <inheritdoc cref="HerdingWeight"/>
    public double SilenceWeight { get; init; }
    /// <inheritdoc cref="HerdingWeight"/>
    public double SaturatedWeight { get; init; }
    /// <inheritdoc cref="HerdingWeight"/>
    public double VerifyWeight { get; init; }
    /// <inheritdoc cref="HerdingWeight"/>
    public double GossipWeight { get; init; }
    /// <inheritdoc cref="HerdingWeight"/>
    public double ProgressWeight { get; init; }

    /// <summary>Returns a copy with <paramref name="kind"/> incremented by <paramref name="weight"/>.</summary>
    public DisreputeBucket Add(ReputationOffenseKind kind, double weight) => kind switch
    {
        ReputationOffenseKind.Herding => this with
        {
            TotalWeight = TotalWeight + weight,
            Herding = Herding + 1,
            HerdingWeight = HerdingWeight + weight
        },
        ReputationOffenseKind.Silence => this with
        {
            TotalWeight = TotalWeight + weight,
            Silence = Silence + 1,
            SilenceWeight = SilenceWeight + weight
        },
        ReputationOffenseKind.Saturated => this with
        {
            TotalWeight = TotalWeight + weight,
            Saturated = Saturated + 1,
            SaturatedWeight = SaturatedWeight + weight
        },
        ReputationOffenseKind.Verify => this with
        {
            TotalWeight = TotalWeight + weight,
            Verify = Verify + 1,
            VerifyWeight = VerifyWeight + weight
        },
        ReputationOffenseKind.Gossip => this with
        {
            TotalWeight = TotalWeight + weight,
            Gossip = Gossip + 1,
            GossipWeight = GossipWeight + weight
        },
        ReputationOffenseKind.Progress => this with
        {
            TotalWeight = TotalWeight + weight,
            Progress = Progress + 1,
            ProgressWeight = ProgressWeight + weight
        },
        _ => this with { TotalWeight = TotalWeight + weight }
    };
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
