using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Legacy-style cooperative agent that broadcasts incident claims and follows peer reports.
/// Prefer <see cref="CooperativeSearchAgent"/> for new trust experiments.
/// </summary>
public sealed class StigmergyAgent : Agent
{
    private readonly Random _rng;
    private int _ticksSinceReport;

    /// <summary>
    /// Creates an agent that transmits and follows incident report messages.
    /// </summary>
    /// <param name="seed">Optional RNG seed for deterministic exploration; otherwise non-deterministic.</param>
    public StigmergyAgent(int? seed = null)
    {
        AgentType = "Stigmergy";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = true;
    }

    /// <summary>Minimum ticks between report transmissions.</summary>
    public int DropCooldown { get; set; } = 15;

    /// <summary>TTL for the optional world echo of a transmission (UI/metrics).</summary>
    public int MarkerTtl { get; set; } = 40;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(observation.SelfPosition, i.Position))
                .First();
            return Steering.DirectionToward(nearest.Position - observation.SelfPosition);
        }

        var reports = observation.NearbyMessages
            .Select(m =>
            {
                var ok = IncidentMarker.TryParse(m.Payload, out _, out var claimed);
                return (Ok: ok, Claimed: claimed);
            })
            .Where(x => x.Ok)
            .OrderBy(x => Vec2.Distance(observation.SelfPosition, x.Claimed))
            .ToList();

        if (reports.Count > 0)
            return Steering.DirectionToward(reports[0].Claimed - observation.SelfPosition);

        return Steering.RandomAction(_rng);
    }

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        _ticksSinceReport++;

        if (observation.NearbyIncidents.Count > 0 && _ticksSinceReport >= DropCooldown)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(Position, i.Position))
                .First();
            ctx.World.Transmit(
                this,
                IncidentMarker.Format(nearest.Id, nearest.Position),
                ttl: MarkerTtl,
                toAgentId: null);
            _ticksSinceReport = 0;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Clamp(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }
}

/// <summary>Factory for <see cref="StigmergyAgent"/> instances.</summary>
public sealed class StigmergyAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "Stigmergy";

    /// <inheritdoc />
    public string Description => "Broadcasts incident claims; follows peer reports (legacy name).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new StigmergyAgent { Label = label ?? "ST" };
}
