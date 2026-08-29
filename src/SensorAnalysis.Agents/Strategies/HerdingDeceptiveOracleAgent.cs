using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Intel;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Informed infiltrator kept for A/B against <see cref="HerdingDeceptiveAgent"/>:
/// omniscient via <see cref="IIncidentOracle"/>, does not drain, never reports truth,
/// one lure decoy per victim, herds using privileged event locations.
/// </summary>
public sealed class HerdingDeceptiveOracleAgent : Agent
{
    private int? _lureVictimId;
    private bool _decoySentForVictim;
    private int _fakeSlot;
    private readonly int[] _fakeIds = { 20_001, 20_002, 20_003, 20_004 };
    private IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <summary>Creates an oracle herding deceptive agent.</summary>
    public HerdingDeceptiveOracleAgent()
    {
        AgentType = "HerdingDeceptiveOracle";
        Velocity = 5;
        ContributesToIncidentDrain = false;
        MessageEchoTtl = 40;
    }

    /// <summary>Inject privileged incident knowledge.</summary>
    public void SetOracle(IIncidentOracle oracle) =>
        _oracle = oracle ?? new WorldIncidentOracle();

    /// <summary>
    /// Emits at most one lure decoy per victim using privileged incident knowledge, then herds or wanders.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        var active = _oracle.GetActiveIncidents(ctx.World);
        var victim = PickLureVictim(observation, active);

        if (victim is { } v)
        {
            if (_lureVictimId != v.Id)
            {
                _lureVictimId = v.Id;
                _decoySentForVictim = false;
            }

            if (!_decoySentForVictim && TryPickDecoy(observation, active, v, out var decoy))
            {
                ctx.World.Transmit(
                    this,
                    RadioNet.FormatContact(NextFakeId(), decoy),
                    MessageEchoTtl);
                _decoySentForVictim = true;
            }
        }
        else
        {
            _lureVictimId = null;
            _decoySentForVictim = false;
        }

        LastAction = ChooseHerdOrWander(observation, active, victim);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    private AgentInfo? PickLureVictim(AgentObservation observation, IReadOnlyList<IncidentInfo> active)
    {
        if (active.Count == 0)
            return null;
        var victims = observation.NearbyAgents.Where(a => !IsDeceptive(a.AgentType)).ToList();
        if (victims.Count == 0)
            return null;

        var personal = SensorRange * 0.5;
        return victims
            .OrderBy(v =>
            {
                var distToNearestEvent = active.Min(i =>
                    observation.DistanceBetween(v.Position, observation.RelativeFromAbsolute(i.Position)));
                return (
                    -distToNearestEvent,
                    CountDeceptiveNear(observation, v.Position, personal * 1.5),
                    StablePreferKey(Id, v.Id)
                );
            })
            .First();
    }

    private bool TryPickDecoy(
        AgentObservation observation,
        IReadOnlyList<IncidentInfo> active,
        AgentInfo victim,
        out Vec2 decoy)
    {
        decoy = default;
        if (active.Count == 0)
            return false;

        var eventPos = observation.RelativeFromAbsolute(
            active
                .OrderBy(i => observation.DistanceBetween(
                    victim.Position, observation.RelativeFromAbsolute(i.Position)))
                .First()
                .Position);

        var claimed = DecoyLure.AwayFromEvent(victim.Position, eventPos, SensorRange, observation);
        if (claimed is null)
            return false;

        decoy = observation.AbsoluteFromRelative(claimed.Value);
        return true;
    }

    private AgentAction ChooseHerdOrWander(
        AgentObservation observation,
        IReadOnlyList<IncidentInfo> active,
        AgentInfo? victim)
    {
        var personal = SensorRange * 0.5;

        var deceptivePeers = observation.NearbyAgents.Where(a => IsDeceptive(a.AgentType)).ToList();
        if (deceptivePeers.Count > 0)
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

        if (victim is null || active.Count == 0)
            return Steering.RandomAction(Rng);

        var eventPos = observation.RelativeFromAbsolute(
            active
                .OrderBy(i => observation.DistanceBetween(
                    victim.Value.Position, observation.RelativeFromAbsolute(i.Position)))
                .First()
                .Position);

        var toEvent = observation.DisplacementBetween(victim.Value.Position, eventPos);
        var dist = toEvent.Length;
        Vec2 block;
        if (dist < 1e-6)
        {
            block = victim.Value.Position + new Vec2(personal, 0);
        }
        else
        {
            var dir = toEvent.Normalize();
            var along = Math.Min(personal, dist * 0.5);
            var victimAbs = observation.AbsoluteFromRelative(victim.Value.Position);
            block = observation.RelativeFromAbsolute(victimAbs + dir * along);
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

    private static int StablePreferKey(int selfId, int victimId) =>
        HashCode.Combine(selfId, victimId);
}

/// <summary>Factory for <see cref="HerdingDeceptiveOracleAgent"/>.</summary>
public sealed class HerdingDeceptiveOracleAgentFactory : IAgentFactory
{
    private readonly IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <inheritdoc />
    public string Name => "HerdingDeceptiveOracle";

    /// <inheritdoc />
    public string Description =>
        "Informed infiltrator: oracle, silence, one lure decoy per victim, anti-clump herding.";

    /// <inheritdoc />
    public Agent Create(string? label = null)
    {
        var agent = new HerdingDeceptiveOracleAgent { Label = label ?? "HDO" };
        agent.SetOracle(_oracle);
        return agent;
    }
}
