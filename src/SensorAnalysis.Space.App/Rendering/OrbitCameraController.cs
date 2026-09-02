using System.Windows;
using System.Windows.Media.Media3D;
using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.App.Rendering;

/// <summary>Orbit-about-focus camera with optional fly (god) mode. Yaw is about world +Y; pitch is clamped 20° from the poles.</summary>
public sealed class OrbitCameraController
{
    /// <summary>Default isometric yaw in degrees.</summary>
    public const double DefaultYaw = -38;

    /// <summary>Default isometric pitch in degrees.</summary>
    public const double DefaultPitch = -22;

    /// <summary>Pitch limit so the camera stays at least 20° from ±Y.</summary>
    public const double PitchLimit = 70;

    /// <summary>Gets or sets azimuth about world +Y in degrees.</summary>
    public double Yaw { get; set; } = DefaultYaw;

    /// <summary>Gets or sets elevation in degrees, clamped to ±<see cref="PitchLimit"/>.</summary>
    public double Pitch { get; set; } = DefaultPitch;

    /// <summary>Gets or sets distance from <see cref="Focus"/> to the camera.</summary>
    public double Distance { get; set; } = 420;

    /// <summary>Gets or sets the look-at point (may be an unwrapped image of a wrapped object).</summary>
    public Vec3 Focus { get; set; }

    /// <summary>Gets or sets horizontal field of view in degrees (WPF <see cref="PerspectiveCamera.FieldOfView"/>).</summary>
    public double FieldOfView { get; set; } = 48;

    /// <summary>Gets or sets a value indicating whether WASD fly is active (orbit follow is suspended).</summary>
    public bool Fly { get; set; }

    /// <summary>Gets or sets the camera position used in fly mode.</summary>
    public Point3D FlyPosition { get; set; }

    /// <summary>Gets or sets the look direction used in fly mode.</summary>
    public Vector3D FlyLook { get; set; } = new(-1, -0.4, -1);

    /// <summary>Gets or sets fly move speed in world units per key sample.</summary>
    public double FlySpeed { get; set; } = 8;

    /// <summary>Minimum orbit distance.</summary>
    public double MinDistance { get; set; } = 12;

    /// <summary>Maximum orbit distance.</summary>
    public double MaxDistance { get; set; } = 8000;

