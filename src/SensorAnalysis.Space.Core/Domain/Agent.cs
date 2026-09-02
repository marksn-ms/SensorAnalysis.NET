using SensorAnalysis.Space.Core.World;

namespace SensorAnalysis.Space.Core.Domain;

/// <summary>A mobile sensor agent in volumetric space.</summary>
public class Agent : WorldObject
{
    /// <summary>Creates an agent with cone visuals and a default sensor range.</summary>
    public Agent()
    {
        ObjectType = "Agent";
        Visual = VisualKind.Cone;
        HasInertia = true;
        VisualRadius = 2.2;
        ShieldRadius = 2.5;
    }

    /// <summary>Gets or sets the omnidirectional sensing radius in world units.</summary>
    public double SensorRange { get; set; } = 80;

    /// <summary>Gets or sets radio range. Defaults to <see cref="SensorRange"/> when null.</summary>
    public double? CommRange { get; set; }

    /// <summary>Gets the effective transmit/receive range.</summary>
    public double EffectiveCommRange => CommRange ?? SensorRange;

    /// <summary>Gets or sets remaining Δv budget. <see cref="double.PositiveInfinity"/> means unlimited.</summary>
    public double Fuel { get; set; } = double.PositiveInfinity;

    /// <summary>Gets or sets the maximum |Δv| this agent may spend on a single tick.</summary>
    public double MaxDeltaVPerTick { get; set; } = 0.08;

    /// <summary>Gets the action performed on the most recent tick.</summary>
    public AgentAction LastAction { get; protected set; } = AgentAction.Coast;

    /// <summary>Gets or sets ticks between random-walk heading (thrust) resamples.</summary>
    public virtual int HeadingChangeInterval { get; set; } = 24;

    /// <summary>Gets or sets remaining ticks on the current random-walk thrust.</summary>
    public int HeadingTicksRemaining { get; set; }

    /// <summary>World RNG for the current tick; set by <see cref="BindContext"/>.</summary>
    protected Random Rng { get; private set; } = null!;

    /// <summary>Gets or sets the strategy name.</summary>
    public string AgentType { get; init; } = "Agent";

    /// <summary>When true (default), this agent drains incident intensity while in range.</summary>
    public bool ContributesToIncidentDrain { get; set; } = true;

    /// <summary>Override to choose thrust and look-at from local observations. Default: coast.</summary>
    /// <param name="observation">Sensed neighbors and incidents.</param>
    /// <returns>The action for this tick.</returns>
    public virtual AgentAction Decide(AgentObservation observation) => AgentAction.Coast;

    /// <summary>Binds the world's RNG for this tick.</summary>
    /// <param name="ctx">Current world context.</param>
    protected void BindContext(WorldContext ctx) => Rng = ctx.Rng;

    /// <summary>Observes, decides, and applies clamped Δv / look-at. Translation is integrated by the world.</summary>
    /// <param name="ctx">World context.</param>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        LastAction = Decide(observation);
        ApplyAction(LastAction);
    }

    /// <summary>Applies fuel- and throttle-clamped Δv and optional look-at.</summary>
    /// <param name="action">Decision for this tick.</param>
    protected void ApplyAction(AgentAction action)
    {
        var delta = action.DeltaV;
        var mag = delta.Length;
        if (mag > 1e-12)
        {
            var cap = MaxDeltaVPerTick;
            if (!double.IsPositiveInfinity(Fuel))
                cap = Math.Min(cap, Math.Max(0, Fuel));

            if (mag > cap)
                delta *= cap / mag;

            var spent = delta.Length;
            if (!double.IsPositiveInfinity(Fuel))
                Fuel = Math.Max(0, Fuel - spent);

            if (HasInertia)
                Velocity += delta;
            else
                Velocity = delta;
        }
        else if (!HasInertia)
        {
            Velocity = Vec3.Zero;
        }

        if (action.LookAt is { } look && look.LengthSquared > 1e-12)
        {
            var frame = Vec3.OrthonormalFrame(look, Up);
            Forward = frame.Forward;
            Up = frame.Up;
        }
        else if (Velocity.LengthSquared > 1e-12)
        {
            var frame = Vec3.OrthonormalFrame(Velocity, Up);
            Forward = frame.Forward;
            Up = frame.Up;
        }
    }
}
