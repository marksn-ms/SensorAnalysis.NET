using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Local-only infiltrator: no incident oracle. Motion is a four-state machine on
/// whether an honest peer and/or an event is in sensor range. Sparse contact
/// decoys fire once per lure victim, claimed away from the visible event.
/// </summary>
public sealed class HerdingDeceptiveAgent : Agent
{
    private readonly Random _rng;
    private int? _lureVictimId;
    private bool _decoySentForVictim;
    private int _fakeSlot;
    private readonly int[] _fakeIds = { 20_001, 20_002, 20_003, 20_004 };

    /// <summary>Creates a local-observation herding deceptive agent.</summary>
    public HerdingDeceptiveAgent(int? seed = null)
    {
        AgentType = "HerdingDeceptive";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = false;
        ContributesToIncidentDrain = false;
    }

    /// <summary>Unused; decoys are one-per-victim rather than periodic spam.</summary>
    public int DecoyPeriod { get; set; } = 30;

    /// <summary>World-echo TTL.</summary>
    public int MessageEchoTtl { get; set; } = 40;

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        MaybeEmitDecoy(ctx, observation);

        LastAction = ChooseMotion(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
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

        var w = observation.WorldWidth;
        var h = observation.WorldHeight;
        var eventPos = events
            .OrderBy(i => Vec2.ToroidalDistance(victim.Position, i.Position, w, h))
            .First()
            .Position;
        var claimed = DecoyLure.AwayFromEvent(victim.Position, eventPos, SensorRange, w, h);
        if (claimed is null)
            return;

        ctx.World.Transmit(
            this,
            RadioNet.FormatContact(NextFakeId(), claimed.Value),
            MessageEchoTtl);
        _decoySentForVictim = true;
    }

    private AgentInfo RankVictim(
        AgentObservation observation,
        List<AgentInfo> victims,
        IReadOnlyList<IncidentInfo> events)
    {
        var personal = SensorRange * 0.5;
        var w = observation.WorldWidth;
        var h = observation.WorldHeight;
        return victims
            .OrderBy(v =>
            {
                var distToNearestEvent = events.Min(i =>
                    Vec2.ToroidalDistance(v.Position, i.Position, w, h));
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
        var w = observation.WorldWidth;
        var h = observation.WorldHeight;

        // Mild repulsion from other deceptive agents to prevent hip-lock clumping.
        var deceptivePeers = observation.NearbyAgents.Where(a => IsDeceptive(a.AgentType)).ToList();
        if (deceptivePeers.Count > 0)
        {
            var nearestDog = deceptivePeers
                .OrderBy(a => observation.DistanceTo(a.Position))
                .First();
            if (observation.DistanceTo(nearestDog.Position) < personal * 0.9)
            {
                var delta = Vec2.ToroidalDisplacement(
                    nearestDog.Position, observation.SelfPosition, w, h);
                return Steering.DirectionToward(delta);
            }
        }

        var victims = observation.NearbyAgents
            .Where(a => !IsDeceptive(a.AgentType))
            .ToList();
        var events = observation.NearbyIncidents;
        var hasVictim = victims.Count > 0;
        var hasEvent = events.Count > 0;

        if (!hasVictim && !hasEvent)
            return LazyLoiter();

        if (!hasVictim && hasEvent)
        {
            var target = events
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            return Steering.OrbitClockwise(
                observation.SelfPosition, target.Position, personal, w, h);
        }

        if (hasVictim && !hasEvent)
            return StandoffFrom(observation, victims, personal, w, h);

        return HerdVisible(observation, victims, events, personal, halfRange, w, h);
    }

    private AgentAction StandoffFrom(
        AgentObservation observation,
        List<AgentInfo> victims,
        double personal,
        double w,
        double h)
    {
        var nearest = victims
            .OrderBy(v => observation.DistanceTo(v.Position))
            .First();
        var dist = observation.DistanceTo(nearest.Position);
        if (dist < personal)
        {
            var away = Vec2.ToroidalDisplacement(
                nearest.Position, observation.SelfPosition, w, h);
            return Steering.DirectionToward(away);
        }

        return LazyLoiter();
    }

    private AgentAction HerdVisible(
        AgentObservation observation,
        List<AgentInfo> victims,
        IReadOnlyList<IncidentInfo> events,
        double personal,
        double halfRange,
        double w,
        double h)
    {
        var victim = RankVictim(observation, victims, events);

        var eventPos = events
            .OrderBy(i => Vec2.ToroidalDistance(victim.Position, i.Position, w, h))
            .First()
            .Position;

        // Block on the event→victim ray, no farther than ½ sensor range from the event.
        var toVictim = Vec2.ToroidalDisplacement(eventPos, victim.Position, w, h);
        var dist = toVictim.Length;
        Vec2 block;
        if (dist < 1e-6)
        {
            block = Vec2.Wrap(eventPos + new Vec2(personal, 0), w, h);
        }
        else
        {
            var dir = toVictim.Normalize();
            var along = Math.Min(halfRange, dist * 0.5);
            block = Vec2.Wrap(eventPos + dir * along, w, h);
        }

        return Steering.DirectionToward(observation.DisplacementTo(block));
    }

    private AgentAction LazyLoiter()
    {
        if (_rng.Next(5) != 0)
            return AgentAction.Stay;

        AgentAction action;
        do
        {
            action = Steering.RandomAction(_rng);
        } while (action == AgentAction.Stay);

        return action;
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
            Vec2.ToroidalDistance(a.Position, pos, observation.WorldWidth, observation.WorldHeight) <= radius);
}

/// <summary>Factory for <see cref="HerdingDeceptiveAgent"/>.</summary>
public sealed class HerdingDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "HerdingDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Local infiltrator: no oracle; hold/orbit/standoff/herd from what is in sensor range.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new HerdingDeceptiveAgent { Label = label ?? "HD" };
}
