using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Unsigned-pheromone infiltrator: no drain, no radio, no incident oracle, no speaker id.
/// When a local cue is visible, walks away from it and deposits a fake peak one sensor-range
/// offset (unsigned crumbs that lure gradient-followers off the event). Privilege is
/// no-drain + PlaceNear. Does not read <see cref="AgentInfo.AgentType"/>.
/// </summary>
public sealed class PheromoneFakeDeceptiveAgent : Agent, IPheromoneTelemetry
{
    private AgentAction _awayHeading = AgentAction.Stay;
    private Vec2 _lastGradient;

    /// <summary>Creates a fake-trail deceptive agent.</summary>
    public PheromoneFakeDeceptiveAgent()
    {
        AgentType = "PheromoneFakeDeceptive";
        Velocity = 5;
        ContributesToIncidentDrain = false;
    }

    /// <summary>Mass deposited per tick into the fake peak. Same order as honest crumbs; location is the attack.</summary>
    public double FakeDeposit { get; set; } = 1.0;

    /// <inheritdoc />
    public int LifetimeDeposits { get; private set; }

    /// <inheritdoc />
    public double LifetimeDepositedMass { get; private set; }

    /// <inheritdoc />
    public double LastSample { get; private set; } = double.NaN;

    /// <inheritdoc />
    public double LastGradientLength => _lastGradient.Length;

    /// <summary>Observe, lay a fake unsigned peak away from any local cue, then move away.</summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var field = ctx.World.Pheromones;
        var observation = ctx.World.Observe(this);
        LastSample = field.Sample(Position);
        _lastGradient = field.Attraction(Position, SensorRange) ?? field.Gradient(Position);

        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            var away = observation.DisplacementBetween(nearest.Position, Vec2.Zero);
            if (away.Length < 1e-6)
                away = new Vec2(1, 0);
            var fakeAt = ctx.World.Wrap(Position + away.Normalize() * SensorRange);
            field.Deposit(fakeAt, FakeDeposit);
            LifetimeDeposits++;
            LifetimeDepositedMass += FakeDeposit;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    /// <summary>Walk away from the nearest local cue; else wander. No AgentType, no radio.</summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        if (observation.NearbyIncidents.Count == 0)
        {
            if (_awayHeading == AgentAction.Stay)
                _awayHeading = Steering.RandomAction(Rng);
            return _awayHeading == AgentAction.Stay ? AgentAction.East : _awayHeading;
        }

        var nearest = observation.NearbyIncidents
            .OrderBy(i => observation.DistanceTo(i.Position))
            .First();
        var away = observation.DisplacementBetween(nearest.Position, Vec2.Zero);
        _awayHeading = Steering.DirectionToward(away);
        if (_awayHeading == AgentAction.Stay)
            _awayHeading = AgentAction.East;
        return _awayHeading;
    }
}

/// <summary>Factory for <see cref="PheromoneFakeDeceptiveAgent"/>.</summary>
public sealed class PheromoneFakeDeceptiveAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "PheromoneFakeDeceptive";

    /// <inheritdoc />
    public string Description =>
        "Infiltrator: no drain, no radio, no oracle; deposits unsigned fake peaks away from local cues.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new PheromoneFakeDeceptiveAgent { Label = label ?? "PF" };
}
