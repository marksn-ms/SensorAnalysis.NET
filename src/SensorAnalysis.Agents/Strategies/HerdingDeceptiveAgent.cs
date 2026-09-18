using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Local-only infiltrator: no incident oracle. Once an event is sensed, it latches
/// that post and stays to guard it (orbit / herd around the last known location)
/// instead of correlated-walking away on a missed cue. Sparse contact decoys fire
/// once per lure victim, claimed away from the visible event.
/// </summary>
public sealed class HerdingDeceptiveAgent : Agent
{
    private int? _lureVictimId;
    private bool _decoySentForVictim;
    private int _fakeSlot;
    private readonly int[] _fakeIds = { 20_001, 20_002, 20_003, 20_004 };
    private int _heading;
    private bool _headingInitialized;
    private Vec2? _guardAbsolute;
    private int _ticksWithoutGuardCue;

    /// <summary>Ticks of missed cues before abandoning a latched event post.</summary>
    public const int GuardPatienceTicks = 48;

    /// <summary>Creates a local-observation herding deceptive agent.</summary>
    public HerdingDeceptiveAgent()
    {
        AgentType = "HerdingDeceptive";
        Velocity = 5;
        ContributesToIncidentDrain = false;
        MessageEchoTtl = 40;
    }

    /// <summary>
    /// Emits at most one lure decoy per victim, then moves via the four-state herding motion machine.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        MaybeEmitDecoy(ctx, observation);

        LastAction = ChooseMotion(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    private void MaybeEmitDecoy(WorldContext ctx, AgentObservation observation)
    {
        var victims = observation.NearbyAgents.Where(a => !IsDeceptive(a.AgentType)).ToList();
        var events = observation.NearbyIncidents;
        if (victims.Count == 0 || events.Count == 0)
        {
            _lureVictimId = null;
            _decoySentForVictim = false;
            return;
        }

        var victim = RankVictim(observation, victims, events);
        if (_lureVictimId != victim.Id)
        {
            _lureVictimId = victim.Id;
            _decoySentForVictim = false;
        }

        if (_decoySentForVictim)
            return;

        var eventPos = events
            .OrderBy(i => observation.DistanceBetween(victim.Position, i.Position))
            .First()
            .Position;
        var claimed = DecoyLure.AwayFromEvent(victim.Position, eventPos, SensorRange, observation);
        if (claimed is null)
            return;

        ctx.World.Transmit(
            this,
            RadioNet.FormatContact(NextFakeId(), observation.AbsoluteFromRelative(claimed.Value)),
            MessageEchoTtl);
        _decoySentForVictim = true;
    }

    private AgentInfo RankVictim(
        AgentObservation observation,
        List<AgentInfo> victims,
        IReadOnlyList<IncidentInfo> events)
    {
        var personal = SensorRange * 0.5;
        return victims
            .OrderBy(v =>
            {
                var distToNearestEvent = events.Min(i =>
                    observation.DistanceBetween(v.Position, i.Position));
                return (
                    -distToNearestEvent,
                    CountDeceptiveNear(observation, v.Position, personal * 1.5),
                    HashCode.Combine(Id, v.Id)
                );
            })
            .First();
    }

    private AgentAction ChooseMotion(AgentObservation observation)
    {
        var personal = SensorRange * 0.5;
        var halfRange = SensorRange * 0.5;
        UpdateGuard(observation);
        var post = GuardRelative(observation);

        var deceptivePeers = observation.NearbyAgents.Where(a => IsDeceptive(a.AgentType)).ToList();
        var victims = observation.NearbyAgents
            .Where(a => !IsDeceptive(a.AgentType))
            .ToList();
        var hasVictim = victims.Count > 0;
        var hasPost = post is not null;

        if (!hasVictim && !hasPost)
            return Steering.CorrelatedWalk(Rng, ref _heading, ref _headingInitialized);

        var center = post ?? EventCenterFallback(observation);

        // Other infiltrators: stay on the orbit ring rather than fleeing the post.
        if (hasPost && deceptivePeers.Count > 0)
        {
            var nearestDog = deceptivePeers
                .OrderBy(a => observation.DistanceTo(a.Position))
                .First();
            if (observation.DistanceTo(nearestDog.Position) < personal * 0.9)
                return Steering.OrbitClockwise(center, personal, observation);
        }
        else if (!hasPost && deceptivePeers.Count > 0)
        {
            var nearestDog = deceptivePeers
                .OrderBy(a => observation.DistanceTo(a.Position))
                .First();
            if (observation.DistanceTo(nearestDog.Position) < personal * 0.9)
            {
                var delta = observation.DisplacementBetween(nearestDog.Position, Vec2.Zero);
                return Steering.DirectionToward(delta);
            }
        }

        if (!hasVictim)
            return Steering.OrbitClockwise(center, personal, observation);

        if (!hasPost)
            return StandoffFrom(observation, victims, personal);

        return HerdVisible(observation, victims, center, personal, halfRange);
    }

    private void UpdateGuard(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            _guardAbsolute = observation.AbsoluteFromRelative(nearest.Position);
            _ticksWithoutGuardCue = 0;
            return;
        }

        if (_guardAbsolute is null)
            return;

        _ticksWithoutGuardCue++;
        if (_ticksWithoutGuardCue > GuardPatienceTicks)
            _guardAbsolute = null;
    }

