namespace SensorAnalysis.Core.Domain;

/// <summary>
/// An inhabitant with fixed extent. The 2D counterpart of a space-engine body:
/// agents remain points, and anything with size (a rock, a wall) is a body.
/// Heading 0 points along +X. Positive heading rotates toward +Y.
/// </summary>
public class Body : WorldObject
{
    /// <summary>Logical kind (<c>Wall</c>, <c>Asteroid</c>, …). Default: <c>Body</c>.</summary>
    public string BodyType { get; set; } = "Body";

    /// <summary>Disc or capsule. Default: <see cref="BodyShape.Disc"/>.</summary>
    public BodyShape Shape { get; set; } = BodyShape.Disc;

    /// <summary>
    /// Collision and draw radius. For a segment this is the capsule radius
    /// (half the wall thickness).
    /// </summary>
    public double Radius { get; set; } = 1;

    /// <summary>Centerline length. Ignored for <see cref="BodyShape.Disc"/>.</summary>
    public double Length { get; set; }

    /// <summary>Centerline heading in radians. 0 is +X, positive toward +Y.</summary>
    public double HeadingRadians { get; set; }

    /// <summary>
    /// When true (default), the body does not move. Agents that penetrate it are
    /// pushed back to the surface. The body itself stays put.
    /// </summary>
    public bool IsAnchored { get; set; } = true;

    /// <summary>Unit direction of the centerline.</summary>
    public Vec2 Direction => new(Math.Cos(HeadingRadians), Math.Sin(HeadingRadians));

    /// <summary>First centerline endpoint (segment) or the center (disc).</summary>
    public Vec2 EndpointA =>
        Shape == BodyShape.Segment
            ? Position - Direction * (Length * 0.5)
            : Position;

    /// <summary>Second centerline endpoint (segment) or the center (disc).</summary>
    public Vec2 EndpointB =>
        Shape == BodyShape.Segment
            ? Position + Direction * (Length * 0.5)
            : Position;

    /// <summary>
    /// Nearest point on the centerline (the center, for a disc).
    /// Uses the toroidal minimum image so a long body is still found from its near end.
    /// </summary>
    public Vec2 NearestCenterlinePoint(Vec2 query, double worldWidth, double worldHeight)
    {
        if (Shape != BodyShape.Segment || Length <= 1e-9)
            return Position;

        var along = Direction * Length;
        var len2 = along.X * along.X + along.Y * along.Y;
        if (len2 < 1e-18)
            return Position;

        var a = EndpointA;
        var aq = Vec2.ToroidalDisplacement(a, query, worldWidth, worldHeight);
        var t = Math.Clamp((aq.X * along.X + aq.Y * along.Y) / len2, 0, 1);
        return Vec2.Wrap(new Vec2(a.X + along.X * t, a.Y + along.Y * t), worldWidth, worldHeight);
    }

    /// <summary>
    /// Nearest point on the solid surface (centerline pushed out by <see cref="Radius"/>
    /// toward <paramref name="query"/>).
    /// </summary>
    public Vec2 NearestSurfacePoint(Vec2 query, double worldWidth, double worldHeight)
    {
        var spine = NearestCenterlinePoint(query, worldWidth, worldHeight);
        var outward = Vec2.ToroidalDisplacement(spine, query, worldWidth, worldHeight);
        var dist = outward.Length;
        if (dist < 1e-9)
        {
            var tangent = Shape == BodyShape.Segment ? Direction : new Vec2(1, 0);
            var normal = new Vec2(-tangent.Y, tangent.X);
            return Vec2.Wrap(spine + normal * Radius, worldWidth, worldHeight);
        }

        return Vec2.Wrap(spine + outward * (Radius / dist), worldWidth, worldHeight);
    }

    /// <summary>
    /// Signed distance from <paramref name="query"/> to the surface.
    /// Negative means the point is inside the body.
    /// </summary>
    public double DistanceToSurface(Vec2 query, double worldWidth, double worldHeight)
    {
        var spine = NearestCenterlinePoint(query, worldWidth, worldHeight);
        return Vec2.ToroidalDistance(query, spine, worldWidth, worldHeight) - Radius;
    }

    /// <summary>True when the disc or capsule meets the axis-aligned rectangle.</summary>
    public bool IntersectsAabb(
        double minX,
        double minY,
        double maxX,
        double maxY,
        double worldWidth,
        double worldHeight)
    {
        var pad = Math.Max(0, Radius);
        minX -= pad;
        minY -= pad;
        maxX += pad;
        maxY += pad;

        var a = Vec2.Wrap(EndpointA, worldWidth, worldHeight);
        var b = Vec2.Wrap(EndpointB, worldWidth, worldHeight);
        if (Contains(a) || Contains(b))
            return true;

        return SegmentsCross(a, b, new Vec2(minX, minY), new Vec2(maxX, minY))
            || SegmentsCross(a, b, new Vec2(maxX, minY), new Vec2(maxX, maxY))
            || SegmentsCross(a, b, new Vec2(maxX, maxY), new Vec2(minX, maxY))
            || SegmentsCross(a, b, new Vec2(minX, maxY), new Vec2(minX, minY));

        bool Contains(Vec2 p) => p.X >= minX && p.X < maxX && p.Y >= minY && p.Y < maxY;
    }

    private static bool SegmentsCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        var ab = b - a;
        var ac = c - a;
        var ad = d - a;
        var cd = d - c;
        var ca = a - c;
        var cb = b - c;
        var abCrossAc = Cross(ab, ac);
        var abCrossAd = Cross(ab, ad);
        var cdCrossCa = Cross(cd, ca);
        var cdCrossCb = Cross(cd, cb);
        return abCrossAc * abCrossAd <= 0 && cdCrossCa * cdCrossCb <= 0;
    }

    private static double Cross(Vec2 u, Vec2 v) => u.X * v.Y - u.Y * v.X;
}
