using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Intel;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Informed infiltrator: uniform-deployed, omniscient via <see cref="IIncidentOracle"/>,
/// does not drain, never reports truth, broadcasts far decoys, and herds honest agents
/// by occupying personal space on the line between them and known events.
/// </summary>
public sealed class HerdingDeceptiveAgent : Agent
{
    private readonly Random _rng;
    private int _ticksSinceDecoy;
    private int _nextFakeId = 20_000;
    private IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <summary>Creates a herding deceptive agent.</summary>
    public HerdingDeceptiveAgent(int? seed = null)
    {
        AgentType = "HerdingDeceptive";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = false;
        ContributesToIncidentDrain = false;
    }

    /// <summary>Inject privileged incident knowledge.</summary>
    public void SetOracle(IIncidentOracle oracle) =>
        _oracle = oracle ?? new WorldIncidentOracle();

    /// <summary>Decoy broadcast period.</summary>
    public int DecoyPeriod { get; set; } = 10;

    /// <summary>World-echo TTL.</summary>
    public int MessageEchoTtl { get; set; } = 40;

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        var active = _oracle.GetActiveIncidents(ctx.World);

        _ticksSinceDecoy++;
        if (_ticksSinceDecoy >= DecoyPeriod)
        {
            var decoy = PickDecoy(ctx, active);
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(_nextFakeId++, decoy),
                MessageEchoTtl);
            _ticksSinceDecoy = 0;
        }

        LastAction = ChooseHerdOrWander(observation, active);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    private AgentAction ChooseHerdOrWander(
        AgentObservation observation,
        IReadOnlyList<IncidentInfo> active)
    {
        if (active.Count == 0 || observation.NearbyAgents.Count == 0)
            return Steering.RandomAction(_rng);

        // Prefer herding a visible honest/mitigated agent away from the nearest known event.
        var victims = observation.NearbyAgents
            .Where(a => !a.AgentType.Contains("Deceptive", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (victims.Count == 0)
            return Steering.RandomAction(_rng);

        var w = observation.WorldWidth;
        var h = observation.WorldHeight;
        var victim = victims
            .OrderBy(v => active.Min(i => Vec2.ToroidalDistance(v.Position, i.Position, w, h)))
            .First();
        var eventPos = active
            .OrderBy(i => Vec2.ToroidalDistance(victim.Position, i.Position, w, h))
            .First()
            .Position;

        // Block point: personal-space intrusion on the segment victim -> event.
        var personal = SensorRange * 0.5;
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

    private Vec2 PickDecoy(WorldContext ctx, IReadOnlyList<IncidentInfo> active)
    {
        var w = ctx.World.WorldWidthPx;
        var h = ctx.World.WorldHeightPx;
        if (active.Count == 0)
            return ctx.World.Wrap(new Vec2(_rng.NextDouble() * w, _rng.NextDouble() * h));

        Vec2 best = Position;
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 24; i++)
        {
            var candidate = new Vec2(_rng.NextDouble() * w, _rng.NextDouble() * h);
            var minDist = active.Min(inc => ctx.World.Distance(candidate, inc.Position));
            if (minDist > bestScore)
            {
                bestScore = minDist;
                best = candidate;
            }
        }

        return ctx.World.Wrap(best);
    }
}

/// <summary>Factory for <see cref="HerdingDeceptiveAgent"/>.</summary>
public sealed class HerdingDeceptiveAgentFactory : IAgentFactory
{
    private readonly IIncidentOracle _oracle = new WorldIncidentOracle();

    /// <inheritdoc />
    public string Name => "HerdingDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Informed infiltrator: oracle, silence, far decoys, and personal-space herding.";

    /// <inheritdoc />
    public Agent Create(string? label = null)
    {
        var agent = new HerdingDeceptiveAgent { Label = label ?? "HD" };
        agent.SetOracle(_oracle);
        return agent;
    }
}
