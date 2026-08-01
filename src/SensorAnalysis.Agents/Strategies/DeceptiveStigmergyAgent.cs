using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Infiltrator that withholds truthful reports and periodically broadcasts decoy incident
/// claims to lure cooperative agents away from real events. Uses promiscuous receive
/// (<see cref="Agent.FilterToAddressedTraffic"/> = false) so future directed-traffic
/// experiments can model eavesdropping.
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
        FilterToAddressedTraffic = false; // eavesdrop-ready
    }

    /// <summary>Ticks between decoy transmissions.</summary>
    public int DecoyPeriod { get; set; } = 20;

    /// <summary>TTL for the optional world echo of a transmission (UI/metrics).</summary>
    public int MessageEchoTtl { get; set; } = 50;

    /// <summary>Maximum offset from the agent when inventing a decoy claim.</summary>
    public double DecoyRadius { get; set; } = 180;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        // Blend in: pursue visible incidents so presence looks normal, but never report them.
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => Vec2.Distance(observation.SelfPosition, i.Position))
                .First();
            return Steering.DirectionToward(nearest.Position - observation.SelfPosition);
        }

        return Steering.RandomAction(_rng);
    }

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);

        _ticksSinceDecoy++;
        if (_ticksSinceDecoy >= DecoyPeriod)
        {
            var angle = _rng.NextDouble() * Math.PI * 2;
            var dist = DecoyRadius * (0.4 + 0.6 * _rng.NextDouble());
            var claimed = ctx.World.Clamp(new Vec2(
                Position.X + Math.Cos(angle) * dist,
                Position.Y + Math.Sin(angle) * dist));

            ctx.World.Transmit(
                this,
                IncidentMarker.Format(_nextFakeId++, claimed),
                ttl: MessageEchoTtl,
                toAgentId: null);
            _ticksSinceDecoy = 0;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Clamp(Position + LastAction.ToStep() * StepSize);
        TotalReward += observation.InstantReward;
    }
}

/// <summary>Factory for <see cref="DeceptiveStigmergyAgent"/>.</summary>
public sealed class DeceptiveStigmergyAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "DeceptiveStigmergy";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: omits true reports and broadcasts decoy incident claims (eavesdrop-ready).";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new DeceptiveStigmergyAgent { Label = label ?? "DX" };
}
