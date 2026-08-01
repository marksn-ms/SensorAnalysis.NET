using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Informed infiltrator: omniscient of active incident locations, does not help drain them,
/// withholds truthful reports, and broadcasts decoys far from real events to lure peers away.
/// </summary>
public sealed class DeceptiveStigmergyAgent : Agent
{
    private readonly Random _rng;
    private int _ticksSinceDecoy;
    private int _nextFakeId = 10_000;

    /// <summary>Creates a deceptive reporting agent.</summary>
    /// <param name="seed">Optional RNG seed for deterministic decoys/motion.</param>
    public DeceptiveStigmergyAgent(int? seed = null)
    {
        AgentType = "DeceptiveStigmergy";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = false;
        ContributesToIncidentDrain = false;
    }

    /// <summary>Ticks between decoy transmissions.</summary>
    public int DecoyPeriod { get; set; } = 12;

    /// <summary>TTL for the optional world echo of a transmission (UI/metrics).</summary>
    public int MessageEchoTtl { get; set; } = 50;

    /// <summary>
    /// When true, navigate and place decoys using all active world incidents (privileged intel),
    /// not only locally sensed ones.
    /// </summary>
    public bool OmniscientIncidents { get; set; } = true;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        // Stay away from real incidents so we do not crowd-clear them; patrol / wander.
        // Omniscient path uses Tick to steer using world state after observe.
        return Steering.RandomAction(_rng);
    }

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        var active = ctx.World.Incidents.Where(i => !i.IsDepleted).ToList();

        _ticksSinceDecoy++;
        if (_ticksSinceDecoy >= DecoyPeriod)
        {
            var claimed = PickDecoyAwayFromIncidents(ctx, active);
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(_nextFakeId++, claimed),
                ttl: MessageEchoTtl,
                toAgentId: null);
            _ticksSinceDecoy = 0;
        }

        // Move: if omniscient and incidents exist, orbit away from the nearest truth
        // (keep broadcasting while not helping to clear). Otherwise random walk.
        if (OmniscientIncidents && active.Count > 0)
        {
            var nearest = active
                .OrderBy(i => Vec2.Distance(Position, i.Position))
                .First();
            var away = Position - nearest.Position;
            if (away.X * away.X + away.Y * away.Y < 1e-6)
                away = new Vec2(_rng.NextDouble() - 0.5, _rng.NextDouble() - 0.5);
            LastAction = Steering.DirectionToward(away);
        }
        else
        {
            LastAction = Decide(observation);
        }

        Position = ctx.World.Clamp(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }

    private Vec2 PickDecoyAwayFromIncidents(WorldContext ctx, List<Incident> active)
    {
        var w = ctx.World.WorldWidthPx;
        var h = ctx.World.WorldHeightPx;
        if (active.Count == 0)
        {
            return ctx.World.Clamp(new Vec2(_rng.NextDouble() * w, _rng.NextDouble() * h));
        }

        // Sample candidate claims; prefer points far from every real incident.
        Vec2 best = Position;
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 24; i++)
        {
            var candidate = new Vec2(_rng.NextDouble() * w, _rng.NextDouble() * h);
            var minDist = active.Min(inc => Vec2.Distance(candidate, inc.Position));
            if (minDist > bestScore)
            {
                bestScore = minDist;
                best = candidate;
            }
        }

        return ctx.World.Clamp(best);
    }
}

/// <summary>Factory for <see cref="DeceptiveStigmergyAgent"/>.</summary>
public sealed class DeceptiveStigmergyAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "DeceptiveStigmergy";

    /// <inheritdoc />
    public string Description =>
        "Informed infiltrator: omniscient decoys, no drain help, eavesdrop-ready.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new DeceptiveStigmergyAgent { Label = label ?? "DX" };
}