    private Vec2? GuardRelative(AgentObservation observation) =>
        _guardAbsolute is Vec2 abs ? observation.RelativeFromAbsolute(abs) : null;

    private static Vec2 EventCenterFallback(AgentObservation observation) =>
        observation.NearbyIncidents.Count == 0
            ? Vec2.Zero
            : observation.NearbyIncidents.OrderBy(i => observation.DistanceTo(i.Position)).First().Position;

    private AgentAction StandoffFrom(
        AgentObservation observation,
        List<AgentInfo> victims,
        double personal)
    {
        var nearest = victims
            .OrderBy(v => observation.DistanceTo(v.Position))
            .First();
        var dist = observation.DistanceTo(nearest.Position);
        if (dist < personal)
        {
            var away = observation.DisplacementBetween(nearest.Position, Vec2.Zero);
            return Steering.DirectionToward(away);
        }

        return Steering.CorrelatedWalk(Rng, ref _heading, ref _headingInitialized);
    }

    private AgentAction HerdVisible(
        AgentObservation observation,
        List<AgentInfo> victims,
        Vec2 eventPos,
        double personal,
        double halfRange)
    {
        if (observation.NearbyIncidents.Count == 0)
            return Steering.OrbitClockwise(eventPos, personal, observation);

        var victim = RankVictim(observation, victims, observation.NearbyIncidents);
        eventPos = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceBetween(victim.Position, i.Position))
            .First()
            .Position;

        // Block on the event→victim ray, no farther than ½ sensor range from the event.
        var toVictim = observation.DisplacementBetween(eventPos, victim.Position);
        var dist = toVictim.Length;
        Vec2 block;
        if (dist < 1e-6)
        {
            block = eventPos + new Vec2(personal, 0);
        }
        else
        {
            var dir = toVictim.Normalize();
            var along = Math.Min(halfRange, dist * 0.5);
            var eventAbs = observation.AbsoluteFromRelative(eventPos);
            block = observation.RelativeFromAbsolute(eventAbs + dir * along);
        }

        return Steering.DirectionToward(observation.DisplacementTo(block));
    }

    private int NextFakeId()
    {
        var id = _fakeIds[_fakeSlot % _fakeIds.Length];
        _fakeSlot++;
        return id;
    }

    private static bool IsDeceptive(string agentType) =>
        agentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase);

    private static int CountDeceptiveNear(AgentObservation observation, Vec2 pos, double radius) =>
        observation.NearbyAgents.Count(a =>
            IsDeceptive(a.AgentType) &&
            observation.DistanceBetween(a.Position, pos) <= radius);
}

/// <summary>Factory for <see cref="HerdingDeceptiveAgent"/>.</summary>
public sealed class HerdingDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "HerdingDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Local infiltrator: latches a sensed event and guards it; decoys when a cooperator is in range.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new HerdingDeceptiveAgent { Label = label ?? "HD" };
}
