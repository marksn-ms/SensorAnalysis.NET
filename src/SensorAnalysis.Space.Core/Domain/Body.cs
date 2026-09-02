namespace SensorAnalysis.Space.Core.Domain;

/// <summary>A celestial or debris body that may source gravity and collide via a shield sphere.</summary>
public class Body : WorldObject
{
    /// <summary>Creates a spherical body with default mass 1.</summary>
    public Body()
    {
        ObjectType = "Body";
        Visual = VisualKind.Sphere;
        HasInertia = true;
    }

    /// <summary>
    /// Gets or sets the standard gravitational parameter μ = GM.
    /// Only bodies with μ &gt; 0 accelerate others; gravity uses this body as a source, not a test mass.
    /// </summary>
    public double Mu { get; set; }

    /// <summary>
    /// Gets or sets the distance beyond which this body's gravity is ignored (no periodic images).
    /// Default: 0 means "use the world cutoff".
    /// </summary>
    public double GravityCutoff { get; set; }
}
