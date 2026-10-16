using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.Sensing;

namespace SensorAnalysis.Core.World;

/// <summary>
/// The simulated environment: grid of tiles, agents, incidents, messages, and ticking deployers.
/// Deterministic given a fixed seed and identical agent decisions.
/// </summary>
public sealed class SimWorld
{
    private readonly List<Agent> _agents = new();
    private readonly List<Incident> _incidents = new();
    private readonly List<Message> _messages = new();
    private readonly List<Body> _bodies = new();
    private readonly List<IDeployer> _deployers = new();
    private readonly Tile[,] _tiles;
    private readonly Random _rng;
    private readonly RadioTrafficLog _radio = new();

    /// <summary>
    /// Creates a new world from options and a deterministic seed.
    /// </summary>
    /// <param name="options">Grid size, title, and related configuration.</param>
    /// <param name="seed">RNG seed used for world randomness.</param>
    /// <param name="rewardModel">Optional reward model; a default instance is used when <c>null</c>.</param>
    public SimWorld(WorldOptions options, int seed, RewardModel? rewardModel = null)
    {
        Options = options;
        Seed = seed;
        _rng = new Random(seed);
        _tiles = BuildTiles(options);
        Title = options.Title;
        RewardModel = rewardModel ?? new RewardModel();
        Pheromones = new PheromoneField(WorldWidthPx, WorldHeightPx);
    }

    /// <summary>Configuration used to construct this world.</summary>
    public WorldOptions Options { get; }

    /// <summary>RNG seed supplied at construction.</summary>
    public int Seed { get; }

    /// <summary>Shared deterministic RNG for the world and everything it hosts.</summary>
    public Random Random => _rng;

    /// <summary>Human-readable world title.</summary>
    public string Title { get; set; }

    /// <summary>Zero-based tick index advanced by <see cref="Tick"/>.</summary>
    public long CurrentTick { get; private set; }

    /// <summary>Zero-based trial index set by experiment runners.</summary>
    public int CurrentTrial { get; set; }

    /// <summary>Reward model used when building agent observations.</summary>
    public RewardModel RewardModel { get; }

    /// <summary>Agents currently in the world.</summary>
    public IReadOnlyList<Agent> Agents => _agents;

    /// <summary>Incidents currently in the world.</summary>
    public IReadOnlyList<Incident> Incidents => _incidents;

    /// <summary>Messages / markers currently in the world.</summary>
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Fixed-size inhabitants (walls, rocks). Agents remain points.</summary>
    public IReadOnlyList<Body> Bodies => _bodies;

    /// <summary>
    /// Registered deployers ticked before agents each step (immediate then dormant, or periodic).
    /// </summary>
    public IReadOnlyList<IDeployer> Deployers => _deployers;

    /// <summary>
    /// Global radio traffic log (narrated Transmit events). Cleared with entities on demand by the UI.
    /// </summary>
    public RadioTrafficLog Radio => _radio;

    /// <summary>
    /// Unsigned decaying digital-pheromone field (no speaker id). Fresh per <see cref="SimWorld"/> instance.
    /// </summary>
    public PheromoneField Pheromones { get; }

    /// <summary>Two-dimensional tile grid indexed by <c>[col, row]</c>.</summary>
    public Tile[,] Tiles => _tiles;

    /// <summary>World width in pixels derived from tile count and tile size.</summary>
    public double WorldWidthPx => Options.TileCount.Width * Options.TileSizePx.Width;

    /// <summary>World height in pixels derived from tile count and tile size.</summary>
    public double WorldHeightPx => Options.TileCount.Height * Options.TileSizePx.Height;

    /// <summary>Adds an agent to the world.</summary>
    /// <param name="agent">Agent instance to add.</param>
    public void Add(Agent agent)
    {
        agent.InitializeTiming(_rng);
        _agents.Add(agent);
    }

    /// <summary>Adds an incident to the world.</summary>
    /// <param name="incident">Incident instance to add.</param>
    public void Add(Incident incident) => _incidents.Add(incident);

    /// <summary>Adds a message / marker to the world.</summary>
    /// <param name="message">Message instance to add.</param>
    public void Add(Message message) => _messages.Add(message);

    /// <summary>Adds a fixed-size body (wall segment, disc, or other fixture).</summary>
    /// <param name="body">Body instance to add.</param>
    public void Add(Body body) => _bodies.Add(body);

