using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace SensorAnalysis.Space.App.Rendering;

/// <summary>Builds a world-space picking ray from a mouse position in a <see cref="Viewport3D"/>.</summary>
public static class ViewportRay
{
    /// <summary>
    /// Unprojects <paramref name="mouse"/> through a <see cref="PerspectiveCamera"/>.
    /// WPF field of view is horizontal.
    /// </summary>
    /// <param name="viewport">Host viewport.</param>
    /// <param name="mouse">Mouse position in viewport coordinates.</param>
    /// <param name="origin">Ray origin (camera position).</param>
    /// <param name="direction">Normalized ray direction.</param>
    /// <returns><c>true</c> when the viewport has a usable size and camera.</returns>
    public static bool TryGet(Viewport3D viewport, Point mouse, out Point3D origin, out Vector3D direction)
    {
        origin = new Point3D();
        direction = new Vector3D();
        if (viewport.Camera is not PerspectiveCamera camera)
            return false;
        if (viewport.ActualWidth < 1 || viewport.ActualHeight < 1)
            return false;

        origin = camera.Position;
        var look = camera.LookDirection;
        if (look.LengthSquared < 1e-12)
            return false;
        look.Normalize();

        var up = camera.UpDirection;
        if (up.LengthSquared < 1e-12)
            up = new Vector3D(0, 1, 0);
        up.Normalize();

        var right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 1e-12)
            return false;
        right.Normalize();
        up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        var aspect = viewport.ActualWidth / viewport.ActualHeight;
        var fov = camera.FieldOfView * Math.PI / 180.0;
        var tanH = Math.Tan(fov * 0.5);
        var nx = (mouse.X / viewport.ActualWidth) * 2 - 1;
        var ny = 1 - (mouse.Y / viewport.ActualHeight) * 2;
        direction = look + right * (nx * tanH) + up * (ny * tanH / aspect);
        if (direction.LengthSquared < 1e-12)
            return false;
        direction.Normalize();
        return true;
    }
}
