using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="PlaceNearDeployer"/>.</summary>
public sealed class PlaceNearOptions : DeployerOptionsBase
{
    /// <summary>Objects per wave (or once when <see cref="PeriodTicks"/> is 0).</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Host type to place near (must already be in the world).</summary>
    [DeployerParam("Near type", IsDeployableType = true,
        Description = "Objects land near living hosts of this type.")]
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

    /// <summary>Deployable type name for spawned objects.</summary>
    [DeployerParam("Type", IsDeployableType = true)]
    public string TypeName { get; set; } = "Incident";

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
/// Places objects near living hosts of a chosen type at a configurable fraction of their sensor range.
/// </summary>
public sealed class PlaceNearDeployer : IDeployer
{
    private SimWorld? _world;
    private PlaceNearOptions? _options;
    private DeployContext? _context;
    private int _waveIndex;
    private bool _dormant = true;
    private int[]? _hostOrder;
    private int _hostCursor;

    /// <inheritdoc />
    public string Name => "PlaceNear";

    /// <inheritdoc />
    public string Description =>
        "Objects near a host type at RangeFactor × sensor range (one-shot or periodic).";

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
        _context = context;
        _waveIndex = 0;
        _hostOrder = null;
        _hostCursor = 0;
        if (_options.Count < 1) _options.Count = 1;
        if (_options.RangeFactor < 0) _options.RangeFactor = 0;

        if (_options.PeriodTicks <= 0)
        {
            SpawnWave(world, waveIndex: 0);
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

        SpawnWave(_world, _waveIndex);
        _waveIndex++;
    }

    private void SpawnWave(SimWorld world, int waveIndex)
    {
        var o = _options!;
        var rng = world.Random;
        var deployableParams = _context!.ParametersFor(o.TypeName);
        var avoid = IncidentPlacementHelper.CollectOccupiedPositions(world);

        for (int i = 0; i < o.Count; i++)
        {
            var position = IncidentPlacementHelper.PlaceWithMinSpacing(
                world,
                rng,
                () => o.RoundRobinHosts
                    ? PlaceRoundRobin(world, rng, o)
                    : IncidentPlacementHelper.PlaceNearType(
                        world, rng, o.NearTypeName, o.RangeFactor),
                o.MinDistance,
                avoid);

            _context.Deployables.Deploy(world, o.TypeName, new DeployableSpawnRequest
            {
                Position = position,
                Label = $"obj-near-{waveIndex}-{i}",
                Parameters = deployableParams,
                Rng = rng
            });
            avoid.Add(position);
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
