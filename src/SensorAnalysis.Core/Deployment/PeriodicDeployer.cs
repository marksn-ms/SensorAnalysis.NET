using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="PeriodicDeployer"/>.</summary>
public sealed class PeriodicDeployOptions : DeployerOptionsBase
{
    /// <summary>
    /// Ticks between waves. When 0, place once in <c>Attach</c> then go dormant.
    /// When &gt; 0, spawn on ticks 0, N, 2N, ….
    /// </summary>
    [DeployerParam("Period (ticks)",
        Description = "0 = one-shot; otherwise waves on ticks 0, N, 2N, …")]
    public int PeriodTicks { get; set; } = 25;

    /// <summary>Objects created each wave.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Deployable type name.</summary>
    [DeployerParam("Type", IsDeployableType = true)]
    public string TypeName { get; set; } = "Incident";

    /// <summary>
    /// When set, spawn near living hosts of this type. When empty, place uniformly.
    /// </summary>
    [DeployerParam("Near type", Required = false, IsDeployableType = true,
        Description = "Empty = uniform field; otherwise spawn near living hosts of this type.")]
    public string NearTypeName { get; set; } = "";

    /// <summary>
    /// Placement radius as a fraction of the host's sensor range (ignored when Near type is empty).
    /// </summary>
    [DeployerParam("Range factor", Required = false,
        Description = "Radius = factor × host SensorRange (with light jitter). Used only with Near type.")]
    public double RangeFactor { get; set; } = 0.5;

    /// <summary>
    /// When true, shuffle hosts once per trial and cycle through them (preferring
    /// hosts that do not already cover an active incident).
    /// </summary>
    [DeployerParam("Round-robin hosts", Required = false,
        Description = "Cycle shuffled hosts; skip hosts that already have a nearby incident.")]
    public bool RoundRobinHosts { get; set; }
}

/// <summary>
/// Wave deployer: uniform field, or optionally near a living host type.
/// Period 0 is one-shot; period N repeats on ticks 0, N, 2N, ….
/// </summary>
public sealed class PeriodicDeployer : IDeployer
{
    private SimWorld? _world;
    private PeriodicDeployOptions? _options;
    private DeployContext? _context;
    private int _waveIndex;
    private bool _dormant = true;
    private readonly HostRoundRobinPicker _hosts = new();

    /// <inheritdoc />
    public string Name => "Periodic";

    /// <inheritdoc />
    public string Description =>
        "Waves every N ticks (0 = one-shot). Empty Near type = uniform field; otherwise spawn near that host type.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(PeriodicDeployOptions);

    /// <inheritdoc />
    public object CreateOptions() => new PeriodicDeployOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        _options = (PeriodicDeployOptions)options;
        _world = world;
        _context = context;
        _waveIndex = 0;
        _hosts.Reset();
        if (_options.Count < 1) _options.Count = 1;
        if (_options.RangeFactor < 0) _options.RangeFactor = 0;

        if (_options.PeriodTicks <= 0)
        {
            SpawnWave(world, waveIndex: 0);
            _dormant = true;
            return;
        }

        _dormant = false;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx)
    {
        if (_dormant || _world is null || _options is null || _context is null) return;
        if (ctx.World != _world) return;
        if (ctx.Tick % _options.PeriodTicks != 0) return;

        SpawnWave(_world, _waveIndex);
        _waveIndex++;
    }

    private void SpawnWave(SimWorld world, int waveIndex)
    {
        var o = _options!;
        var rng = world.Random;
        var nearType = o.NearTypeName?.Trim() ?? "";
        Func<Vec2> place = string.IsNullOrEmpty(nearType)
            ? () => IncidentPlacementHelper.PlaceUniform(world, rng)
            : o.RoundRobinHosts
                ? () => _hosts.Next(world, rng, nearType, o.RangeFactor)
                : () => IncidentPlacementHelper.PlaceNearType(world, rng, nearType, o.RangeFactor);

        DeployableSpawner.Spawn(
            world,
            _context!,
            o.Count,
            o.TypeName,
            $"obj-w{waveIndex}",
            place,
            o.MinDistance);
    }
}
