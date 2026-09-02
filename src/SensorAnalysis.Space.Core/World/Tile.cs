using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.Core.World;

/// <summary>A cubic voxel in the wrapped world grid (spatial index and selection target).</summary>
public sealed class Tile
{
    /// <summary>Creates a tile at the given grid indices and world-space center.</summary>
    /// <param name="ix">Zero-based index along +X.</param>
    /// <param name="iy">Zero-based index along +Y.</param>
    /// <param name="iz">Zero-based index along +Z.</param>
    /// <param name="center">World-space center of the cube.</param>
    public Tile(int ix, int iy, int iz, Vec3 center)
    {
        Ix = ix;
        Iy = iy;
        Iz = iz;
        Center = center;
    }

    /// <summary>Gets the zero-based index along +X.</summary>
    public int Ix { get; }

    /// <summary>Gets the zero-based index along +Y.</summary>
    public int Iy { get; }

    /// <summary>Gets the zero-based index along +Z.</summary>
    public int Iz { get; }

    /// <summary>Gets the world-space center of this cube.</summary>
    public Vec3 Center { get; }

    /// <summary>Gets or sets the tick when an agent last covered this cube, or <c>-1</c> if never covered.</summary>
    public long LastCoveredTick { get; set; } = -1;

    /// <inheritdoc />
    public override string ToString() => $"Tile[{Ix},{Iy},{Iz}]";
}
