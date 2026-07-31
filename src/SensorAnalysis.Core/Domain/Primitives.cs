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

    /// <summary>Euclidean distance between two points.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Distance in world units.</returns>
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
}

/// <summary>Integer grid size (tiles across / tiles high, or pixel size of a tile).</summary>
/// <param name="Width">Width in grid cells or pixels.</param>
/// <param name="Height">Height in grid cells or pixels.</param>
public readonly record struct GridSize(int Width, int Height)
{
    /// <summary>Product of width and height.</summary>
    public int Area => Width * Height;
}

/// <summary>Discrete movement / action choices (no-inertia model, matching the classic platform).</summary>
public enum AgentAction
{
    /// <summary>Remain in place.</summary>
    Stay = 0,

    /// <summary>Move north (negative Y).</summary>
    North,

    /// <summary>Move northeast.</summary>
    NorthEast,

    /// <summary>Move east (positive X).</summary>
    East,

    /// <summary>Move southeast.</summary>
    SouthEast,

    /// <summary>Move south (positive Y).</summary>
    South,

    /// <summary>Move southwest.</summary>
    SouthWest,

    /// <summary>Move west (negative X).</summary>
    West,

    /// <summary>Move northwest.</summary>
    NorthWest
}

/// <summary>Converts <see cref="AgentAction"/> to a unit step vector.</summary>
public static class AgentActionExtensions
{
    /// <summary>Maps an action to an 8-connected unit step (or zero for <see cref="AgentAction.Stay"/>).</summary>
    /// <param name="action">The action to convert.</param>
    /// <returns>A unit step vector in world coordinates.</returns>
    public static Vec2 ToStep(this AgentAction action) => action switch
    {
        AgentAction.North => new(0, -1),
        AgentAction.NorthEast => new(1, -1),
        AgentAction.East => new(1, 0),
        AgentAction.SouthEast => new(1, 1),
        AgentAction.South => new(0, 1),
        AgentAction.SouthWest => new(-1, 1),
        AgentAction.West => new(-1, 0),
        AgentAction.NorthWest => new(-1, -1),
        _ => Vec2.Zero
    };
}