    /// <summary>Applies yaw/pitch deltas (orbit). Pitch is clamped away from the poles.</summary>
    /// <param name="deltaYaw">Yaw change in degrees.</param>
    /// <param name="deltaPitch">Pitch change in degrees.</param>
    public void Orbit(double deltaYaw, double deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -PitchLimit, PitchLimit);
    }

    /// <summary>Pans <see cref="Focus"/> in the camera plane.</summary>
    /// <param name="deltaX">Pixel delta X (right positive).</param>
    /// <param name="deltaY">Pixel delta Y (down positive, inverted to world up).</param>
    public void Pan(double deltaX, double deltaY)
    {
        var (_, look, _) = OrbitPose();
        var lookN = look;
        lookN.Normalize();
        var right = Vector3D.CrossProduct(lookN, new Vector3D(0, 1, 0));
        if (right.LengthSquared < 1e-8)
            right = new Vector3D(1, 0, 0);
        right.Normalize();
        var camUp = Vector3D.CrossProduct(right, lookN);
        camUp.Normalize();
        var scale = Distance * 0.0025;
        Focus += new Vec3(
            -(deltaX * right.X + -deltaY * camUp.X) * scale,
            -(deltaX * right.Y + -deltaY * camUp.Y) * scale,
            -(deltaX * right.Z + -deltaY * camUp.Z) * scale);
    }

    /// <summary>Scales <see cref="Distance"/> by <paramref name="factor"/>.</summary>
    /// <param name="factor">Multiply distance by this value (&lt; 1 zooms in).</param>
    public void Zoom(double factor) =>
        Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);

    /// <summary>Restores the default isometric view of <paramref name="focus"/>.</summary>
    /// <param name="focus">Look-at point.</param>
    /// <param name="distance">Orbit distance.</param>
    public void Reset(Vec3 focus, double distance)
    {
        Fly = false;
        Focus = focus;
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;
    }

    /// <summary>Captures the current orbit pose into fly-mode state.</summary>
    public void EnterFly()
    {
        var (position, look, _) = OrbitPose();
        FlyPosition = position;
        FlyLook = look;
        Fly = true;
    }

    /// <summary>Leaves fly mode and rebuilds orbit parameters from the fly camera.</summary>
    public void ExitFly()
    {
        Fly = false;
        var look = FlyLook;
        if (look.LengthSquared < 1e-12)
            look = new Vector3D(-1, -0.4, -1);
        look.Normalize();
        Focus = new Vec3(
            FlyPosition.X + look.X * Distance,
            FlyPosition.Y + look.Y * Distance,
            FlyPosition.Z + look.Z * Distance);
        Pitch = Math.Clamp(Math.Asin(Math.Clamp(-look.Y, -1, 1)) * 180 / Math.PI, -PitchLimit, PitchLimit);
        Yaw = Math.Atan2(-look.Z, -look.X) * 180 / Math.PI;
    }

    /// <summary>Moves the fly camera along look / right / world-up.</summary>
    /// <param name="forward">+1 along look, −1 opposite.</param>
    /// <param name="right">+1 along camera right.</param>
    /// <param name="up">+1 along world +Y.</param>
    public void FlyMove(double forward, double right, double up)
    {
        var look = FlyLook;
        if (look.LengthSquared < 1e-12)
            return;
        look.Normalize();
        var camRight = Vector3D.CrossProduct(look, new Vector3D(0, 1, 0));
        if (camRight.LengthSquared < 1e-8)
            camRight = new Vector3D(1, 0, 0);
        camRight.Normalize();
        var delta = look * (forward * FlySpeed) + camRight * (right * FlySpeed) + new Vector3D(0, 1, 0) * (up * FlySpeed);
        FlyPosition = new Point3D(FlyPosition.X + delta.X, FlyPosition.Y + delta.Y, FlyPosition.Z + delta.Z);
    }

    /// <summary>Mouse-look in fly mode (yaw about +Y, pitch clamped).</summary>
    /// <param name="deltaYaw">Yaw in degrees.</param>
    /// <param name="deltaPitch">Pitch in degrees.</param>
    public void FlyLookDelta(double deltaYaw, double deltaPitch)
    {
        var yaw = Math.Atan2(FlyLook.X, FlyLook.Z) * 180 / Math.PI;
        var horiz = Math.Sqrt(FlyLook.X * FlyLook.X + FlyLook.Z * FlyLook.Z);
        var pitch = Math.Atan2(FlyLook.Y, horiz) * 180 / Math.PI;
        yaw += deltaYaw;
        pitch = Math.Clamp(pitch + deltaPitch, -PitchLimit, PitchLimit);
        var pr = pitch * Math.PI / 180;
        var yr = yaw * Math.PI / 180;
        FlyLook = new Vector3D(Math.Cos(pr) * Math.Sin(yr), Math.Sin(pr), Math.Cos(pr) * Math.Cos(yr));
    }

    /// <summary>Writes position, look, up, FOV, and clip planes onto <paramref name="camera"/>.</summary>
    /// <param name="camera">Target perspective camera.</param>
    /// <param name="worldDiagonal">Used to size the far clip plane.</param>
    public void Apply(PerspectiveCamera camera, double worldDiagonal)
    {
        camera.FieldOfView = Math.Clamp(FieldOfView, 35, 75);
        Point3D position;
        Vector3D look;
        Vector3D up;
        double dist;
        if (Fly)
        {
            position = FlyPosition;
            look = FlyLook;
            if (look.LengthSquared < 1e-12)
                look = new Vector3D(0, 0, -1);
            look.Normalize();
            up = new Vector3D(0, 1, 0);
            dist = Distance;
        }
        else
        {
            (position, look, up) = OrbitPose();
            dist = Distance;
        }

        camera.Position = position;
        camera.LookDirection = look;
        camera.UpDirection = up;
        camera.NearPlaneDistance = Math.Max(0.05, 0.02 * Math.Max(dist, 1));
        camera.FarPlaneDistance = Math.Max(camera.NearPlaneDistance * 80, 4 * Math.Max(worldDiagonal, dist));
    }

    /// <summary>Orbit camera pose for the current yaw, pitch, distance, and focus.</summary>
    /// <returns>Position, look direction, and up.</returns>
    public (Point3D Position, Vector3D Look, Vector3D Up) OrbitPose()
    {
        var yaw = Yaw * Math.PI / 180;
        var pitch = Pitch * Math.PI / 180;
        var offset = new Vector3D(
            Distance * Math.Cos(pitch) * Math.Cos(yaw),
            Distance * Math.Sin(pitch),
            Distance * Math.Cos(pitch) * Math.Sin(yaw));
        var position = new Point3D(Focus.X + offset.X, Focus.Y + offset.Y, Focus.Z + offset.Z);
        var look = new Vector3D(-offset.X, -offset.Y, -offset.Z);
        return (position, look, new Vector3D(0, 1, 0));
    }

    /// <summary>World-space width of the view at the focus, used for logarithmic grid spacing.</summary>
    /// <returns>Approximate span in world units.</returns>
    public double VisibleSpan()
    {
        var fov = Math.Clamp(FieldOfView, 35, 75) * Math.PI / 180;
        return 2 * Distance * Math.Tan(fov * 0.5);
    }
}
