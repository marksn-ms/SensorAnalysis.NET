using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="PlaceNearDeployer"/>.</summary>
public sealed class PlaceNearOptions
{
    /// <summary>Incidents per wave (or once when <see cref="PeriodTicks"/> is 0).</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Host agent type to place near (must already be in the world).</summary>
    [DeployerParam("Near agent type", IsAgentType = true,
        Description = "Incidents land near living agents of this type.")]
    public string NearTypeName { get; set; } = "HerdingDeceptive";

    /// <summary>
    /// Placement radius as a fraction of the host's sensor range (e.g. 0.5 = half range).
    /// </summary>
    [DeployerParam("Range factor",
        Description = "Radius = factor × host SensorRange (with light jitter).")]
    public double RangeFactor { get; set; } = 0.5;

    /// <summary>
    /// When 0, place once in <c>Attach</c> then go dormant.
    /// When &gt; 0, spawn on ticks 0, N, 2N, ….
    /// </summary>
    [DeployerParam("Period (ticks)", Required = false,
        Description = "0 = one-shot; otherwise periodic waves.")]
    public int PeriodTicks { get; set; }

    /// <summary>Logical incident kind for styling / filtering (default <c>Incident</c>).</summary>
    [DeployerParam("Incident type", IsIncidentType = true, Required = false)]
    public string IncidentType { get; set; } = "Incident";

    /// <summary>
    /// When true, shuffle hosts once per trial and cycle through them (preferring
    /// hosts that do not already cover an active incident) instead of picking
    /// uniformly at random each spawn.
    /// </summary>
    [DeployerParam("Round-robin hosts", Required = false,
        Description = "Cycle shuffled hosts; skip hosts that already have a nearby incident.")]
    public bool RoundRobinHosts { get; set; }
}

/// <summary>
/// Places incidents near living agents of a chosen type at a configurable fraction of their sensor range.
/// One-shot when <see cref="PlaceNearOptions.PeriodTicks"/> is 0; otherwise periodic.
/// Register after the host agents have been deployed (same tick is fine if this step runs later in the list).
/// </summary>
public sealed class PlaceNearDeployer : IDeployer
{
    private SimWorld? _world;
    private PlaceNearOptions? _options;
    private int _randomSalt;
    private int _waveIndex;
    private bool _dormant = true;
    private int[]? _hostOrder;
    private int _hostCursor;

    /// <inheritdoc />
    public string Name => "PlaceNear";

    /// <inheritdoc />
    public string Description =>
        "Incidents near a host agent type at RangeFactor × sensor range (one-shot or periodic).";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(PlaceNearOptions);

    /// <inheritdoc />
    public object CreateOptions() => new PlaceNearOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        _options = (PlaceNearOptions)options;
        _world = world;
        _randomSalt = context.RandomSalt;
        _waveIndex = 0;
        _hostOrder = null;
        _hostCursor = 0;
        if (_options.Count < 1) _options.Count = 1;
        if (_options.RangeFactor < 0) _options.RangeFactor = 0;

        if (_options.PeriodTicks <= 0)
        {
            SpawnWave(world, context.RandomSalt, waveIndex: 0);
            _dormant = true;
        }
        else
        {
            _dormant = false;
        }
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx)
    {
        if (_dormant || _world is null || _options is null) return;
        if (ctx.World != _world) return;
        if (ctx.Tick % _options.PeriodTicks != 0) return;

        SpawnWave(_world, unchecked(_randomSalt ^ (int)ctx.Tick * 7919), _waveIndex);
        _waveIndex++;
    }

    private void SpawnWave(SimWorld world, int salt, int waveIndex)
    {
        var o = _options!;
        var rng = new Random(unchecked(world.Seed ^ salt ^ waveIndex * 104729));
        for (int i = 0; i < o.Count; i++)
        {
            var position = o.RoundRobinHosts
                ? PlaceRoundRobin(world, rng, o)
                : IncidentPlacementHelper.PlaceNearType(
                    world, rng, o.NearTypeName, o.RangeFactor);
            world.Add(new Incident
            {
                Position = position,
                Intensity = 1.0,
                IncidentType = string.IsNullOrWhiteSpace(o.IncidentType) ? "Incident" : o.IncidentType,
                Label = $"I-near-{waveIndex}-{i}"
            });
        }
    }

    private Vec2 PlaceRoundRobin(SimWorld world, Random rng, PlaceNearOptions o)
    {
        var hosts = world.Agents
            .Where(a => string.Equals(a.AgentType, o.NearTypeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hosts.Count == 0)
            return IncidentPlacementHelper.PlaceUniform(world, rng);

        EnsureHostOrder(hosts, rng);
        var free = hosts.Where(h =>
            IncidentPlacementHelper.HostIsFreeOfIncidents(world, h, o.RangeFactor)).ToList();
        var pool = free.Count > 0 ? free : hosts;

        var host = NextHostInOrder(pool) ?? pool[rng.Next(pool.Count)];
        return IncidentPlacementHelper.PlaceNearAgent(world, rng, host, o.RangeFactor);
    }

    private void EnsureHostOrder(List<Agent> hosts, Random rng)
    {
        var ids = hosts.Select(h => h.Id).OrderBy(id => id).ToArray();
        if (_hostOrder is not null &&
            _hostOrder.Length == ids.Length &&
            _hostOrder.OrderBy(id => id).SequenceEqual(ids))
            return;

        _hostOrder = hosts.Select(h => h.Id).ToArray();
        for (int i = _hostOrder.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_hostOrder[i], _hostOrder[j]) = (_hostOrder[j], _hostOrder[i]);
        }
        _hostCursor = 0;
    }

    private Agent? NextHostInOrder(List<Agent> pool)
    {
        if (_hostOrder is null || _hostOrder.Length == 0 || pool.Count == 0)
            return null;

        for (int n = 0; n < _hostOrder.Length; n++)
        {
            var id = _hostOrder[(_hostCursor + n) % _hostOrder.Length];
            var host = pool.FirstOrDefault(h => h.Id == id);
            if (host is null) continue;
            _hostCursor = (_hostCursor + n + 1) % _hostOrder.Length;
            return host;
        }

        return null;
    }
}