    /// <summary>
    /// Registers a deployer to receive <see cref="IDeployer.Tick"/> before agents each world tick.
    /// </summary>
    /// <param name="deployer">Deployer already configured via <see cref="IDeployer.Attach"/>.</param>
    public void AddDeployer(IDeployer deployer) => _deployers.Add(deployer);

    /// <summary>Removes all registered deployers (does not clear agents/incidents).</summary>
    public void ClearDeployers() => _deployers.Clear();

    /// <summary>Lookup an agent by runtime id, or <c>null</c> if not present.</summary>
    public Agent? FindAgent(int id) => _agents.FirstOrDefault(a => a.Id == id);

    /// <summary>Active (non-depleted) incidents — used by privileged oracles and telemetry.</summary>
    public IEnumerable<Incident> ActiveIncidents => _incidents.Where(i => !i.IsDepleted);

    /// <summary>Removes all agents, incidents, messages, and bodies (tiles and deployers are retained).</summary>
    public void ClearEntities()
    {
        _agents.Clear();
        _incidents.Clear();
        _messages.Clear();
        _bodies.Clear();
        Pheromones.Clear();
    }

    /// <summary>
    /// Wraps a point onto the toroidal world (leaving one edge enters the opposite edge).
    /// </summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>Equivalent point inside <c>[0, width) × [0, height)</c>.</returns>
    public Vec2 Wrap(Vec2 p) => Vec2.Wrap(p, WorldWidthPx, WorldHeightPx);

    /// <summary>Shortest distance between two points on this toroidal world.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Toroidal distance in world units.</returns>
    public double Distance(Vec2 a, Vec2 b) =>
        Vec2.ToroidalDistance(a, b, WorldWidthPx, WorldHeightPx);

    /// <summary>Shortest signed displacement from <paramref name="from"/> to <paramref name="to"/> on this torus.</summary>
    /// <param name="from">Origin.</param>
    /// <param name="to">Destination.</param>
    /// <returns>Minimum-image displacement vector.</returns>
    public Vec2 Displacement(Vec2 from, Vec2 to) =>
        Vec2.ToroidalDisplacement(from, to, WorldWidthPx, WorldHeightPx);

    /// <summary>Returns entities whose positions fall inside the axis-aligned tile rectangle.</summary>
    /// <param name="tile">Tile to query.</param>
    /// <returns>Agents, active incidents, and messages currently in the tile.</returns>
    public IEnumerable<WorldObject> ObjectsInTile(Tile tile)
    {
        var tw = Options.TileSizePx.Width;
        var th = Options.TileSizePx.Height;
        var x0 = tile.Col * tw;
        var y0 = tile.Row * th;
        var x1 = x0 + tw;
        var y1 = y0 + th;

        bool Inside(Vec2 p)
        {
            p = Wrap(p);
            return p.X >= x0 && p.X < x1 && p.Y >= y0 && p.Y < y1;
        }

        foreach (var a in _agents)
            if (Inside(a.Position)) yield return a;
        foreach (var i in _incidents)
            if (!i.IsDepleted && Inside(i.Position)) yield return i;
        foreach (var m in _messages)
            if (Inside(m.Position)) yield return m;
        foreach (var b in _bodies)
        {
            if (b.IntersectsAabb(x0, y0, x1, y1, WorldWidthPx, WorldHeightPx))
                yield return b;
        }
    }

    /// <summary>Returns the tile containing <paramref name="p"/> (coordinates are wrapped first).</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>The containing <see cref="Tile"/>.</returns>
    public Tile TileAt(Vec2 p)
    {
        p = Wrap(p);
        var col = Math.Min(Options.TileCount.Width - 1, (int)(p.X / Options.TileSizePx.Width));
        var row = Math.Min(Options.TileCount.Height - 1, (int)(p.Y / Options.TileSizePx.Height));
        return _tiles[col, row];
    }

    /// <summary>Build the local observation for an agent (sensor-range limited).</summary>
    /// <param name="self">Agent for whom the observation is built.</param>
    /// <returns>An <see cref="AgentObservation"/> including computed instantaneous reward.</returns>
    public AgentObservation Observe(Agent self)
    {
        var origin = self.Position;
        var range = self.SensorRange;
        var nearbyAgents = _agents
            .Where(a => a.Id != self.Id && Distance(a.Position, origin) <= range)
            .Select(a => new AgentInfo(
                a.Id,
                Displacement(origin, a.Position),
                a.LastAction.ToStep() * a.Velocity,
                a.LastAction,
                a.AgentType))
            .ToList();

        var nearbyIncidents = SenseIncidents(self, origin, range);
        var nearbyMessages = self.DrainInbox()
            .Select(m => MessageInfo.ForObserver(m, Displacement(origin, m.Position), self.Id))
            .ToList();
        var nearbyBodies = SenseBodies(origin, range);

        var obs = new AgentObservation(
            this,
            origin,
            CurrentTick,
            nearbyAgents,
            nearbyIncidents,
            nearbyMessages,
            nearbyBodies);

        obs.InstantReward = RewardModel.Evaluate(self, obs, this);
        return obs;
    }

