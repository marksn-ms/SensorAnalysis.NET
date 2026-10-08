namespace SensorAnalysis.Hallway;

/// <summary>
/// Inch-scale school corridor. The compact default is a short hall a GUI can draw.
/// A 100-yard hall is <c>HallLength = 3600</c>. A 50-foot hall width is <c>HallWidth = 600</c>.
/// </summary>
public sealed record FloorPlanSpec
{
    /// <summary>Short hall used when a deployer leaves its dimensions at the defaults.</summary>
    public static FloorPlanSpec Compact { get; } = new();

    /// <summary>Hall length along X, in world units. Default: 360 (30 feet).</summary>
    public double HallLength { get; init; } = 360;

    /// <summary>Clear width between the two hall walls. Default: 96 (8 feet).</summary>
    public double HallWidth { get; init; } = 96;

    /// <summary>Depth of classrooms on each side of the hall. Default: 120.</summary>
    public double RoomDepth { get; init; } = 120;

    /// <summary>Distance between door centers along the hall. Default: 180 (15 feet).</summary>
    public double DoorSpacing { get; init; } = 180;

    /// <summary>Width of each doorway gap. Default: 36.</summary>
    public double DoorWidth { get; init; } = 36;

    /// <summary>Full thickness of each wall body. Default: 6.</summary>
    public double WallThickness { get; init; } = 6;

    /// <summary>
    /// Empty torus margin around the building. Must exceed sensor range so a wrapped
    /// copy of a wall is not sensed through the edge.
    /// </summary>
    public double Margin { get; init; } = 200;

    /// <summary>Students placed in each classroom. Default: 4.</summary>
    public int StudentsPerRoom { get; init; } = 4;

    /// <summary>Student body radius used for proximity penalties. Default: 8.</summary>
    public double BodyRadius { get; init; } = 8;

    /// <summary>How far a student senses. Default: 180.</summary>
    public double SensorRange { get; init; } = 180;

    /// <summary>World units traveled per tick. Default: 6.</summary>
    public double Velocity { get; init; } = 6;

    /// <summary>Passage device name: Eye, Math, Radar, or Radio. Default: Eye.</summary>
    public string Device { get; init; } = "Eye";

    /// <summary>World width the building needs, including margin.</summary>
    public double RequiredWidth => Margin * 2 + Math.Max(1, HallLength);

    /// <summary>World height the building needs, including rooms on both sides and margin.</summary>
    public double RequiredHeight => Margin * 2 + RoomDepth * 2 + Math.Max(1, HallWidth);
}
