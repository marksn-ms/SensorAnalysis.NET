namespace SensorAnalysis.Space.Core.Domain;

/// <summary>A drainable event that may be free-floating or attached to a parent body's local frame.</summary>
public class Incident : WorldObject
{
    /// <summary>Creates a small tetrahedral incident.</summary>
    public Incident()
    {
        ObjectType = "Incident";
        Visual = VisualKind.Tetrahedron;
        HasInertia = false;
        VisualRadius = 1.6;
        ShieldRadius = 1.6;
    }

    /// <summary>Gets or sets remaining intensity. Depleted when ≤ 0.</summary>
    public double Intensity { get; set; } = 1;

    /// <summary>Gets a value indicating whether this incident has been cleared.</summary>
    public bool IsDepleted => Intensity <= 0;

    /// <summary>Gets or sets drain per covering agent per tick.</summary>
    public double DrainPerAgentPerTick { get; set; } = 0.01;

    /// <summary>Gets or sets the type name used for catalogs.</summary>
    public string IncidentType { get; init; } = "Incident";

    /// <summary>Gets or sets the parent body or agent id for surface-attached incidents, if any.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the offset in the parent's body frame (Right, Up, Forward).</summary>
    public Vec3 LocalOffset { get; set; }
}
