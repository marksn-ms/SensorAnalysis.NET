using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Cooperative searcher that verifies peer radio reports and maintains a Beta–Bernoulli
/// reputation per sender, down-weighting or ignoring low-trust reporters.
/// </summary>
public sealed class TrustAwareSearchAgent : Agent
{
    private readonly Random _rng;
    private readonly Dictionary<int, BetaTrust> _trust = new();
    private int _ticksSinceReport;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _exploreTicksRemaining;
    private PendingProbe? _pending;

    /// <summary>Creates a trust-aware search agent.</summary>
    /// <param name="seed">Optional RNG seed for deterministic exploration.</param>
    public TrustAwareSearchAgent(int? seed = null)
    {
        AgentType = "TrustAwareSearch";
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

    /// <summary>Ticks allowed to verify a followed claim before marking failure.</summary>
    public int VerifyWindowTicks { get; set; } = 40;

    /// <summary>Maximum distance from claimed point to count a verification success.</summary>
    public double VerifyRadius { get; set; } = 60;

    /// <summary>Minimum posterior mean trust required to follow a sender's report.</summary>
    public double TrustThreshold { get; set; } = 0.35;

    /// <summary>Prior successes (alpha) for unseen senders.</summary>
    public double PriorAlpha { get; set; } = 2;

    /// <summary>Prior failures (beta) for unseen senders.</summary>
    public double PriorBeta { get; set; } = 2;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        ResolvePending(observation);

        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(observation.SelfPosition, i.Position))
                .First();
            return Steering.DirectionToward(nearest.Position - observation.SelfPosition);
        }

        if (_pending is { } probe)
            return Steering.DirectionToward(probe.Claimed - observation.SelfPosition);

        var candidates = observation.NearbyMessages
            .Where(m => m.FromAgentId is int sid && sid != Id)
            .Select(m =>
            {
                var ok = IncidentMarker.TryParse(m.Payload, out _, out var claimed);
                return (Ok: ok, Msg: m, Claimed: claimed, Sender: m.FromAgentId!.Value);
            })
            .Where(x => x.Ok && MeanTrust(x.Sender) >= TrustThreshold)
            .OrderByDescending(x => MeanTrust(x.Sender))
            .ThenBy(x => Vec2.Distance(observation.SelfPosition, x.Claimed))
            .ToList();

        if (candidates.Count > 0)
        {
            var best = candidates[0];
            _pending = new PendingProbe(best.Sender, best.Claimed, observation.Tick + VerifyWindowTicks);
            return Steering.DirectionToward(best.Claimed - observation.SelfPosition);
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
                .OrderBy(i => Vec2.Distance(Position, i.Position))
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
        Position = ctx.World.Clamp(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    private void ResolvePending(AgentObservation observation)
    {
        if (_pending is null) return;
        var probe = _pending.Value;

        var confirmed = observation.NearbyIncidents.Any(i =>
            Vec2.Distance(i.Position, probe.Claimed) <= VerifyRadius);

        if (confirmed)
        {
            UpdateTrust(probe.SenderId, success: true);
            _pending = null;
            return;
        }

        if (observation.Tick >= probe.DeadlineTick)
        {
            UpdateTrust(probe.SenderId, success: false);
            _pending = null;
        }
    }

    private double MeanTrust(int senderId)
    {
        var t = GetTrust(senderId);
        return t.Alpha / (t.Alpha + t.Beta);
    }

    private void UpdateTrust(int senderId, bool success)
    {
        var t = GetTrust(senderId);
        _trust[senderId] = success
            ? t with { Alpha = t.Alpha + 1 }
            : t with { Beta = t.Beta + 1 };
    }

    private BetaTrust GetTrust(int senderId)
    {
        if (_trust.TryGetValue(senderId, out var t)) return t;
        t = new BetaTrust(PriorAlpha, PriorBeta);
        _trust[senderId] = t;
        return t;
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

    private readonly record struct BetaTrust(double Alpha, double Beta);
    private readonly record struct PendingProbe(int SenderId, Vec2 Claimed, long DeadlineTick);
}

/// <summary>Factory for <see cref="TrustAwareSearchAgent"/>.</summary>
public sealed class TrustAwareSearchAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "TrustAwareSearch";

    /// <inheritdoc />
    public string Description =>
        "Verifies peer radio reports and discounts low-reputation senders (Beta–Bernoulli).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new TrustAwareSearchAgent { Label = label ?? "TA" };
}
