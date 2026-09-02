using SensorAnalysis.Space.Core.World;

namespace SensorAnalysis.Space.Core.Domain;

/// <summary>Base for anything that lives in the volumetric world and can be drawn or selected.</summary>
public abstract class WorldObject
{
    private static int _nextId;

    /// <summary>Initializes a new instance and assigns a unique identifier.</summary>
    protected WorldObject()
    {
        Id = Interlocked.Increment(ref _nextId);
    }

    /// <summary>Gets the unique identifier for this world object.</summary>
    public int Id { get; }

    /// <summary>Gets or sets the position of this object's center in world coordinates.</summary>
    public Vec3 Position { get; set; }

    /// <summary>Gets or sets linear velocity in world units per tick.</summary>
    public Vec3 Velocity { get; set; }

    /// <summary>Gets or sets the unit forward (look-at) axis in world space.</summary>
    public Vec3 Forward { get; set; } = Vec3.UnitZ;

    /// <summary>Gets or sets the unit up axis in world space.</summary>
    public Vec3 Up { get; set; } = Vec3.UnitY;

    /// <summary>Gets the unit right axis <c>Up × Forward</c>.</summary>
    public Vec3 Right => Vec3.Cross(Up, Forward).Normalize();

    /// <summary>Gets or sets angular velocity as an axis-angle vector in radians per tick.</summary>
    public Vec3 AngularVelocity { get; set; }

    /// <summary>Gets or sets inertial mass. Default: 1. Does not affect gravitational acceleration.</summary>
    public double Mass { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets a value indicating whether linear velocity is integrated.
    /// When false, an agent's decision replaces velocity each tick (kinematic).
    /// </summary>
    public bool HasInertia { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this object is an infinite-mass kinematic anchor
    /// (position is not integrated; spin still applies).
    /// </summary>
    public bool IsAnchored { get; set; }

    /// <summary>Gets or sets linear drag per tick. Default: 0 (vacuum). Non-zero spirals orbits inward.</summary>
    public double LinearDrag { get; set; }

    /// <summary>Gets or sets angular damping per tick applied as a multiplicative decay on <see cref="AngularVelocity"/>.</summary>
    public double AngularDamping { get; set; }

    /// <summary>Gets or sets the visual bounding radius used for rendering scale.</summary>
    public double VisualRadius { get; set; } = 1;

    /// <summary>Gets or sets the collision-shield radius. Broad-phase uses toroidal distance vs the sum of shields.</summary>
    public double ShieldRadius { get; set; } = 1;

    /// <summary>Gets or sets the coefficient of restitution in <c>[0, 1]</c> used when shields overlap.</summary>
    public double Restitution { get; set; } = 0.55;

    /// <summary>Gets or sets the default geometric primitive for this object.</summary>
    public VisualKind Visual { get; set; } = VisualKind.Sphere;

    /// <summary>Gets or sets the display label.</summary>
    public string Label { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether this object is selected in the UI.</summary>
    public bool IsSelected { get; set; }

    /// <summary>Gets the type name used in selection summaries and catalogs.</summary>
    public string ObjectType { get; init; } = "Object";

    /// <summary>Called once per simulation tick after physics, before coverage bookkeeping.</summary>
    /// <param name="ctx">World state for the current tick.</param>
    public virtual void Tick(WorldContext ctx)
    {
    }

    /// <summary>Applies <see cref="AngularVelocity"/> to <see cref="Forward"/> and <see cref="Up"/> and re-orthonormalizes.</summary>
    public void IntegrateOrientation()
    {
        if (AngularVelocity.LengthSquared < 1e-18)
            return;

        Forward = Vec3.Rotate(Forward, AngularVelocity).Normalize();
        Up = Vec3.Rotate(Up, AngularVelocity);
        var frame = Vec3.OrthonormalFrame(Forward, Up);
        Forward = frame.Forward;
        Up = frame.Up;

        if (AngularDamping > 0)
            AngularVelocity *= Math.Max(0, 1 - AngularDamping);
    }

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name}#{Id}";
}
