using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SensorAnalysis.Space.App.Rendering;

/// <summary>Builds frozen <see cref="MeshGeometry3D"/> primitives for the volumetric viewport.</summary>
public static class MeshFactory
{
    /// <summary>Unit sphere of radius 1 centered at the origin.</summary>
    /// <param name="rings">Latitude subdivisions.</param>
    /// <param name="segments">Longitude subdivisions.</param>
    /// <returns>A frozen sphere mesh.</returns>
    public static MeshGeometry3D Sphere(int rings = 20, int segments = 32) =>
        BuildSphere(1, rings, segments);

    /// <summary>Cube with vertices at ±1 (half-extent 1).</summary>
    /// <returns>A frozen cube mesh.</returns>
    public static MeshGeometry3D Cube()
    {
        var mesh = new MeshGeometry3D();
        var p = new[]
        {
            new Point3D(-1, -1, -1), new Point3D(1, -1, -1), new Point3D(1, 1, -1), new Point3D(-1, 1, -1),
            new Point3D(-1, -1, 1), new Point3D(1, -1, 1), new Point3D(1, 1, 1), new Point3D(-1, 1, 1)
        };
        AddQuad(mesh, p[0], p[1], p[2], p[3]);
        AddQuad(mesh, p[4], p[7], p[6], p[5]);
        AddQuad(mesh, p[0], p[4], p[5], p[1]);
        AddQuad(mesh, p[3], p[2], p[6], p[7]);
        AddQuad(mesh, p[0], p[3], p[7], p[4]);
        AddQuad(mesh, p[1], p[5], p[6], p[2]);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Unit quad in the XY plane (z = 0) covering <c>[−1, 1]²</c> with normal +Z.</summary>
    /// <returns>A frozen quad mesh.</returns>
    public static MeshGeometry3D UnitQuad()
    {
        var mesh = new MeshGeometry3D();
        AddQuad(
            mesh,
            new Point3D(-1, -1, 0),
            new Point3D(1, -1, 0),
            new Point3D(1, 1, 0),
            new Point3D(-1, 1, 0));
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Cone of height 2 along +Z with base radius 0.55 at z = −0.5 and tip at z = 1.5.</summary>
    /// <param name="segments">Radial segments.</param>
    /// <returns>A frozen cone mesh.</returns>
    public static MeshGeometry3D Cone(int segments = 16)
    {
        var mesh = new MeshGeometry3D();
        var tip = new Point3D(0, 0, 1.5);
        var baseCenter = new Point3D(0, 0, -0.5);
        var ring = new Point3D[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = Math.Tau * i / segments;
            ring[i] = new Point3D(0.55 * Math.Cos(a), 0.55 * Math.Sin(a), -0.5);
        }

        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % segments;
            AddTriangle(mesh, tip, ring[i], ring[j]);
            AddTriangle(mesh, baseCenter, ring[j], ring[i]);
        }

        mesh.Freeze();
        return mesh;
    }

    /// <summary>Regular tetrahedron inscribed in a unit sphere.</summary>
    /// <returns>A frozen tetrahedron mesh.</returns>
    public static MeshGeometry3D Tetrahedron()
    {
        var mesh = new MeshGeometry3D();
        var a = new Point3D(1, 1, 1);
        var b = new Point3D(1, -1, -1);
        var c = new Point3D(-1, 1, -1);
        var d = new Point3D(-1, -1, 1);
        Normalize(ref a);
        Normalize(ref b);
        Normalize(ref c);
        Normalize(ref d);
        AddTriangle(mesh, a, b, c);
        AddTriangle(mesh, a, c, d);
        AddTriangle(mesh, a, d, b);
        AddTriangle(mesh, b, d, c);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Icosphere of radius 1 with optional deterministic vertex displacement (asteroids).</summary>
    /// <param name="subdivisions">0–2 geodesic subdivisions.</param>
    /// <param name="noise">Displacement amplitude along the normal; 0 is a smooth sphere.</param>
    /// <param name="seed">Noise seed.</param>
    /// <returns>A frozen icosphere mesh.</returns>
    public static MeshGeometry3D Icosphere(int subdivisions = 1, double noise = 0.18, int seed = 0)
    {
        var phi = (1 + Math.Sqrt(5)) / 2;
        var verts = new List<Point3D>
        {
            P(0, 1, phi), P(0, -1, phi), P(0, 1, -phi), P(0, -1, -phi),
            P(1, phi, 0), P(-1, phi, 0), P(1, -phi, 0), P(-1, -phi, 0),
            P(phi, 0, 1), P(-phi, 0, 1), P(phi, 0, -1), P(-phi, 0, -1)
        };
        for (var i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            Normalize(ref v);
            verts[i] = v;
        }

        var faces = new List<(int A, int B, int C)>
        {
            (0, 1, 8), (0, 9, 1), (0, 4, 5), (0, 5, 9), (0, 8, 4),
            (2, 3, 11), (2, 10, 3), (2, 5, 4), (2, 4, 10), (2, 11, 5),
            (1, 6, 8), (1, 9, 7), (1, 7, 6),
            (3, 8, 6), (3, 7, 11), (3, 6, 7),
            (4, 8, 10), (5, 11, 9), (6, 10, 8), (7, 9, 11)
        };

        for (var s = 0; s < subdivisions; s++)
        {
            var midCache = new Dictionary<(int, int), int>();
            var next = new List<(int A, int B, int C)>();
            foreach (var (a, b, c) in faces)
            {
                var ab = Midpoint(verts, midCache, a, b);
                var bc = Midpoint(verts, midCache, b, c);
                var ca = Midpoint(verts, midCache, c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }

            faces = next;
        }

        if (noise > 0)
        {
            var rng = new Random(seed);
            for (var i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                var n = (rng.NextDouble() * 2 - 1) * noise;
                verts[i] = new Point3D(v.X * (1 + n), v.Y * (1 + n), v.Z * (1 + n));
            }
        }

        var mesh = new MeshGeometry3D();
        foreach (var (a, b, c) in faces)
            AddTriangle(mesh, verts[a], verts[b], verts[c]);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Unit cylinder of radius 1 along ±Z with half-length 1 (total length 2).</summary>
    /// <param name="segments">Radial segments.</param>
    /// <returns>A frozen cylinder mesh.</returns>
    public static MeshGeometry3D UnitCylinder(int segments = 10)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < segments; i++)
        {
            var a0 = Math.Tau * i / segments;
            var a1 = Math.Tau * (i + 1) / segments;
            var p0 = new Point3D(Math.Cos(a0), Math.Sin(a0), -1);
            var p1 = new Point3D(Math.Cos(a1), Math.Sin(a1), -1);
            var p2 = new Point3D(Math.Cos(a1), Math.Sin(a1), 1);
            var p3 = new Point3D(Math.Cos(a0), Math.Sin(a0), 1);
            AddQuad(mesh, p0, p1, p2, p3);
        }

        mesh.Freeze();
        return mesh;
    }

    /// <summary>Creates a thin box spanning from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">Start point.</param>
    /// <param name="to">End point.</param>
    /// <param name="thickness">Box thickness.</param>
    /// <returns>A transform that places a unit cube along the segment, or <c>null</c> if degenerate.</returns>
    public static Matrix3D? BarTransform(Point3D from, Point3D to, double thickness)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-9)
            return null;

        var mid = new Point3D((from.X + to.X) * 0.5, (from.Y + to.Y) * 0.5, (from.Z + to.Z) * 0.5);
        var axis = new Vector3D(dx / length, dy / length, dz / length);
        var rot = RotationAligningZ(axis);
        var m = new Matrix3D();
        m.Scale(new Vector3D(thickness * 0.5, thickness * 0.5, length * 0.5));
        m.Rotate(rot);
        m.Translate(new Vector3D(mid.X, mid.Y, mid.Z));
        return m;
    }

