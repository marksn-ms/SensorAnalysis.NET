namespace SensorAnalysis.Sphere.Core.Geometry;

/// <summary>One triangular face in a frequency-n icosahedral geodesic mesh.</summary>
public sealed record GeodesicFace(
    int Id,
    int A,
    int B,
    int C,
    SphereVector Center,
    double AreaSteradians,
    double AngularRadius,
    IReadOnlyList<int> Neighbors);

/// <summary>
/// A frequency-n icosahedral geodesic polyhedron on the unit sphere.
/// It contains exactly 20n² triangular faces; face adjacency is the dual graph.
/// </summary>
public sealed class GeodesicMesh
{
    private GeodesicMesh(
        int frequency,
        IReadOnlyList<SphereVector> vertices,
        IReadOnlyList<GeodesicFace> faces,
        double characteristicEdgeRadians)
    {
        Frequency = frequency;
        Vertices = vertices;
        Faces = faces;
        CharacteristicEdgeRadians = characteristicEdgeRadians;
    }

    /// <summary>Subdivision frequency n.</summary>
    public int Frequency { get; }

    /// <summary>Unit-sphere mesh vertices.</summary>
    public IReadOnlyList<SphereVector> Vertices { get; }

    /// <summary>Triangular faces indexed by stable id.</summary>
    public IReadOnlyList<GeodesicFace> Faces { get; }

    /// <summary>Mean angular length of all unique mesh edges.</summary>
    public double CharacteristicEdgeRadians { get; }

    /// <summary>Gets the exact face count, 20n².</summary>
    public int ExpectedFaceCount => 20 * Frequency * Frequency;

    /// <summary>Builds the frequency-n icosahedral geodesic mesh.</summary>
    public static GeodesicMesh Create(int frequency)
    {
        if (frequency < 1) throw new ArgumentOutOfRangeException(nameof(frequency));

        var (baseVertices, baseFaces) = Icosahedron();
        var vertices = new List<SphereVector>();
        var vertexLookup = new Dictionary<(long X, long Y, long Z), int>();
        var rawFaces = new List<(int A, int B, int C)>();

        int AddVertex(SphereVector value)
        {
            var unit = value.Normalize();
            var key = ((long)Math.Round(unit.X * 1e12), (long)Math.Round(unit.Y * 1e12), (long)Math.Round(unit.Z * 1e12));
            if (vertexLookup.TryGetValue(key, out var id)) return id;
            id = vertices.Count;
            vertices.Add(unit);
            vertexLookup.Add(key, id);
            return id;
        }

        foreach (var (a, b, c) in baseFaces)
        {
            var local = new Dictionary<(int I, int J), int>();
            for (var i = 0; i <= frequency; i++)
            for (var j = 0; j <= frequency - i; j++)
            {
                var k = frequency - i - j;
                local[(i, j)] = AddVertex((baseVertices[a] * k + baseVertices[b] * i + baseVertices[c] * j) / frequency);
            }

            for (var i = 0; i < frequency; i++)
            for (var j = 0; j < frequency - i; j++)
            {
                rawFaces.Add((local[(i, j)], local[(i + 1, j)], local[(i, j + 1)]));
                if (j < frequency - i - 1)
                    rawFaces.Add((local[(i + 1, j)], local[(i + 1, j + 1)], local[(i, j + 1)]));
            }
        }

        var edges = new Dictionary<(int A, int B), List<int>>();
        for (var faceId = 0; faceId < rawFaces.Count; faceId++)
        {
            var face = rawFaces[faceId];
            AddEdge(face.A, face.B, faceId);
            AddEdge(face.B, face.C, faceId);
            AddEdge(face.C, face.A, faceId);
        }

        var neighbors = Enumerable.Range(0, rawFaces.Count).Select(_ => new HashSet<int>()).ToArray();
        foreach (var faceIds in edges.Values)
        {
            if (faceIds.Count != 2)
                throw new InvalidOperationException("The geodesic mesh must be a closed two-manifold.");
            neighbors[faceIds[0]].Add(faceIds[1]);
            neighbors[faceIds[1]].Add(faceIds[0]);
        }

        var faces = rawFaces.Select((face, id) =>
        {
            var center = (vertices[face.A] + vertices[face.B] + vertices[face.C]).Normalize();
            var angularRadius = Math.Max(
                SphereVector.AngularDistance(center, vertices[face.A]),
                Math.Max(SphereVector.AngularDistance(center, vertices[face.B]), SphereVector.AngularDistance(center, vertices[face.C])));
            return new GeodesicFace(
                id, face.A, face.B, face.C, center,
                SphericalTriangleArea(vertices[face.A], vertices[face.B], vertices[face.C]),
                angularRadius,
                neighbors[id].Order().ToArray());
        }).ToArray();

        if (faces.Length != 20 * frequency * frequency)
            throw new InvalidOperationException("Unexpected geodesic face count.");

        var characteristicEdge = edges.Keys.Average(edge => SphereVector.AngularDistance(vertices[edge.A], vertices[edge.B]));
        return new GeodesicMesh(frequency, vertices, faces, characteristicEdge);

        void AddEdge(int first, int second, int faceId)
        {
            var edge = first < second ? (first, second) : (second, first);
            if (!edges.TryGetValue(edge, out var faceIds))
                edges[edge] = faceIds = [];
            faceIds.Add(faceId);
        }
    }

    /// <summary>Returns the face whose spherical triangle contains the nearest face center to <paramref name="position"/>.</summary>
    public int FindNearestFace(SphereVector position) =>
        Faces.MaxBy(face => SphereVector.Dot(face.Center, position))!.Id;

    /// <summary>Runs a hop-limited breadth-first search over the dual graph.</summary>
    public IReadOnlyList<int> FacesWithinHops(int originFaceId, int hopLimit)
    {
        if (hopLimit < 0) throw new ArgumentOutOfRangeException(nameof(hopLimit));
        var visited = new HashSet<int> { originFaceId };
        var queue = new Queue<(int FaceId, int Hops)>();
        queue.Enqueue((originFaceId, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Hops == hopLimit) continue;
            foreach (var neighbor in Faces[current.FaceId].Neighbors)
                if (visited.Add(neighbor))
                    queue.Enqueue((neighbor, current.Hops + 1));
        }
        return visited.Order().ToArray();
    }

    private static double SphericalTriangleArea(SphereVector a, SphereVector b, SphereVector c)
    {
        var determinant = Math.Abs(SphereVector.Dot(a, SphereVector.Cross(b, c)));
        var denominator = 1 + SphereVector.Dot(a, b) + SphereVector.Dot(b, c) + SphereVector.Dot(c, a);
        return 2 * Math.Atan2(determinant, denominator);
    }

    private static (IReadOnlyList<SphereVector> Vertices, IReadOnlyList<(int A, int B, int C)> Faces) Icosahedron()
    {
        var phi = (1 + Math.Sqrt(5)) / 2;
        SphereVector V(double x, double y, double z) => new SphereVector(x, y, z).Normalize();
        var vertices = new[]
        {
            V(-1, phi, 0), V(1, phi, 0), V(-1, -phi, 0), V(1, -phi, 0),
            V(0, -1, phi), V(0, 1, phi), V(0, -1, -phi), V(0, 1, -phi),
            V(phi, 0, -1), V(phi, 0, 1), V(-phi, 0, -1), V(-phi, 0, 1)
        };
        var faces = new (int, int, int)[]
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)
        };
        return (vertices, faces);
    }
}
