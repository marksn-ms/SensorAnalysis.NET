using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.World;

/// <summary>A single cell in the world grid.</summary>
public sealed class Tile
{
    /// <summary>Zero-based column index in the tile grid.</summary>
    public int Col { get; }

    /// <summary>Zero-based row index in the tile grid.</summary>
    public int Row { get; }

    /// <summary>World-space center of this tile.</summary>
    public Vec2 Center { get; }

    /// <summary>Optional resource value associated with this tile.</summary>
    public double Resources { get; set; }

    /// <summary>Tick when an agent last covered this tile, or <c>-1</c> if never covered.</summary>
    public long LastCoveredTick { get; set; } = -1;

    /// <summary>Creates a tile at the given grid indices and world-space center.</summary>
    /// <param name="col">Column index.</param>
    /// <param name="row">Row index.</param>
    /// <param name="center">World-space center point.</param>
    public Tile(int col, int row, Vec2 center)
    {
        Col = col;
        Row = row;
        Center = center;
    }
}

/// <summary>Geometric shape used when rendering or interpreting tiles.</summary>
public enum TileShape
{
    /// <summary>Axis-aligned rectangular tiles (default).</summary>
    Rectangle,
    // Hexagon support can be added later without changing the agent API
}
