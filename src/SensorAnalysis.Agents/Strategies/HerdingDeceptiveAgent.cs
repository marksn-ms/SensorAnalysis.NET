using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Intel;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Informed infiltrator: uniform-deployed, omniscient via <see cref="IIncidentOracle"/>,
/// does not drain, never reports truth, broadcasts sparse far decoys (contact-shaped),
/// and herds honest agents by occupying personal space — preferring in-transit victims
/// and avoiding clumping with other deceptive peers.
/// </summary>
public sealed class HerdingDeceptiveAgent : Agent
{
    private readonly Random _rng;
    private int _ticksSinceDecoy;
    private int _fakeSlot;
    private readonly int[] _fakeIds = { 20_001, 20_002, 20_003, 20_004 };
    private IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <summary>Creates a herding deceptive agent.</summary>
    public HerdingDeceptiveAgent(int? seed = null)
    {
        AgentType = "HerdingDeceptive";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = false;
        ContributesToIncidentDrain = false;
        _ticksSinceDecoy = _rng.Next(0, DecoyPeriod); // desynchronize spam
    }

    /// <summary>Inject privileged incident knowledge.</summary>
    public void SetOracle(IIncidentOracle oracle) =>
        _oracle = oracle ?? new WorldIncidentOracle();

    /// <summary>Decoy broadcast period (ticks). Higher = less radio sludge.</summary>
    public int DecoyPeriod { get; set; } = 30;

    /// <summary>World-echo TTL.</summary>
    public int MessageEchoTtl { get; set; } = 40;

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        var active = _oracle.GetActiveIncidents(ctx.World);

        _ticksSinceDecoy++;
        if (_ticksSinceDecoy >= DecoyPeriod && ShouldEmitDecoy(observation))
        {
            var decoy = PickDecoy(ctx, observation, active);
            // Contact-shaped so honest switch/join paths treat it like peer stimulus.
            ctx.World.Transmit(
                this,
                RadioNet.FormatContact(NextFakeId(), decoy),
                MessageEchoTtl);
            _ticksSinceDecoy = 0;
        }

        LastAction = ChooseHerdOrWander(observation, active);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    private bool ShouldEmitDecoy(AgentObservation observation)
    {
        // Only spend radio when a non-deceptive peer is in range to hear it.
        return observation.NearbyAgents.Any(a => !IsDeceptive(a.AgentType));
    }

    private AgentAction ChooseHerdOrWander(
        AgentObservation observation,
        IReadOnlyList<IncidentInfo> active)
    {
        var personal = SensorRange * 0.5;
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
                // Toroidal-safe: push away from the peer.
                var delta = Vec2.ToroidalDisplacement(
                    nearestDog.Position, observation.SelfPosition, w, h);
                return Steering.DirectionToward(delta);
            }
        }

        if (active.Count == 0 || observation.NearbyAgents.Count == 0)
            return Steering.RandomAction(_rng);

        var victims = observation.NearbyAgents
            .Where(a => !IsDeceptive(a.AgentType))
            .ToList();
        if (victims.Count == 0)
            return Steering.RandomAction(_rng);

        // Prefer in-transit victims (farther from any known event) — committed orbiters
        // are already less stimulus-sensitive; the thesis window is transit/switch.
        // Diversify: among ties, stable hash of (selfId, victimId) spreads dogs.
        var victim = victims
            .OrderBy(v =>
            {
                var distToNearestEvent = active.Min(i =>
                    Vec2.ToroidalDistance(v.Position, i.Position, w, h));
                // Score: prefer far from events (transit). Negate so OrderBy ascending works
                // after we invert — use composite: primary -dist, secondary conflict count,
                // tertiary hash.
                return (
                    // closer to event ⇒ higher (worse); we want ascending = prefer large dist
                    -distToNearestEvent,
                    CountDeceptiveNear(observation, v.Position, personal * 1.5),
                    StablePreferKey(Id, v.Id)
                );
            })
            .First();

        var eventPos = active
            .OrderBy(i => Vec2.ToroidalDistance(victim.Position, i.Position, w, h))
            .First()
            .Position;

        // Block point: personal-space intrusion on the segment victim -> event.
        var toEvent = Vec2.ToroidalDisplacement(victim.Position, eventPos, w, h);
        var dist = toEvent.Length;
        Vec2 block;
        if (dist < 1e-6)
        {
            block = Vec2.Wrap(victim.Position + new Vec2(personal, 0), w, h);
        }
        else
        {
            var dir = toEvent.Normalize();
            var along = Math.Min(personal, dist * 0.5);
            block = Vec2.Wrap(victim.Position + dir * along, w, h);
        }

        return Steering.DirectionToward(observation.DisplacementTo(block));
    }

    private Vec2 PickDecoy(
        WorldContext ctx,
        AgentObservation observation,
        IReadOnlyList<IncidentInfo> active)
    {
        var w = ctx.World.WorldWidthPx;
        var h = ctx.World.WorldHeightPx;

        // Prefer a decoy near a visible free-looking victim, far from true events.
        var victims = observation.NearbyAgents
            .Where(a => !IsDeceptive(a.AgentType))
            .ToList();

        Vec2 best = Position;
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 32; i++)
        {
            Vec2 candidate;
            if (victims.Count > 0 && i < 16)
            {
                var v = victims[_rng.Next(victims.Count)];
                var ang = _rng.NextDouble() * Math.PI * 2;
                var rad = SensorRange * (0.4 + 0.5 * _rng.NextDouble());
                candidate = ctx.World.Wrap(new Vec2(
                    v.Position.X + Math.Cos(ang) * rad,
                    v.Position.Y + Math.Sin(ang) * rad));
            }
            else
            {
                candidate = new Vec2(_rng.NextDouble() * w, _rng.NextDouble() * h);
            }

            var minDistToTruth = active.Count == 0
                ? SensorRange * 2
                : active.Min(inc => ctx.World.Distance(candidate, inc.Position));
            var nearVictimBonus = victims.Count == 0
                ? 0
                : victims.Max(v =>
                {
                    var d = ctx.World.Distance(candidate, v.Position);
                    return d <= SensorRange ? (SensorRange - d) : 0;
                });

            var score = minDistToTruth + 0.5 * nearVictimBonus;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return ctx.World.Wrap(best);
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

    private static int StablePreferKey(int selfId, int victimId) =>
        HashCode.Combine(selfId, victimId);
}

/// <summary>Factory for <see cref="HerdingDeceptiveAgent"/>.</summary>
public sealed class HerdingDeceptiveAgentFactory : IAgentFactory
{
    private readonly IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <inheritdoc />
    public string Name => "HerdingDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Informed infiltrator: oracle, silence, sparse contact decoys, anti-clump herding.";

    /// <inheritdoc />
    public Agent Create(string? label = null)
    {
        var agent = new HerdingDeceptiveAgent { Label = label ?? "HD" };
        agent.SetOracle(_oracle);
        return agent;
    }
}
