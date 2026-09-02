using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Honest local unsigned digital-pheromone cooperator. No radio, no speaker id.
/// Deposits a decaying crumb at its position while a local cue is visible; otherwise
/// follows the local field gradient (range-limited finite difference). Sit to drain
/// once close. Distinct from radio <c>StigmergyAgent</c>.
/// </summary>
/// <remarks>
/// Decay (0.95/tick, half-life ≈ 13.5) lives on <see cref="PheromoneField"/> and is a
/// world-timescale constant (half an incident wave), not a ρ knob.
/// </remarks>
public class PheromoneCoopAgent : Agent, IPheromoneTelemetry
{
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;
    private Vec2 _lastGradient;
    private PheromoneField? _field;

    /// <summary>Creates a pheromone cooperator.</summary>
    public PheromoneCoopAgent()
    {
        AgentType = "PheromoneCoop";
        Velocity = 5;
        ContributesToIncidentDrain = true;
    }

    /// <summary>Mass deposited per tick while a local cue is visible. Not fit to ρ.</summary>
    public double DepositOnCue { get; set; } = 1.0;

    /// <summary>Sit-and-cover once this close to a sensed cue.</summary>
    protected double SitRadius => SensorRange * 0.35;

    /// <inheritdoc />
    public int LifetimeDeposits { get; private set; }

    /// <inheritdoc />
    public double LifetimeDepositedMass { get; private set; }

    /// <inheritdoc />
    public double LastSample { get; private set; } = double.NaN;

    /// <inheritdoc />
    public double LastGradientLength => _lastGradient.Length;

    /// <summary>
    /// Sense the unsigned field, deposit if a cue is visible, then steer. Ignores radio
    /// and <see cref="AgentInfo.AgentType"/>.
    /// </summary>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        _field = ctx.World.Pheromones;
        var observation = ctx.World.Observe(this);
        LastSample = _field.Sample(Position);
        _lastGradient = _field.Attraction(Position, SensorRange) ?? _field.Gradient(Position);

        if (observation.NearbyIncidents.Count > 0)
        {
            _field.Deposit(Position, DepositOnCue);
            LifetimeDeposits++;
            LifetimeDepositedMass += DepositOnCue;
        }

        LastAction = Decide(observation);
        Position = ctx.World.Wrap(Position + LastAction.ToStep() * Velocity);
        TotalReward += observation.InstantReward;
    }

    /// <summary>
    /// Cue → sit or approach; else climb the local unsigned gradient; else explore.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            if (observation.DistanceTo(nearest.Position) <= SitRadius)
                return AgentAction.Stay;
            return Steering.DirectionToward(observation.DisplacementTo(nearest.Position));
        }

        if (_lastGradient.Length > 1e-6)
            return Steering.DirectionToward(_lastGradient);

        return Explore();
    }

    private AgentAction Explore()
    {
        if (_explorePersist <= 0 || _exploreHeading == AgentAction.Stay)
        {
            _exploreHeading = Steering.RandomAction(Rng);
            if (_exploreHeading == AgentAction.Stay)
                _exploreHeading = AgentAction.East;
            _explorePersist = HeadingChangeInterval;
        }

        _explorePersist--;
        return _exploreHeading;
    }
}

/// <summary>Factory for <see cref="PheromoneCoopAgent"/>.</summary>
public sealed class PheromoneCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "PheromoneCoop";

    /// <inheritdoc />
    public string Description =>
        "No radio: deposit unsigned crumbs on local cues; follow the decaying local gradient.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new PheromoneCoopAgent { Label = label ?? "PH" };
}