    /// <summary>
    /// Transmit a radio message from <paramref name="sender"/> to in-range agents' inboxes.
    /// A short-lived world echo is retained for visualization/metrics only; agents decide
    /// from inbox deliveries, not by "smelling" the echo.
    /// </summary>
    /// <param name="sender">Transmitting agent.</param>
    /// <param name="payload">Opaque payload (e.g. incident claim).</param>
    /// <param name="ttl">World-echo TTL for UI/metrics.</param>
    /// <param name="toAgentId">Optional addressee; <c>null</c> = broadcast.</param>
    public void Transmit(Agent sender, string payload, int ttl = 50, int? toAgentId = null)
    {
        var message = new Message
        {
            Position = sender.Position,
            FromAgentType = sender.AgentType,
            FromAgentId = sender.Id,
            ToAgentId = toAgentId,
            Payload = payload,
            Ttl = ttl,
            InitialTtl = ttl
        };
        Add(message);

        _radio.Record(
            CurrentTick,
            message.Id,
            message.FromAgentId,
            message.FromAgentType,
            message.Payload,
            sender.Position,
            sender.SensorRange);

        var info = new MessageInfo(
            message.Id,
            message.Position,
            message.FromAgentType,
            message.FromAgentId,
            message.ToAgentId,
            message.Payload,
            message.Ttl);

        var range = sender.EffectiveCommRange;
        foreach (var agent in _agents)
        {
            if (agent.Id == sender.Id) continue;
            if (Distance(agent.Position, sender.Position) > range) continue;
            agent.Receive(info);
        }
    }

    /// <summary>Advance the world by one tick.</summary>
    public void Tick()
    {
        var ctx = new WorldContext
        {
            Tick = CurrentTick,
            Trial = CurrentTrial,
            World = this,
            Rng = _rng
        };

        // 0. Deployers act first (waves / dormant immediate no-ops)
        foreach (var deployer in _deployers)
        {
            if (!deployer.IsDormant)
                deployer.Tick(ctx);
        }

        // 1. Agents decide and move
        foreach (var agent in _agents)
            agent.Tick(ctx);

        // 1b. Anchored bodies keep their extent. Push penetrating agents back to the surface.
        SeparateAgentsFromBodies();

        // 2. Drain incidents near agents (closest N contributors only)
        var maxDrainers = Math.Max(1, Options.MaxDrainingAgentsPerIncident);
        foreach (var incident in _incidents)
        {
            if (incident.IsDepleted) continue;
            var covering = _agents
                .Where(a =>
                    a.ContributesToIncidentDrain &&
                    Distance(a.Position, incident.Position) <= a.SensorRange)
                .Select(a => Distance(a.Position, incident.Position))
                .OrderBy(d => d)
                .Take(maxDrainers)
                .Count();
            // Superlinear multi-eye clearance among the closest orbiters only.
            var drain = covering <= 0
                ? 0
                : incident.DrainPerAgentPerTick * Math.Pow(covering, incident.MultiAgentDrainExponent);
            incident.Intensity = Math.Max(0, incident.Intensity - drain);
        }

        // 3. Tick messages, drop expired
        foreach (var msg in _messages)
            msg.Tick(ctx);
        _messages.RemoveAll(m => m.IsExpired);

        // 4. Mark tiles covered by agents
        foreach (var agent in _agents)
        {
            var tile = TileAt(agent.Position);
            tile.LastCoveredTick = CurrentTick;
        }

        // 5. Unsigned pheromone diffuses then evaporates once per tick (after deposits).
        Pheromones.Diffuse();
        Pheromones.Decay();

        CurrentTick++;
    }

