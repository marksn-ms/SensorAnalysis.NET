namespace SensorAnalysis.Space.Core.Domain;

/// <summary>Default geometric primitive used to visualize a <see cref="WorldObject"/>.</summary>
public enum VisualKind
{
    /// <summary>UV sphere (planetoid, default body).</summary>
    Sphere,

    /// <summary>Axis-aligned cube in local space (including a Borg cube).</summary>
    Cube,

    /// <summary>Cone along local +Z (agents; +Z is forward).</summary>
    Cone,

    /// <summary>Regular tetrahedron.</summary>
    Tetrahedron,

    /// <summary>Icosphere with optional vertex displacement (asteroids).</summary>
    Icosphere
}
