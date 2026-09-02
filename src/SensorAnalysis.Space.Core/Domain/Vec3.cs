namespace SensorAnalysis.Space.Core.Domain;

/// <summary>A 3D vector in world coordinates (right-handed, Y-up).</summary>
/// <param name="X">Coordinate along world +X.</param>
/// <param name="Y">Coordinate along world +Y (up).</param>
/// <param name="Z">Coordinate along world +Z.</param>
public readonly record struct Vec3(double X, double Y, double Z)
{
    /// <summary>The zero vector.</summary>
    public static Vec3 Zero => new(0, 0, 0);

    /// <summary>Unit vector along +X.</summary>
    public static Vec3 UnitX => new(1, 0, 0);

    /// <summary>Unit vector along +Y (world up).</summary>
    public static Vec3 UnitY => new(0, 1, 0);

    /// <summary>Unit vector along +Z.</summary>
    public static Vec3 UnitZ => new(0, 0, 1);

    /// <summary>Euclidean length of this vector.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>Squared Euclidean length.</summary>
    public double LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>Returns a unit-length copy, or <see cref="Zero"/> when this vector is negligible.</summary>
    /// <returns>The normalized vector.</returns>
    public Vec3 Normalize()
    {
        var length = Length;
        return length < 1e-12 ? Zero : this / length;
    }

    /// <summary>Dot product of two vectors.</summary>
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Cross product of two vectors (right-handed).</summary>
    public static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Rotates <paramref name="v"/> by the axis-angle vector <paramref name="axisAngle"/> (Rodrigues).</summary>
    /// <param name="v">Vector to rotate.</param>
    /// <param name="axisAngle">Rotation axis scaled by the angle in radians.</param>
    /// <returns>The rotated vector.</returns>
    public static Vec3 Rotate(Vec3 v, Vec3 axisAngle)
    {
        var theta = axisAngle.Length;
        if (theta < 1e-15)
            return v;

        var k = axisAngle / theta;
        var cos = Math.Cos(theta);
        var sin = Math.Sin(theta);
        return v * cos + Cross(k, v) * sin + k * Dot(k, v) * (1 - cos);
    }

    /// <summary>Adds two vectors component-wise.</summary>
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Subtracts two vectors component-wise.</summary>
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Negates a vector.</summary>
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>Scales a vector by a scalar.</summary>
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Scales a vector by a scalar.</summary>
    public static Vec3 operator *(double s, Vec3 a) => a * s;

    /// <summary>Divides a vector by a scalar.</summary>
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    /// <summary>Euclidean distance between two points (no wrapping).</summary>
    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    /// <summary>Wraps a scalar into <c>[−half, half)</c> for a centered torus of length <paramref name="extent"/>.</summary>
    /// <param name="value">Coordinate that may lie outside the interval.</param>
    /// <param name="extent">Positive world extent along that axis.</param>
    /// <returns>Equivalent coordinate inside the primary cell.</returns>
    public static double WrapCoord(double value, double extent)
    {
        if (extent <= 0)
            return value;

        var half = extent * 0.5;
        var r = (value + half) % extent;
        if (r < 0)
            r += extent;
        if (r >= extent)
            r = 0;
        return r - half;
    }

    /// <summary>Wraps a point onto a centered cubic 3-torus of the given extent.</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <param name="extent">World edge length (the cube spans <c>[−extent/2, extent/2)</c>).</param>
    /// <returns>The equivalent point in the primary cell.</returns>
    public static Vec3 Wrap(Vec3 p, double extent) =>
        new(WrapCoord(p.X, extent), WrapCoord(p.Y, extent), WrapCoord(p.Z, extent));

    /// <summary>Shortest signed displacement from <paramref name="from"/> to <paramref name="to"/> on a 3-torus.</summary>
    /// <param name="from">Origin point.</param>
    /// <param name="to">Destination point.</param>
    /// <param name="extent">World edge length.</param>
    /// <returns>Delta with each component in <c>[−extent/2, extent/2)</c>.</returns>
    public static Vec3 ToroidalDisplacement(Vec3 from, Vec3 to, double extent) =>
        new(WrapCoord(to.X - from.X, extent), WrapCoord(to.Y - from.Y, extent), WrapCoord(to.Z - from.Z, extent));

    /// <summary>Shortest distance between two points on a cubic 3-torus.</summary>
    public static double ToroidalDistance(Vec3 a, Vec3 b, double extent) =>
        ToroidalDisplacement(a, b, extent).Length;

    /// <summary>Draws a uniformly random unit vector using Gaussian axes.</summary>
    /// <param name="rng">Deterministic random source.</param>
    /// <returns>A unit vector, or <see cref="UnitZ"/> if the sample degenerates.</returns>
    public static Vec3 RandomUnit(Random rng)
    {
        var v = new Vec3(Gaussian(rng), Gaussian(rng), Gaussian(rng));
        var n = v.Normalize();
        return n.LengthSquared < 0.5 ? UnitZ : n;
    }

    /// <summary>Builds a right-handed orthonormal frame with the given forward axis.</summary>
    /// <param name="forward">Desired forward; normalized internally.</param>
    /// <param name="upHint">Preferred up direction (need not be orthogonal).</param>
    /// <returns>Normalized (forward, up) with <c>Right = Up × Forward</c>.</returns>
    public static (Vec3 Forward, Vec3 Up) OrthonormalFrame(Vec3 forward, Vec3 upHint)
    {
        var f = forward.Normalize();
        if (f.LengthSquared < 0.5)
            f = UnitZ;

        var hint = upHint.LengthSquared < 1e-12 ? UnitY : upHint;
        if (Math.Abs(Dot(f, hint.Normalize())) > 0.98)
            hint = Math.Abs(f.Y) < 0.9 ? UnitY : UnitX;

        var right = Cross(hint, f).Normalize();
        if (right.LengthSquared < 0.5)
            right = Cross(UnitY, f).Normalize();

        var up = Cross(f, right).Normalize();
        return (f, up);
    }

    /// <summary>Samples a uniformly random orientation (forward + up).</summary>
    /// <param name="rng">Deterministic random source.</param>
    /// <returns>An orthonormal (forward, up) pair.</returns>
    public static (Vec3 Forward, Vec3 Up) RandomOrientation(Random rng)
    {
        var forward = RandomUnit(rng);
        var upHint = RandomUnit(rng);
        return OrthonormalFrame(forward, upHint);
    }

    private static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
