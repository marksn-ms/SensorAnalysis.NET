using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.Core.World;

/// <summary>
/// A cubic 3-torus: agents, bodies, incidents, and a voxel grid.
/// Positions live in <c>[−HalfExtent, HalfExtent)</c> on each axis. Deterministic given a seed.
/// </summary>
public sealed class SimWorld
{
    private readonly List<Agent> _agents = [];
    private readonly List<Body> _bodies = [];
    private readonly List<Incident> _incidents = [];
    private readonly List<WorldObject> _all = [];
    private readonly List<WorldMessage> _messages = [];
    private readonly List<Directive> _directives = [];
    private readonly Tile[,,] _tiles;
    private readonly Random _rng;

    /// <summary>Creates a world from options and a deterministic seed, then runs <see cref="WorldInitializer"/>.</summary>
    /// <param name="options">Cube size, initializer kind, and related configuration.</param>
    /// <param name="seed">RNG seed used for world randomness.</param>
    public SimWorld(WorldOptions options, int seed)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CubesPerAxis < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "CubesPerAxis must be ≥ 1.");
        if (options.CubeSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "CubeSize must be positive.");

        Options = options;
        Seed = seed;
        _rng = new Random(seed);
        _tiles = BuildTiles(options);
        Title = options.Title;
        WorldInitializer.Populate(this);
    }

    /// <summary>Gets the configuration used to construct this world.</summary>
    public WorldOptions Options { get; }

    /// <summary>Gets the RNG seed supplied at construction.</summary>
    public int Seed { get; }

    /// <summary>Gets the shared deterministic RNG for the world and everything it hosts.</summary>
    public Random Random => _rng;

    /// <summary>Gets or sets the human-readable world title.</summary>
    public string Title { get; set; }

    /// <summary>Gets the zero-based tick index advanced by <see cref="Tick"/>.</summary>
    public long CurrentTick { get; private set; }

    /// <summary>Gets the world edge length.</summary>
    public double Extent => Options.Extent;

    /// <summary>Gets agents currently in the world.</summary>
    public IReadOnlyList<Agent> Agents => _agents;

    /// <summary>Gets celestial bodies and debris.</summary>
    public IReadOnlyList<Body> Bodies => _bodies;

    /// <summary>Gets incidents currently in the world.</summary>
    public IReadOnlyList<Incident> Incidents => _incidents;

    /// <summary>Gets every object (bodies, agents, incidents) in insertion order.</summary>
    public IReadOnlyList<WorldObject> AllObjects => _all;

    /// <summary>Gets recorded message / radio log lines (oldest first).</summary>
    public IReadOnlyList<WorldMessage> Messages => _messages;

    /// <summary>Gets tick-scheduled directives.</summary>
    public IReadOnlyList<Directive> Directives => _directives;

    /// <summary>Gets a value indicating whether coverage samples should appear in live metrics.</summary>
    public bool CoverageWatcherEnabled { get; private set; }

    /// <summary>Gets the cubic tile grid indexed by <c>[ix, iy, iz]</c>.</summary>
    public Tile[,,] Tiles => _tiles;

    /// <summary>Adds an agent to the world.</summary>
    /// <param name="agent">Agent instance to add.</param>
    public void Add(Agent agent)
    {
        _agents.Add(agent);
        _all.Add(agent);
    }

    /// <summary>Adds a body to the world.</summary>
    /// <param name="body">Body instance to add.</param>
    public void Add(Body body)
    {
        _bodies.Add(body);
        _all.Add(body);
    }

    /// <summary>Adds an incident to the world.</summary>
    /// <param name="incident">Incident instance to add.</param>
    public void Add(Incident incident)
    {
        _incidents.Add(incident);
        _all.Add(incident);
    }

    /// <summary>Appends a line to the message log at the current tick.</summary>
    /// <param name="text">Human-readable payload.</param>
    public void Record(string text) => _messages.Add(new WorldMessage(CurrentTick, text));

    /// <summary>Queues a directive and runs it immediately when <see cref="Directive.AtTick"/> is already due.</summary>
    /// <param name="directive">Directive to add.</param>
    public void AddDirective(Directive directive)
    {
        ArgumentNullException.ThrowIfNull(directive);
        _directives.Add(directive);
        if (!directive.Applied && directive.AtTick <= CurrentTick)
            ApplyDirective(directive);
    }

    /// <summary>Removes a queued directive. No-op if it is not in the list.</summary>
    /// <param name="directive">Directive to remove.</param>
    public void RemoveDirective(Directive directive) => _directives.Remove(directive);

    /// <summary>Finds any object by runtime id, or <c>null</c> if not present.</summary>
    /// <param name="id">Runtime identifier.</param>
    /// <returns>The matching object, if any.</returns>
    public WorldObject? FindObject(int id) => _all.FirstOrDefault(o => o.Id == id);

    /// <summary>Wraps a point onto the primary cell.</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>Equivalent point in <c>[−Half, Half)³</c>.</returns>
    public Vec3 Wrap(Vec3 p) => Vec3.Wrap(p, Extent);

    /// <summary>Shortest toroidal displacement from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public Vec3 Displacement(Vec3 from, Vec3 to) => Vec3.ToroidalDisplacement(from, to, Extent);

    /// <summary>Shortest toroidal distance between two points.</summary>
    public double Distance(Vec3 a, Vec3 b) => Vec3.ToroidalDistance(a, b, Extent);

    /// <summary>Returns the cube containing <paramref name="p"/> (coordinates are wrapped first).</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>The containing <see cref="Tile"/>.</returns>
    public Tile TileAt(Vec3 p)
    {
        p = Wrap(p);
        var n = Options.CubesPerAxis;
        var size = Options.CubeSize;
        var half = Options.HalfExtent;
        var ix = IndexFromCoord(p.X, half, size, n);
        var iy = IndexFromCoord(p.Y, half, size, n);
        var iz = IndexFromCoord(p.Z, half, size, n);
        return _tiles[ix, iy, iz];
    }

    /// <summary>Objects whose centers currently lie in <paramref name="tile"/>.</summary>
    /// <param name="tile">Voxel to query.</param>
    /// <returns>Occupants in that cube.</returns>
    public IEnumerable<WorldObject> Occupants(Tile tile)
    {
        foreach (var obj in _all)
        {
            var t = TileAt(obj.Position);
            if (t.Ix == tile.Ix && t.Iy == tile.Iy && t.Iz == tile.Iz)
                yield return obj;
        }
    }

    /// <summary>Walks cubes along a ray up to <paramref name="maxDistance"/> (Amanatides–Woo, one primary cell plus a margin).</summary>
    /// <param name="origin">Ray origin in world space.</param>
    /// <param name="direction">Ray direction (normalized internally).</param>
    /// <param name="maxDistance">Maximum walk distance in world units.</param>
    /// <returns>Tiles in visit order (duplicates possible after wrap).</returns>
    public IReadOnlyList<Tile> TilesAlongRay(Vec3 origin, Vec3 direction, double maxDistance)
    {
        var dir = direction.Normalize();
        if (dir.LengthSquared < 0.5 || maxDistance <= 0)
            return [];

        var n = Options.CubesPerAxis;
        var size = Options.CubeSize;
        var visited = new List<Tile>();
        var seen = new HashSet<(int, int, int)>();
        var pos = origin;
        var remaining = maxDistance;
        const int maxSteps = 512;

        for (var step = 0; step < maxSteps && remaining > 0; step++)
        {
            var tile = TileAt(pos);
            var key = (tile.Ix, tile.Iy, tile.Iz);
            if (seen.Add(key))
                visited.Add(tile);

            var tExit = DistanceToVoxelExit(pos, dir, size, Options.HalfExtent);
            var advance = Math.Min(remaining, Math.Max(tExit, 1e-6) + 1e-5);
            pos += dir * advance;
            remaining -= advance;
        }

        return visited;
    }

    /// <summary>First cube along the ray, including empty cubes.</summary>
    /// <param name="origin">Ray origin.</param>
    /// <param name="direction">Ray direction.</param>
    /// <param name="maxDistance">Maximum walk distance.</param>
    /// <returns>The first tile, or <c>null</c> when the ray is degenerate.</returns>
    public Tile? FirstTileAlongRay(Vec3 origin, Vec3 direction, double maxDistance)
    {
        var tiles = TilesAlongRay(origin, direction, maxDistance);
        return tiles.Count > 0 ? tiles[0] : null;
    }

    /// <summary>Builds the local omnidirectional observation for an agent.</summary>
    /// <param name="self">Observing agent.</param>
    /// <returns>Neighbors and incidents within <see cref="Agent.SensorRange"/>.</returns>
    public AgentObservation Observe(Agent self)
    {
        var origin = self.Position;
        var range = self.SensorRange;
        var agents = _agents
            .Where(a => a.Id != self.Id && Distance(a.Position, origin) <= range)
            .Select(a => new AgentInfo(a.Id, Displacement(origin, a.Position), a.Velocity, a.AgentType))
            .ToList();
        var bodies = _bodies
            .Where(b => Distance(b.Position, origin) <= range)
            .Select(b => new BodyInfo(b.Id, Displacement(origin, b.Position), b.VisualRadius, b.Mu, b.ObjectType))
            .ToList();
        var incidents = _incidents
            .Where(i => !i.IsDepleted && Distance(i.Position, origin) <= range)
            .Select(i => new IncidentInfo(i.Id, Displacement(origin, i.Position), i.Intensity))
            .ToList();
        return new AgentObservation(origin, CurrentTick, agents, bodies, incidents);
    }

    /// <summary>Fraction of cubes that have been covered by an agent at least once.</summary>
    public double CoverageFraction
    {
        get
        {
            var n = Options.CubesPerAxis;
            var covered = 0;
            for (var x = 0; x < n; x++)
            for (var y = 0; y < n; y++)
            for (var z = 0; z < n; z++)
            {
                if (_tiles[x, y, z].LastCoveredTick >= 0)
                    covered++;
            }

            return covered / (double)(n * n * n);
        }
    }

    /// <summary>Advances the world by one tick: agents decide, then physics, drain, and coverage.</summary>
    public void Tick()
    {
        var ctx = new WorldContext
        {
            Tick = CurrentTick,
            World = this,
            Rng = _rng
        };

        foreach (var directive in _directives)
        {
            if (!directive.Applied && directive.AtTick <= CurrentTick)
                ApplyDirective(directive);
        }

        foreach (var agent in _agents)
            agent.Tick(ctx);

        Physics.Step(this);

        DrainIncidents();

        foreach (var agent in _agents)
            TileAt(agent.Position).LastCoveredTick = CurrentTick;

        CurrentTick++;
    }

    private void ApplyDirective(Directive directive)
    {
        switch (directive.Kind)
        {
            case DirectiveKind.DeployUniformAgents:
                WorldInitializer.SpawnRandomWalkers(this, Math.Max(0, directive.Count));
                Record($"Deployed {directive.Count} RandomWalk agent(s).");
                break;
            case DirectiveKind.DeployAsteroids:
                WorldInitializer.SpawnDriftAsteroids(this, Math.Max(0, directive.Count));
                Record($"Deployed {directive.Count} asteroid(s).");
                break;
            case DirectiveKind.EnableCoverageWatcher:
                CoverageWatcherEnabled = true;
                Record("Coverage watcher enabled.");
                break;
        }

        directive.Applied = true;
    }

    private void DrainIncidents()
    {
        var maxDrainers = Math.Max(1, Options.MaxDrainingAgentsPerIncident);
        foreach (var incident in _incidents)
        {
            if (incident.IsDepleted)
                continue;

            var covering = _agents
                .Where(a => a.ContributesToIncidentDrain && Distance(a.Position, incident.Position) <= a.SensorRange)
                .OrderBy(a => Distance(a.Position, incident.Position))
                .Take(maxDrainers)
                .Count();
            if (covering <= 0)
                continue;

            incident.Intensity = Math.Max(0, incident.Intensity - incident.DrainPerAgentPerTick * covering);
        }
    }

    private static int IndexFromCoord(double coord, double half, double size, int n)
    {
        var u = coord + half;
        var i = (int)Math.Floor(u / size);
        if (i < 0)
            i = 0;
        if (i >= n)
            i = n - 1;
        return i;
    }

    private static double DistanceToVoxelExit(Vec3 pos, Vec3 dir, double size, double half)
    {
        var t = double.PositiveInfinity;
        t = Math.Min(t, AxisExit(pos.X, dir.X, size, half));
        t = Math.Min(t, AxisExit(pos.Y, dir.Y, size, half));
        t = Math.Min(t, AxisExit(pos.Z, dir.Z, size, half));
        return double.IsInfinity(t) ? size : t;
    }

    private static double AxisExit(double coord, double dir, double size, double half)
    {
        if (Math.Abs(dir) < 1e-12)
            return double.PositiveInfinity;

        var u = coord + half;
        var local = u - Math.Floor(u / size) * size;
        if (dir > 0)
            return (size - local) / dir;
        return local / -dir;
    }

    private static Tile[,,] BuildTiles(WorldOptions options)
    {
        var n = options.CubesPerAxis;
        var size = options.CubeSize;
        var half = options.HalfExtent;
        var tiles = new Tile[n, n, n];
        for (var ix = 0; ix < n; ix++)
        for (var iy = 0; iy < n; iy++)
        for (var iz = 0; iz < n; iz++)
        {
            var center = new Vec3(
                (ix + 0.5) * size - half,
                (iy + 0.5) * size - half,
                (iz + 0.5) * size - half);
            tiles[ix, iy, iz] = new Tile(ix, iy, iz, center);
        }

        return tiles;
    }
}
