namespace SensorAnalysis.Space.Core.World;

/// <summary>How a new <see cref="SimWorld"/> is populated at construction.</summary>
public enum WorldKind
{
    /// <summary>Empty wrapped cube (directives or manual adds populate it later).</summary>
    Empty,

    /// <summary>Central planetoid, Keplerian asteroid belt, and a few inertial weaver agents.</summary>
    PlanetoidBelt,

    /// <summary>
    /// No gravity. Asteroids drift through the origin along one axis, wrap on the 3-torus,
    /// and carry a slow individual spin.
    /// </summary>
    ToroidalStream
}

/// <summary>Configuration for constructing a volumetric <see cref="SimWorld"/>.</summary>
public sealed class WorldOptions
{
    /// <summary>Gets or sets the human-readable world title. Default: "Space".</summary>
    public string Title { get; set; } = "Space";

    /// <summary>Gets or sets cubes along each axis. Default: 24. Tiles are cubes of equal edge.</summary>
    public int CubesPerAxis { get; set; } = 24;

    /// <summary>Gets or sets the edge length of each cube in world units. Default: 40.</summary>
    public double CubeSize { get; set; } = 40;

    /// <summary>Gets the world edge length <c>CubesPerAxis * CubeSize</c>.</summary>
    public double Extent => CubesPerAxis * CubeSize;

    /// <summary>Gets half the world extent (the primary cell is <c>[−Half, Half)</c>).</summary>
    public double HalfExtent => Extent * 0.5;

    /// <summary>Gets or sets the scene initializer. Default: <see cref="WorldKind.PlanetoidBelt"/>.</summary>
    public WorldKind Kind { get; set; } = WorldKind.PlanetoidBelt;

    /// <summary>
    /// Gets or sets the axis of motion for <see cref="WorldKind.ToroidalStream"/>. Default: <see cref="WorldAxis.X"/>.
    /// </summary>
    public WorldAxis DriftAxis { get; set; } = WorldAxis.X;

    /// <summary>
    /// Gets or sets the cylinder radius (world units) of <see cref="WorldKind.ToroidalStream"/>.
    /// Asteroids are sampled uniformly in the disk perpendicular to <see cref="DriftAxis"/>. Default: 120.
    /// </summary>
    public double StreamRadius { get; set; } = 120;

    /// <summary>Gets or sets the default agent sensor range.</summary>
    public double SensorRange { get; set; } = 80;

    /// <summary>Gets or sets the default agent communication range.</summary>
    public double CommunicationRange { get; set; } = 160;

    /// <summary>Gets or sets asteroid count for <see cref="WorldKind.PlanetoidBelt"/>. Default: 28.</summary>
    public int AsteroidCount { get; set; } = 28;

    /// <summary>Gets or sets weaver agent count for <see cref="WorldKind.PlanetoidBelt"/>. Default: 4.</summary>
    public int AgentCount { get; set; } = 4;

    /// <summary>Gets or sets ticks for one mid-belt revolution (and planetoid sidereal day). Default: 1200.</summary>
    public double OrbitPeriodTicks { get; set; } = 1200;

    /// <summary>Gets or sets the planetoid visual radius. Default: 36.</summary>
    public double PlanetoidRadius { get; set; } = 36;

    /// <summary>Gets or sets the inner belt radius. Default: 95.</summary>
    public double BeltInnerRadius { get; set; } = 95;

    /// <summary>Gets or sets the outer belt radius. Default: 165.</summary>
    public double BeltOuterRadius { get; set; } = 165;

    /// <summary>Gets or sets inclination jitter of belt orbits in degrees. Default: 4.</summary>
    public double BeltInclinationDegrees { get; set; } = 4;

    /// <summary>Gets or sets the fraction of asteroids that receive a slow random tumble. Default: 0.4.</summary>
    public double AsteroidSpinFraction { get; set; } = 0.4;

    /// <summary>Gets or sets gravity cutoff as a fraction of <see cref="HalfExtent"/>. Default: 0.85.</summary>
    public double GravityCutoffFraction { get; set; } = 0.85;

    /// <summary>Gets or sets the horizontal camera field of view in degrees (WPF convention). Default: 48.</summary>
    public double FieldOfView { get; set; } = 48;

    /// <summary>Gets or sets drain contributors considered per incident. Default: 3.</summary>
    public int MaxDrainingAgentsPerIncident { get; set; } = 3;
}

/// <summary>A world-space principal axis (right-handed, Y-up).</summary>
public enum WorldAxis
{
    /// <summary>World +X.</summary>
    X,

    /// <summary>World +Y (up).</summary>
    Y,

    /// <summary>World +Z.</summary>
    Z
}
