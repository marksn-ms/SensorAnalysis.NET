using System.Windows.Media.Media3D;
using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.App.Rendering;

/// <summary>Builds a WPF transform from a world object's pose and visual radius.</summary>
public static class PoseTransform
{
    /// <summary>
    /// Local +X → <see cref="WorldObject.Right"/>, +Y → <see cref="WorldObject.Up"/>, +Z → <see cref="WorldObject.Forward"/>,
    /// scaled by <see cref="WorldObject.VisualRadius"/>.
    /// </summary>
    /// <param name="obj">Object whose pose is applied.</param>
    /// <returns>A matrix suitable for <see cref="MatrixTransform3D"/>.</returns>
    public static Matrix3D From(WorldObject obj)
    {
        var r = obj.Right;
        var u = obj.Up;
        var f = obj.Forward;
        if (r.LengthSquared < 0.5)
            r = Vec3.UnitX;
        var s = Math.Max(obj.VisualRadius, 1e-3);
        var p = obj.Position;
        return new Matrix3D(
            r.X * s, r.Y * s, r.Z * s, 0,
            u.X * s, u.Y * s, u.Z * s, 0,
            f.X * s, f.Y * s, f.Z * s, 0,
            p.X, p.Y, p.Z, 1);
    }
}
