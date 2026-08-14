using System.Windows.Media.Media3D;

namespace SensorAnalysis.Viewport3DPrototype.Geometry;

/// <summary>A spherical prototype world described by its evenly distributed tile centers.</summary>
/// <param name="Name">Human-readable generator name.</param>
/// <param name="Centers">Fibonacci-distributed tile centers on the unit sphere.</param>
/// <param name="SensorRange">Default one-tile-scale sensor range in world-radius units.</param>
public sealed record PrototypeWorldShape(
    string Name,
    IReadOnlyList<Point3D> Centers,
    double SensorRange);

/// <summary>Builds spherical worlds from evenly distributed Fibonacci sites.</summary>
public static class PlatonicWorldCatalog
{
    /// <summary>Smallest useful site count for the interactive prototype.</summary>
    public const int MinLevel = 50;

    /// <summary>
    /// Creates a spherical world for the requested number of tile centers.
    /// There is deliberately no artificial upper bound; callers should select a count appropriate
    /// to their desired rendering and simulation workload.
    /// </summary>
    /// <param name="level">Number of Fibonacci-distributed tile centers.</param>
    public static PrototypeWorldShape Create(int level)
    {
        if (level < MinLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"The prototype requires at least {MinLevel} centers.");

        return new PrototypeWorldShape(
            "Fibonacci sphere",
            FibonacciCenters(level),
            OneTileCoveringRange(level));
    }

    private static IReadOnlyList<Point3D> FibonacciCenters(int count)
    {
        var goldenAngle = Math.PI * (3 - Math.Sqrt(5));
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var y = 1 - 2 * ((index + 0.5) / count);
                var radial = Math.Sqrt(1 - y * y);
                var angle = index * goldenAngle;
                return new Point3D(radial * Math.Cos(angle), y, radial * Math.Sin(angle));
            })
            .ToArray();
    }

    // Hexagonal-area approximation: a sensor range sufficient to cover a typical Voronoi cell.
    private static double OneTileCoveringRange(int count) =>
        Math.Sqrt(8 * Math.PI / (3 * Math.Sqrt(3) * count));
}
