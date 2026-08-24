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
    }

    /// <summary>Configuration used to construct this world.</summary>
    public WorldOptions Options { get; }

    /// <summary>RNG seed supplied at construction.</summary>
    public int Seed { get; }

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

    /// <summary>
    /// Registered deployers ticked before agents each step (immediate then dormant, or periodic).
    /// </summary>
    public IReadOnlyList<IDeployer> Deployers => _deployers;

    /// <summary>
    /// Global radio traffic log (narrated Transmit events). Cleared with entities on demand by the UI.
    /// </summary>
    public RadioTrafficLog Radio => _radio;

    /// <summary>Two-dimensional tile grid indexed by <c>[col, row]</c>.</summary>
    public Tile[,] Tiles => _tiles;

    /// <summary>World width in pixels derived from tile count and tile size.</summary>
    public double WorldWidthPx => Options.TileCount.Width * Options.TileSizePx.Width;

    /// <summary>World height in pixels derived from tile count and tile size.</summary>
    public double WorldHeightPx => Options.TileCount.Height * Options.TileSizePx.Height;

    /// <summary>Adds an agent to the world.</summary>
    /// <param name="agent">Agent instance to add.</param>
    public void Add(Agent agent) => _agents.Add(agent);

    /// <summary>Adds an incident to the world.</summary>
    /// <param name="incident">Incident instance to add.</param>
    public void Add(Incident incident) => _incidents.Add(incident);

    /// <summary>Adds a message / marker to the world.</summary>
    /// <param name="message">Message instance to add.</param>
    public void Add(Message message) => _messages.Add(message);

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

    /// <summary>Removes all agents, incidents, and messages (tiles and deployers are retained).</summary>
    public void ClearEntities()
    {
        _agents.Clear();
        _incidents.Clear();
        _messages.Clear();
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
        var range = self.SensorRange;
        var nearbyAgents = _agents
            .Where(a => a.Id != self.Id && Distance(a.Position, self.Position) <= range)
            .Select(a => new AgentInfo(a.Id, a.Position, a.AgentType))
            .ToList();

        var nearbyIncidents = SenseIncidents(self, range);

        // Decision-facing messages come from the radio inbox (not lingering environmental reads).
        var nearbyMessages = self.DrainInbox();

        var obs = new AgentObservation
        {
            Tick = CurrentTick,
            SelfPosition = self.Position,
            WorldWidth = WorldWidthPx,
            WorldHeight = WorldHeightPx,
            NearbyAgents = nearbyAgents,
            NearbyIncidents = nearbyIncidents,
            NearbyMessages = nearbyMessages,
            InstantReward = 0
        };
        // Compute reward with full world context (coverage bonus needs tile state)
        return obs with { InstantReward = RewardModel.Evaluate(self, obs, this) };
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

        CurrentTick++;
    }

    private List<IncidentInfo> SenseIncidents(Agent self, double range)
    {
        var list = new List<IncidentInfo>();
        foreach (var incident in _incidents)
        {
            if (incident.IsDepleted)
                continue;

            if (Options.ExactSensing)
            {
                if (Distance(incident.Position, self.Position) <= range)
                    list.Add(new IncidentInfo(incident.Id, incident.Position, incident.Intensity, 1.0));
                continue;
            }

            var profile = incident.Sensing ?? Options.Sensing;
            if (CueSensor.TrySense(
                    incident, self, CurrentTick, Seed, profile,
                    WorldWidthPx, WorldHeightPx, out var cue))
            {
                list.Add(cue);
            }
        }

        return list;
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
    /// Aligns with honest squad <c>MaxOrbiters</c> (default 3). Extra agents in range do not accelerate clearance.
    /// </summary>
    public int MaxDrainingAgentsPerIncident { get; set; } = 3;
}
