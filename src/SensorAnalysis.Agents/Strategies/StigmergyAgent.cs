using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Leaves environmental markers when it finds an incident, and is attracted to
/// markers left by others. A simple stigmergic / pheromone-style search.
/// </summary>
public sealed class StigmergyAgent : Agent
{
    private readonly Random _rng;
    private int _ticksSinceDrop;

    /// <summary>
    /// Creates a stigmergy agent that drops and follows environmental markers.
    /// </summary>
    /// <param name="seed">Optional RNG seed for deterministic exploration; otherwise non-deterministic.</param>
    public StigmergyAgent(int? seed = null)
    {
        AgentType = "Stigmergy";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
    }

    /// <summary>Minimum ticks between dropping a new marker.</summary>
    public int DropCooldown { get; set; } = 15;

    /// <summary>TTL for markers this agent drops.</summary>
    public int MarkerTtl { get; set; } = 40;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        _ticksSinceDrop++;

        // Prefer nearest incident if any are visible
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(observation.SelfPosition, i.Position))
                .First();
            return DirectionToward(nearest.Position - observation.SelfPosition);
        }

        // Otherwise follow nearest marker from other agents
        var markers = observation.NearbyMessages
            .Where(m => m.Payload.StartsWith("incident:", StringComparison.Ordinal))
            .ToList();
        if (markers.Count > 0)
        {
            var nearest = markers
                .OrderBy(m => Vec2.Distance(observation.SelfPosition, m.Position))
                .First();
            return DirectionToward(nearest.Position - observation.SelfPosition);
        }

        // Explore
        var values = Enum.GetValues<AgentAction>();
        return values[_rng.Next(values.Length)];
    }

    /// <summary>
    /// Drops an environmental marker when an incident is visible and cooldown has elapsed, then runs the base tick.
    /// </summary>
    /// <param name="ctx">World context for the current tick.</param>
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);

        // Drop a marker if we see an incident and cooldown elapsed
        if (observation.NearbyIncidents.Count > 0 && _ticksSinceDrop >= DropCooldown)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(Position, i.Position))
                .First();
            ctx.World.Add(new Message
            {
                Position = Position,
                FromAgentType = AgentType,
                FromAgentId = Id,
                Payload = $"incident:{nearest.Id}",
                Ttl = MarkerTtl
            });
            _ticksSinceDrop = 0;
        }

        base.Tick(ctx);
    }

    private static AgentAction DirectionToward(Vec2 delta)
    {
        var ax = Math.Abs(delta.X);
        var ay = Math.Abs(delta.Y);
        if (ax < 1e-6 && ay < 1e-6) return AgentAction.Stay;

        var sx = Math.Sign(delta.X);
        var sy = Math.Sign(delta.Y);

        if (ax > ay * 2) return sx > 0 ? AgentAction.East : AgentAction.West;
        if (ay > ax * 2) return sy > 0 ? AgentAction.South : AgentAction.North;

        if (sx > 0 && sy > 0) return AgentAction.SouthEast;
        if (sx > 0 && sy < 0) return AgentAction.NorthEast;
        if (sx < 0 && sy > 0) return AgentAction.SouthWest;
        if (sx < 0 && sy < 0) return AgentAction.NorthWest;
        return AgentAction.Stay;
    }
}

/// <summary>Factory for <see cref="StigmergyAgent"/> instances.</summary>
public sealed class StigmergyAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "Stigmergy";

    /// <inheritdoc />
    public string Description => "Drops markers near incidents; follows markers from others.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new StigmergyAgent { Label = label ?? "ST" };
}
