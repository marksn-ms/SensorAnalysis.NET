using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Metrics;

namespace SensorAnalysis.Core.World;

/// <summary>
/// The simulated environment: grid of tiles, agents, incidents, and messages.
/// Deterministic given a fixed seed and identical agent decisions.
/// </summary>
public sealed class SimWorld
{
    private readonly List<Agent> _agents = new();
    private readonly List<Incident> _incidents = new();
    private readonly List<Message> _messages = new();
    private readonly Tile[,] _tiles;
    private readonly Random _rng;

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

    /// <summary>Removes all agents, incidents, and messages (tiles are retained).</summary>
    public void ClearEntities()
    {
        _agents.Clear();
        _incidents.Clear();
        _messages.Clear();
    }

    /// <summary>Clamps a point to the inclusive world bounds.</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>A point guaranteed to lie inside the world.</returns>
    public Vec2 Clamp(Vec2 p) => new(
        Math.Clamp(p.X, 0, WorldWidthPx - 1),
        Math.Clamp(p.Y, 0, WorldHeightPx - 1));

    /// <summary>Returns the tile containing <paramref name="p"/>, or <c>null</c> if out of bounds.</summary>
    /// <param name="p">Point in world coordinates.</param>
    /// <returns>The containing <see cref="Tile"/>, or <c>null</c>.</returns>
    public Tile? TileAt(Vec2 p)
    {
        var col = (int)(p.X / Options.TileSizePx.Width);
        var row = (int)(p.Y / Options.TileSizePx.Height);
        if (col < 0 || row < 0 || col >= Options.TileCount.Width || row >= Options.TileCount.Height)
            return null;
        return _tiles[col, row];
    }

    /// <summary>Build the local observation for an agent (sensor-range limited).</summary>
    /// <param name="self">Agent for whom the observation is built.</param>
    /// <returns>An <see cref="AgentObservation"/> including computed instantaneous reward.</returns>
    public AgentObservation Observe(Agent self)
    {
        var range = self.SensorRange;
        var nearbyAgents = _agents
            .Where(a => a.Id != self.Id && Vec2.Distance(a.Position, self.Position) <= range)
            .Select(a => new AgentInfo(a.Id, a.Position, a.AgentType))
            .ToList();

        var nearbyIncidents = _incidents
            .Where(i => !i.IsDepleted && Vec2.Distance(i.Position, self.Position) <= range)
            .Select(i => new IncidentInfo(i.Id, i.Position, i.Intensity))
            .ToList();

        var nearbyMessages = _messages
            .Where(m => Vec2.Distance(m.Position, self.Position) <= range)
            .Select(m => new MessageInfo(m.Id, m.Position, m.FromAgentType, m.Payload, m.Ttl))
            .ToList();

        var obs = new AgentObservation
        {
            Tick = CurrentTick,
            SelfPosition = self.Position,
            NearbyAgents = nearbyAgents,
            NearbyIncidents = nearbyIncidents,
            NearbyMessages = nearbyMessages,
            InstantReward = 0
        };
        // Compute reward with full world context (coverage bonus needs tile state)
        return obs with { InstantReward = RewardModel.Evaluate(self, obs, this) };
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

        // 1. Agents decide and move
        foreach (var agent in _agents)
            agent.Tick(ctx);

        // 2. Drain incidents near agents
        foreach (var incident in _incidents)
        {
            if (incident.IsDepleted) continue;
            var covering = _agents.Count(a =>
                Vec2.Distance(a.Position, incident.Position) <= a.SensorRange);
            incident.Intensity = Math.Max(0, incident.Intensity - covering * incident.DrainPerAgentPerTick);
        }

        // 3. Tick messages, drop expired
        foreach (var msg in _messages)
            msg.Tick(ctx);
        _messages.RemoveAll(m => m.IsExpired);

        // 4. Mark tiles covered by agents
        foreach (var agent in _agents)
        {
            var tile = TileAt(agent.Position);
            if (tile is not null)
                tile.LastCoveredTick = CurrentTick;
        }

        CurrentTick++;
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
}
