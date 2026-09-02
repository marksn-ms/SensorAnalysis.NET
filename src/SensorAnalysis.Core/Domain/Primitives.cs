namespace SensorAnalysis.Core.Domain;

/// <summary>2D position in world coordinates (not tile indices).</summary>
/// <param name="X">Horizontal coordinate in world units.</param>
/// <param name="Y">Vertical coordinate in world units.</param>
public readonly record struct Vec2(double X, double Y)
{
    /// <summary>The origin vector <c>(0, 0)</c>.</summary>
    public static Vec2 Zero => new(0, 0);

    /// <summary>Euclidean length of this vector.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Returns a unit-length vector in the same direction, or <see cref="Zero"/> if the length is near zero.</summary>
    /// <returns>A normalized copy of this vector.</returns>
    public Vec2 Normalize()
    {
        var len = Length;
        return len < 1e-12 ? Zero : new(X / len, Y / len);
    }

    /// <summary>Adds two vectors component-wise.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts two vectors component-wise.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Scales a vector by a scalar.</summary>
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

    /// <summary>Euclidean distance between two points (no wrapping).</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Distance in world units.</returns>
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    /// <summary>
    /// Wraps a scalar into <c>[0, size)</c> (toroidal / modular arithmetic).
    /// </summary>
    /// <param name="value">Coordinate that may lie outside the interval.</param>
    /// <param name="size">Positive world extent along that axis.</param>
    /// <returns>Equivalent coordinate inside the interval.</returns>
    public static double WrapCoord(double value, double size)
    {
        if (size <= 0) return value;
        var r = value % size;
        return r < 0 ? r + size : r;
    }

    /// <summary>
    /// Wraps a point onto a toroidal world of the given size (classic open-world topology).
    /// </summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <param name="width">World width.</param>
    /// <param name="height">World height.</param>
    /// <returns>Point with coordinates in <c>[0, width) × [0, height)</c>.</returns>
    public static Vec2 Wrap(Vec2 p, double width, double height) =>
        new(WrapCoord(p.X, width), WrapCoord(p.Y, height));

    /// <summary>
    /// Shortest signed displacement from <paramref name="from"/> to <paramref name="to"/> on a torus.
    /// </summary>
    /// <param name="from">Origin point.</param>
    /// <param name="to">Destination point.</param>
    /// <param name="width">World width.</param>
    /// <param name="height">World height.</param>
    /// <returns>Delta vector with each component in <c>(-size/2, size/2]</c>.</returns>
    public static Vec2 ToroidalDisplacement(Vec2 from, Vec2 to, double width, double height) =>
        new(ShortestDelta(from.X, to.X, width), ShortestDelta(from.Y, to.Y, height));

    /// <summary>
    /// Shortest distance between two points on a toroidal world.
    /// </summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <param name="width">World width.</param>
    /// <param name="height">World height.</param>
    /// <returns>Toroidal distance in world units.</returns>
    public static double ToroidalDistance(Vec2 a, Vec2 b, double width, double height) =>
        ToroidalDisplacement(a, b, width, height).Length;

    private static double ShortestDelta(double from, double to, double size)
    {
        if (size <= 0) return to - from;
        var d = WrapCoord(to - from, size);
        if (d > size * 0.5) d -= size;
        return d;
    }
}

/// <summary>Integer grid size (tiles across / tiles high, or pixel size of a tile).</summary>
/// <param name="Width">Width in grid cells or pixels.</param>
/// <param name="Height">Height in grid cells or pixels.</param>
public readonly record struct GridSize(int Width, int Height)
{
    /// <summary>Product of width and height.</summary>
    public int Area => Width * Height;
}

/// <summary>Discrete movement / action choices (16 compass points plus stay).</summary>
public enum AgentAction
{
    /// <summary>Remain in place.</summary>
    Stay = 0,

    /// <summary>Move north (negative Y).</summary>
    North,

    /// <summary>Move north-northeast (22.5° east of north).</summary>
    NorthNorthEast,

    /// <summary>Move northeast.</summary>
    NorthEast,

    /// <summary>Move east-northeast (22.5° north of east).</summary>
    EastNorthEast,

    /// <summary>Move east (positive X).</summary>
    East,

    /// <summary>Move east-southeast (22.5° south of east).</summary>
    EastSouthEast,

    /// <summary>Move southeast.</summary>
    SouthEast,

    /// <summary>Move south-southeast (22.5° east of south).</summary>
    SouthSouthEast,

    /// <summary>Move south (positive Y).</summary>
    South,

    /// <summary>Move south-southwest (22.5° west of south).</summary>
    SouthSouthWest,

    /// <summary>Move southwest.</summary>
    SouthWest,

    /// <summary>Move west-southwest (22.5° south of west).</summary>
    WestSouthWest,

    /// <summary>Move west (negative X).</summary>
    West,

    /// <summary>Move west-northwest (22.5° north of west).</summary>
    WestNorthWest,

    /// <summary>Move northwest.</summary>
    NorthWest,

    /// <summary>Move north-northwest (22.5° west of north).</summary>
    NorthNorthWest
}

/// <summary>Converts <see cref="AgentAction"/> to movement vectors and compass helpers.</summary>
public static class AgentActionExtensions
{
    /// <summary>Sixteen movement headings in clockwise order starting at north.</summary>
    public static readonly AgentAction[] MovementDirections =
    [
        AgentAction.North,
        AgentAction.NorthNorthEast,
        AgentAction.NorthEast,
        AgentAction.EastNorthEast,
        AgentAction.East,
        AgentAction.EastSouthEast,
        AgentAction.SouthEast,
        AgentAction.SouthSouthEast,
        AgentAction.South,
        AgentAction.SouthSouthWest,
        AgentAction.SouthWest,
        AgentAction.WestSouthWest,
        AgentAction.West,
        AgentAction.WestNorthWest,
        AgentAction.NorthWest,
        AgentAction.NorthNorthWest
    ];

    /// <summary>Maps an action to a unit step vector (zero for <see cref="AgentAction.Stay"/>).</summary>
    /// <param name="action">The action to convert.</param>
    /// <returns>A normalized step direction in world coordinates.</returns>
    public static Vec2 ToStep(this AgentAction action)
    {
        for (int i = 0; i < MovementDirections.Length; i++)
        {
            if (MovementDirections[i] == action)
                return DirectionUnit(i);
        }

        return action == AgentAction.Stay ? Vec2.Zero : Vec2.Zero;
    }

    /// <summary>Picks the nearest compass heading for a displacement vector.</summary>
    /// <param name="delta">Desired movement direction.</param>
    /// <returns>The closest <see cref="AgentAction"/> heading, or stay when near zero.</returns>
    public static AgentAction NearestTo(Vec2 delta)
    {
        if (delta.Length < 1e-6)
            return AgentAction.Stay;

        var angle = Math.Atan2(delta.X, -delta.Y);
        if (angle < 0)
            angle += Math.Tau;

        var index = ((int)Math.Round(angle / (Math.PI / 8)) % 16 + 16) % 16;
        return MovementDirections[index];
    }

    private static Vec2 DirectionUnit(int index)
    {
        var angle = index * (Math.PI / 8);
        return new Vec2(Math.Sin(angle), -Math.Cos(angle));
    }
}
