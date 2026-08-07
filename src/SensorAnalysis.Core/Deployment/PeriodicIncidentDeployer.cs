using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Ongoing deployer: uniform incident waves on ticks 0, N, 2N, ….
/// For placement near a host agent type, use <see cref="PlaceNearDeployer"/> instead.
/// </summary>
public sealed class PeriodicIncidentDeployer : IDeployer
{
    private SimWorld? _world;
    private PeriodicIncidentOptions? _options;
    private int _randomSalt;
    private int _waveIndex;
    private bool _dormant = true;

    /// <inheritdoc />
    public string Name => "PeriodicIncidents";

    /// <inheritdoc />
    public string Description => "Ongoing: uniform incident waves every N ticks.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(PeriodicIncidentOptions);

    /// <inheritdoc />
    public object CreateOptions() => new PeriodicIncidentOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        _options = (PeriodicIncidentOptions)options;
        _world = world;
        _randomSalt = context.RandomSalt;
        _waveIndex = 0;
        if (_options.PeriodTicks < 1) _options.PeriodTicks = 1;
        if (_options.Count < 1) _options.Count = 1;
        _dormant = false;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx)
    {
        if (_dormant || _world is null || _options is null) return;
        if (ctx.World != _world) return;
        if (ctx.Tick % _options.PeriodTicks != 0) return;

        var salt = unchecked(_randomSalt ^ (int)ctx.Tick * 7919 ^ _waveIndex * 104729);
        var rng = new Random(unchecked(_world.Seed ^ salt));

        for (int i = 0; i < _options.Count; i++)
        {
            _world.Add(new Incident
            {
                Position = IncidentPlacementHelper.PlaceUniform(_world, rng),
                Intensity = 1.0,
                IncidentType = string.IsNullOrWhiteSpace(_options.IncidentType) ? "Incident" : _options.IncidentType,
                Label = $"I-w{_waveIndex}-{i}"
            });
        }

        _waveIndex++;
    }
}
