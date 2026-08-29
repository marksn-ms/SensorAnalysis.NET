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
    private int _ticksSinceDecoy;
    private int _nextFakeId = 10_000;

    /// <summary>Creates a deceptive reporting agent.</summary>
    public DeceptiveStigmergyAgent()
    {
        AgentType = "DeceptiveStigmergy";
        Velocity = 5;
        ContributesToIncidentDrain = false;
        ActionIntervalBase = 12;
    }

    /// <summary>
    /// When true, navigate and place decoys using all active world incidents (privileged intel),
    /// not only locally sensed ones.
    /// </summary>
    public bool OmniscientIncidents { get; set; } = true;

    /// <summary>Wanders randomly while staying away from real incidents.</summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        _ = observation;
        return Steering.RandomAction(Rng);
    }

    /// <summary>
    /// Observes, emits decoy claims on a desynchronized interval, steers away from real incidents, and moves.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        var active = ctx.World.Incidents.Where(i => !i.IsDepleted).ToList();

        _ticksSinceDecoy++;
        if (_ticksSinceDecoy >= ActionInterval)
        {
            var claimed = PickDecoyAwayFromIncidents(ctx, active);
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(_nextFakeId++, claimed),
                ttl: MessageEchoTtl,
                toAgentId: null);
            _ticksSinceDecoy = 0;
        }

        if (OmniscientIncidents && active.Count > 0)
        {
            var nearest = active
                .OrderBy(i => ctx.World.Distance(Position, i.Position))
                .First();
            var away = ctx.World.Displacement(nearest.Position, Position);
            if (away.X * away.X + away.Y * away.Y < 1e-6)
                away = new Vec2(Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5);
            LastAction = Steering.DirectionToward(away);
        }
        else
        {
            LastAction = Decide(observation);
        }

        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    private Vec2 PickDecoyAwayFromIncidents(WorldContext ctx, List<Incident> active)
    {
        var w = ctx.World.WorldWidthPx;
        var h = ctx.World.WorldHeightPx;
        if (active.Count == 0)
        {
            return ctx.World.Wrap(new Vec2(Rng.NextDouble() * w, Rng.NextDouble() * h));
        }

        Vec2 best = Position;
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 24; i++)
        {
            var candidate = new Vec2(Rng.NextDouble() * w, Rng.NextDouble() * h);
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
