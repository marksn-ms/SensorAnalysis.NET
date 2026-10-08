using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Hallway;

/// <summary>One classroom: interior bounds, center beacon, and the hall doorway.</summary>
public sealed class Classroom
{
    /// <summary>Index in <see cref="FloorPlan.Rooms"/>. North rooms come first.</summary>
    public int Index { get; init; }

    /// <summary>True when the room sits on the north side of the hall (smaller Y).</summary>
    public bool North { get; init; }

    /// <summary>Beacon position. Students are scattered around this point.</summary>
    public Vec2 Center { get; init; }

    /// <summary>Center of the doorway gap on the hall wall.</summary>
    public Vec2 Door { get; init; }

    /// <summary>Interior bounds, inset from the wall centerlines.</summary>
    public double MinX { get; init; }

    /// <summary>Interior bounds, inset from the wall centerlines.</summary>
    public double MaxX { get; init; }

    /// <summary>Interior bounds, inset from the wall centerlines.</summary>
    public double MinY { get; init; }

    /// <summary>Interior bounds, inset from the wall centerlines.</summary>
    public double MaxY { get; init; }
}

/// <summary>
/// Fixed floor plan of wall <see cref="Body"/> segments and classrooms.
/// Coordinates are world units. The building sits inside the margin so the torus
/// seam does not glue the ends of the hall together.
/// </summary>
public sealed class FloorPlan
{
    private FloorPlan(FloorPlanSpec spec, IReadOnlyList<Body> walls, IReadOnlyList<Classroom> rooms, double hallCenterY)
    {
        Spec = spec;
        Walls = walls;
        Rooms = rooms;
        HallCenterY = hallCenterY;
    }

    /// <summary>Dimensions used to build this plan.</summary>
    public FloorPlanSpec Spec { get; }

    /// <summary>Anchored wall capsules.</summary>
    public IReadOnlyList<Body> Walls { get; }

    /// <summary>Classrooms, north side first, then south.</summary>
    public IReadOnlyList<Classroom> Rooms { get; }

    /// <summary>Y of the hall centerline.</summary>
    public double HallCenterY { get; }

    /// <summary>Build wall segments and rooms. Does not add them to a world.</summary>
    public static FloorPlan Build(FloorPlanSpec spec)
    {
        var x0 = spec.Margin;
        var x1 = spec.Margin + spec.HallLength;
        var yNorthOuter = spec.Margin;
        var yHallNorth = spec.Margin + spec.RoomDepth;
        var yHallSouth = yHallNorth + spec.HallWidth;
        var ySouthOuter = yHallSouth + spec.RoomDepth;
        var half = spec.WallThickness * 0.5;

        var doorOffsets = DoorOffsets(spec);
        var bounds = new List<double> { x0 };
        for (var i = 0; i < doorOffsets.Count - 1; i++)
            bounds.Add(x0 + (doorOffsets[i] + doorOffsets[i + 1]) * 0.5);
        bounds.Add(x1);

        var walls = new List<Body>
        {
            Segment(new Vec2(x0, yNorthOuter), new Vec2(x1, yNorthOuter), spec.WallThickness),
            Segment(new Vec2(x0, ySouthOuter), new Vec2(x1, ySouthOuter), spec.WallThickness),
            Segment(new Vec2(x0, yNorthOuter), new Vec2(x0, ySouthOuter), spec.WallThickness),
            Segment(new Vec2(x1, yNorthOuter), new Vec2(x1, ySouthOuter), spec.WallThickness)
        };
        walls.AddRange(HallWall(x0, x1, yHallNorth, doorOffsets, spec));
        walls.AddRange(HallWall(x0, x1, yHallSouth, doorOffsets, spec));
        for (var i = 1; i < bounds.Count - 1; i++)
        {
            var x = bounds[i];
            walls.Add(Segment(new Vec2(x, yNorthOuter), new Vec2(x, yHallNorth), spec.WallThickness));
            walls.Add(Segment(new Vec2(x, yHallSouth), new Vec2(x, ySouthOuter), spec.WallThickness));
        }

        var rooms = new List<Classroom>();
        var index = 0;
        for (var i = 0; i < doorOffsets.Count; i++)
        {
            var minX = bounds[i];
            var maxX = bounds[i + 1];
            var doorX = x0 + doorOffsets[i];
            rooms.Add(new Classroom
            {
                Index = index++,
                North = true,
                Center = new Vec2((minX + maxX) * 0.5, (yNorthOuter + yHallNorth) * 0.5),
                Door = new Vec2(doorX, yHallNorth),
                MinX = minX + half,
                MaxX = maxX - half,
                MinY = yNorthOuter + half,
                MaxY = yHallNorth - half
            });
        }

        for (var i = 0; i < doorOffsets.Count; i++)
        {
            var minX = bounds[i];
            var maxX = bounds[i + 1];
            var doorX = x0 + doorOffsets[i];
            rooms.Add(new Classroom
            {
                Index = index++,
                North = false,
                Center = new Vec2((minX + maxX) * 0.5, (yHallSouth + ySouthOuter) * 0.5),
                Door = new Vec2(doorX, yHallSouth),
                MinX = minX + half,
                MaxX = maxX - half,
                MinY = yHallSouth + half,
                MaxY = ySouthOuter - half
            });
        }

        return new FloorPlan(spec, walls, rooms, (yHallNorth + yHallSouth) * 0.5);
    }

    private static List<double> DoorOffsets(FloorPlanSpec spec)
    {
        var doors = new List<double>();
        for (var x = spec.DoorSpacing * 0.5; x <= spec.HallLength - spec.DoorWidth * 0.5; x += spec.DoorSpacing)
            doors.Add(x);
        if (doors.Count == 0)
            doors.Add(spec.HallLength * 0.5);
        return doors;
    }

    private static IEnumerable<Body> HallWall(
        double x0,
        double x1,
        double y,
        IReadOnlyList<double> doorOffsets,
        FloorPlanSpec spec)
    {
        var cursor = x0;
        foreach (var door in doorOffsets)
        {
            var left = x0 + door - spec.DoorWidth * 0.5;
            var right = x0 + door + spec.DoorWidth * 0.5;
            if (left - cursor > 1)
                yield return Segment(new Vec2(cursor, y), new Vec2(left, y), spec.WallThickness);
            cursor = right;
        }

        if (x1 - cursor > 1)
            yield return Segment(new Vec2(cursor, y), new Vec2(x1, y), spec.WallThickness);
    }

    private static Body Segment(Vec2 a, Vec2 b, double thickness)
    {
        var delta = b - a;
        return new Body
        {
            BodyType = "Wall",
            Label = "Wall",
            Shape = BodyShape.Segment,
            Position = new Vec2((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5),
            Length = delta.Length,
            HeadingRadians = Math.Atan2(delta.Y, delta.X),
            Radius = thickness * 0.5,
            IsAnchored = true
        };
    }
}
