namespace SensorAnalysis.Sphere.Core.Geometry;

/// <summary>A unit-vector position or tangent vector used by a spherical world.</summary>
public readonly record struct SphereVector(double X, double Y, double Z)
{
    /// <summary>Gets the zero vector.</summary>
    public static SphereVector Zero => new(0, 0, 0);

    /// <summary>Gets the vector magnitude.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>Returns a unit-length copy, or <see cref="Zero"/> when this vector is negligible.</summary>
    public SphereVector Normalize()
    {
        var length = Length;
        return length < 1e-12 ? Zero : this / length;
    }

    /// <summary>Computes the dot product.</summary>
    public static double Dot(SphereVector a, SphereVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Computes the cross product.</summary>
    public static SphereVector Cross(SphereVector a, SphereVector b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Returns the great-circle angular distance between two unit positions.</summary>
    public static double AngularDistance(SphereVector a, SphereVector b) =>
        Math.Acos(Math.Clamp(Dot(a, b), -1, 1));

    /// <summary>Returns the geodesic distance on a sphere of <paramref name="radius"/>.</summary>
    public static double GeodesicDistance(SphereVector a, SphereVector b, double radius) =>
        radius * AngularDistance(a, b);

    /// <summary>Projects <paramref name="direction"/> onto the tangent plane at <paramref name="position"/>.</summary>
    public static SphereVector TangentAt(SphereVector position, SphereVector direction) =>
        (direction - position * Dot(position, direction)).Normalize();

    /// <summary>Moves along a great circle by an angular distance from a tangent direction.</summary>
    public static SphereVector Step(SphereVector position, SphereVector tangentDirection, double radians) =>
        (position * Math.Cos(radians) + TangentAt(position, tangentDirection) * Math.Sin(radians)).Normalize();

    /// <summary>Adds two vectors.</summary>
    public static SphereVector operator +(SphereVector a, SphereVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Subtracts two vectors.</summary>
    public static SphereVector operator -(SphereVector a, SphereVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Scales a vector.</summary>
    public static SphereVector operator *(SphereVector vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    /// <summary>Divides a vector by a scalar.</summary>
    public static SphereVector operator /(SphereVector vector, double scalar) =>
        new(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);
}