    private List<IncidentInfo> SenseIncidents(Agent self, Vec2 origin, double range)
    {
        var list = new List<IncidentInfo>();
        foreach (var incident in _incidents)
        {
            if (incident.IsDepleted)
                continue;

            if (Options.ExactSensing)
            {
                if (Distance(incident.Position, origin) <= range)
                {
                    list.Add(new IncidentInfo(
                        incident.Id,
                        Displacement(origin, incident.Position),
                        incident.Intensity,
                        1.0));
                }

                continue;
            }

            var profile = incident.Sensing ?? Options.Sensing;
            if (CueSensor.TrySense(
                    incident, self, CurrentTick, Seed, profile,
                    WorldWidthPx, WorldHeightPx, out var cue))
            {
                list.Add(cue with { Position = Displacement(origin, cue.Position) });
            }
        }

        return list;
    }

    private List<BodyInfo> SenseBodies(Vec2 origin, double range)
    {
        var list = new List<BodyInfo>();
        foreach (var body in _bodies)
        {
            var clearance = body.DistanceToSurface(origin, WorldWidthPx, WorldHeightPx);
            if (clearance > range)
                continue;

            var surface = body.NearestSurfacePoint(origin, WorldWidthPx, WorldHeightPx);
            list.Add(new BodyInfo(
                body.Id,
                Displacement(origin, body.Position),
                Displacement(origin, surface),
                clearance,
                body.Radius,
                body.Length,
                body.HeadingRadians,
                body.Shape,
                body.BodyType));
        }

        return list;
    }

    /// <summary>
    /// Moves agents that have entered an anchored body back to its surface.
    /// Agent-agent overlap is left to the experiment. Worlds with no bodies are unchanged.
    /// </summary>
    private void SeparateAgentsFromBodies()
    {
        if (_bodies.Count == 0 || _agents.Count == 0)
            return;

        for (var pass = 0; pass < 4; pass++)
        {
            var moved = false;
            foreach (var agent in _agents)
            {
                foreach (var body in _bodies)
                {
                    if (!body.IsAnchored)
                        continue;

                    var clearance = body.DistanceToSurface(agent.Position, WorldWidthPx, WorldHeightPx);
                    if (clearance >= 0)
                        continue;

                    agent.Position = body.NearestSurfacePoint(agent.Position, WorldWidthPx, WorldHeightPx);
                    moved = true;
                }
            }

            if (!moved)
                break;
        }
    }

    private static Tile[,] BuildTiles(WorldOptions options)
    {
        var tiles = new Tile[options.TileCount.Width, options.TileCount.Height];
        var tw = options.TileSizePx.Width;
        var th = options.TileSizePx.Height;
        for (int c = 0; c < options.TileCount.Width; c++)
        for (int r = 0; r < options.TileCount.Height; r++)
        {
            var center = new Vec2(c * tw + tw / 2.0, r * th + th / 2.0);
            tiles[c, r] = new Tile(c, r, center);
        }
        return tiles;
    }
}

/// <summary>Configuration for creating a <see cref="SimWorld"/>.</summary>
public sealed class WorldOptions
{
    /// <summary>Human-readable title for the world. Default: "World".</summary>
    public string Title { get; set; } = "World";

    /// <summary>Number of tiles across and down. Default: 10×10.</summary>
    public GridSize TileCount { get; set; } = new(10, 10);

    /// <summary>Pixel size of each tile. Default: 50×50.</summary>
    public GridSize TileSizePx { get; set; } = new(50, 50);

    /// <summary>Tile geometry. Default: <see cref="TileShape.Rectangle"/>.</summary>
    public TileShape TileShape { get; set; } = TileShape.Rectangle;

    /// <summary>
    /// When true, agents see every in-range incident at its exact world position with its real id
    /// (legacy binary disc). When false (default), the environment emits probabilistic, noisy cues.
    /// </summary>
    public bool ExactSensing { get; set; }

    /// <summary>Default sensing profile for incidents that do not override <c>Incident.Sensing</c>.</summary>
    public IncidentSensingProfile Sensing { get; set; } = new();

    /// <summary>
    /// At most this many in-range agents may drain an incident per tick — the closest ones.
    /// Aligns with honest squad <c>MaxShareEvent</c> (default 3). Extra agents in range do not accelerate clearance.
    /// </summary>
    public int MaxDrainingAgentsPerIncident { get; set; } = 3;

    /// <summary>
    /// Intensity removed per draining agent per tick (then scaled by the multi-agent exponent).
    /// Default 0.01 ⇒ ~100 ticks for a solo cover of intensity 1.0.
    /// </summary>
    public double DrainPerAgentPerTick { get; set; } = 0.01;
}