    /// <summary>Quaternion rotating +Z onto <paramref name="axis"/>.</summary>
    /// <param name="axis">Unit direction.</param>
    /// <returns>The aligning rotation.</returns>
    public static Quaternion RotationAligningZ(Vector3D axis)
    {
        axis.Normalize();
        var z = new Vector3D(0, 0, 1);
        var cross = Vector3D.CrossProduct(z, axis);
        var dot = Vector3D.DotProduct(z, axis);
        if (cross.LengthSquared < 1e-12)
            return dot > 0 ? Quaternion.Identity : new Quaternion(new Vector3D(1, 0, 0), 180);

        cross.Normalize();
        var angle = Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI;
        return new Quaternion(cross, angle);
    }

    /// <summary>Diffuse (+ optional emissive) material from an RGB color.</summary>
    /// <param name="color">Base color.</param>
    /// <param name="emissive">When true, adds a dim emissive term.</param>
    /// <returns>A frozen material group.</returns>
    public static Material Material(Color color, bool emissive = false)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        if (emissive)
        {
            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(
                (byte)(color.R / 4), (byte)(color.G / 4), (byte)(color.B / 4)))));
        }

        group.Freeze();
        return group;
    }

    private static MeshGeometry3D BuildSphere(double radius, int rings, int segments)
    {
        var mesh = new MeshGeometry3D();
        for (var ring = 0; ring <= rings; ring++)
        {
            var v = (double)ring / rings;
            var phi = v * Math.PI;
            var y = radius * Math.Cos(phi);
            var r = radius * Math.Sin(phi);
            for (var seg = 0; seg <= segments; seg++)
            {
                var u = (double)seg / segments;
                var theta = u * Math.Tau;
                mesh.Positions.Add(new Point3D(r * Math.Cos(theta), y, r * Math.Sin(theta)));
                mesh.Normals.Add(new Vector3D(Math.Sin(phi) * Math.Cos(theta), Math.Cos(phi), Math.Sin(phi) * Math.Sin(theta)));
            }
        }

        var stride = segments + 1;
        for (var ring = 0; ring < rings; ring++)
        {
            for (var seg = 0; seg < segments; seg++)
            {
                var i = ring * stride + seg;
                mesh.TriangleIndices.Add(i);
                mesh.TriangleIndices.Add(i + stride);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + stride);
                mesh.TriangleIndices.Add(i + stride + 1);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private static Point3D P(double x, double y, double z) => new(x, y, z);

    private static void Normalize(ref Point3D p)
    {
        var len = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        if (len < 1e-12)
            return;
        p = new Point3D(p.X / len, p.Y / len, p.Z / len);
    }

    private static int Midpoint(List<Point3D> verts, Dictionary<(int, int), int> cache, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (cache.TryGetValue(key, out var id))
            return id;

        var pa = verts[a];
        var pb = verts[b];
        var m = new Point3D((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5, (pa.Z + pb.Z) * 0.5);
        Normalize(ref m);
        id = verts.Count;
        verts.Add(m);
        cache[key] = id;
        return id;
    }

    private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        var n = Vector3D.CrossProduct(b - a, c - a);
        if (n.LengthSquared > 1e-12)
            n.Normalize();
        mesh.Normals.Add(n);
        mesh.Normals.Add(n);
        mesh.Normals.Add(n);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        AddTriangle(mesh, a, b, c);
        AddTriangle(mesh, a, c, d);
    }
}
