using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Mitigated orbiting searcher: honest squad tactics plus Beta–Bernoulli reputation,
/// silence detection, false-lure disappointment, and authenticated disappointment gossip
/// (receivers accept gossip only from live <c>MitigatedOrbit</c> senders — non-forgeable).
/// </summary>
public sealed class MitigatedOrbitAgent : OrbitingCoopAgent
{
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private PendingProbe? _pending;
    private readonly HashSet<int> _silenceAccusedThisCommitment = new();
    private readonly Dictionary<int, int> _coPresentTicks = new();

    /// <summary>Creates a mitigated orbiting agent.</summary>
    public MitigatedOrbitAgent(int? seed = null) : base(seed)
    {
        AgentType = "MitigatedOrbit";
    }

    /// <summary>Minimum mean trust to follow a peer incident report.</summary>
    public double TrustThreshold { get; set; } = 0.45;

    /// <summary>Ticks to verify a followed claim.</summary>
    public int VerifyWindowTicks { get; set; } = 35;

    /// <summary>How close to a claim counts as arrival for verification.</summary>
    public double VerifyRadius { get; set; } = 50;

    /// <summary>Beta prior alpha.</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Beta prior beta.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <summary>Reputation penalty applied on confirmed deception / silence.</summary>
    public double BlameBoost { get; set; } = 2;

    /// <summary>Ticks a peer must share the event without reporting before silence blame.</summary>
    public int SilenceGraceTicks { get; set; } = 18;

    /// <summary>Mean trust toward a peer (1 if unknown uses prior).</summary>
    public double MeanTrustToward(int agentId) => MeanTrust(agentId);

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        ProcessGossip(ctx, observation);
        ResolvePending(observation);
        DetectSilence(ctx, observation);

        // Base pipeline (ingest/commit/broadcast/move) with overridden hooks.
        var personalSpace = SensorRange * 0.5;
        IngestReports(observation);
        MaybeCommitFromSight(observation);
        MaybeCommitFromReports(observation);
        MaybeBroadcast(ctx, observation);

        LastAction = ChooseAction(observation, personalSpace);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;

        if (CommittedIncidentId is int id &&
            !ctx.World.ActiveIncidents.Any(i => i.Id == id) &&
            observation.NearbyIncidents.All(i => i.Id != id))
        {
            CommittedIncidentId = null;
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
    protected override void MaybeCommitFromReports(AgentObservation observation)
    {
        if (CommittedIncidentId is not null) return;

        foreach (var msg in observation.NearbyMessages)
        {
            if (!IncidentMarker.TryParse(msg.Payload, out var iid, out var claimed))
                continue;
            if (!ShouldFollowReport(msg, claimed))
                continue;

            var othersCloser = observation.NearbyAgents.Count(a =>
                Vec2.ToroidalDistance(a.Position, claimed, observation.WorldWidth, observation.WorldHeight) + 1e-6 <
                observation.DistanceTo(claimed));
            if (othersCloser >= MaxOthersCloserToJoin)
                continue;

            Commit(iid, claimed);
            if (msg.FromAgentId is int sid)
                _pending = new PendingProbe(sid, claimed, observation.Tick + VerifyWindowTicks, iid);
            return;
        }
    }

    private void ResolvePending(AgentObservation observation)
    {
        if (_pending is null) return;
        var probe = _pending.Value;

        var confirmed = observation.NearbyIncidents.Any(i =>
            Vec2.ToroidalDistance(i.Position, probe.Claimed, observation.WorldWidth, observation.WorldHeight) <= VerifyRadius);

        if (confirmed)
        {
            UpdateTrust(probe.SenderId, success: true);
            _pending = null;
            return;
        }

        var nearClaim = observation.DistanceTo(probe.Claimed) <= VerifyRadius;
        if (nearClaim && observation.NearbyIncidents.Count == 0)
        {
            // Arrived; nothing here — disappointment.
            UpdateTrust(probe.SenderId, success: false, weight: BlameBoost);
            _pending = null;
            return;
        }

        if (observation.Tick >= probe.DeadlineTick)
        {
            UpdateTrust(probe.SenderId, success: false);
            _pending = null;
        }
    }

    private void DetectSilence(WorldContext ctx, AgentObservation observation)
    {
        if (CommittedIncidentId is not int iid) return;
        if (observation.NearbyIncidents.All(i => i.Id != iid)) return;

        var present = new HashSet<int>();
        foreach (var peer in observation.NearbyAgents)
        {
            var peerInRangeOfEvent = Vec2.ToroidalDistance(
                peer.Position, CommittedPosition, observation.WorldWidth, observation.WorldHeight) <= SensorRange;
            if (!peerInRangeOfEvent) continue;
            present.Add(peer.Id);
            _coPresentTicks[peer.Id] = _coPresentTicks.GetValueOrDefault(peer.Id) + 1;

            if (HeardReports.Contains((peer.Id, iid))) continue;
            if (_coPresentTicks[peer.Id] < SilenceGraceTicks) continue;
            if (!_silenceAccusedThisCommitment.Add(peer.Id)) continue;

            UpdateTrust(peer.Id, success: false, weight: BlameBoost);
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

            // Non-forgeable: live sender must be an authorized mitigated type.
            var sender = ctx.World.FindAgent(sid);
            if (sender is null ||
                !IncidentMarker.DisappointmentAuthorTypes.Contains(sender.AgentType))
                continue;

            UpdateTrust(accused, success: false, weight: 1);
        }
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
        "Orbiting coop + reputation, silence detection, and authenticated disappointment gossip.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new MitigatedOrbitAgent { Label = label ?? "MO" };
}
