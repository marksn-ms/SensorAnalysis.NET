using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="PeriodicDeployer"/>.</summary>
public sealed class PeriodicDeployOptions : DeployerOptionsBase
{
    /// <summary>Ticks between waves (including a wave at tick 0).</summary>
    [DeployerParam("Period (ticks)", Description = "Spawn on ticks 0, N, 2N, …")]
    public int PeriodTicks { get; set; } = 25;

    /// <summary>Objects created each wave.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Deployable type name.</summary>
    [DeployerParam("Type", IsDeployableType = true)]
    public string TypeName { get; set; } = "Incident";
}

/// <summary>
/// Ongoing deployer: uniform waves on ticks 0, N, 2N, ….
/// </summary>
public sealed class PeriodicDeployer : IDeployer
{
    private SimWorld? _world;
    private PeriodicDeployOptions? _options;
    private DeployContext? _context;
    private int _waveIndex;
    private bool _dormant = true;

    /// <inheritdoc />
    public string Name => "Periodic";

    /// <inheritdoc />
    public string Description => "Ongoing: uniform waves every N ticks.";

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
        if (_options.PeriodTicks < 1) _options.PeriodTicks = 1;
        if (_options.Count < 1) _options.Count = 1;
        _dormant = false;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx)
    {
        if (_dormant || _world is null || _options is null || _context is null) return;
        if (ctx.World != _world) return;
        if (ctx.Tick % _options.PeriodTicks != 0) return;

        var rng = ctx.Rng;
        var o = _options;
        var deployableParams = _context.ParametersFor(o.TypeName);
        var avoid = IncidentPlacementHelper.CollectOccupiedPositions(_world);

        for (int i = 0; i < o.Count; i++)
        {
            var position = IncidentPlacementHelper.PlaceWithMinSpacing(
                _world,
                rng,
                () => IncidentPlacementHelper.PlaceUniform(_world, rng),
                o.MinDistance,
                avoid);

            _context.Deployables.Deploy(_world, o.TypeName, new DeployableSpawnRequest
            {
                Position = position,
                Label = $"obj-w{_waveIndex}-{i}",
                Parameters = deployableParams,
                Rng = rng
            });
            avoid.Add(position);
        }

        _waveIndex++;
    }
}
