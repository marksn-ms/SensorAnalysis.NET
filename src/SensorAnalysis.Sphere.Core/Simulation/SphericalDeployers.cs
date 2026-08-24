namespace SensorAnalysis.Sphere.Core.Simulation;

/// <summary>A scheduled world population behavior.</summary>
public interface ISphericalDeployer
{
    /// <summary>Human-readable deployer name.</summary>
    string Name { get; }

    /// <summary>Runs before agents move on every world tick.</summary>
    void Tick(SphericalWorld world);
}

/// <summary>Configuration for uniformly distributed periodic incident waves.</summary>
public sealed record PeriodicIncidentOptions
{
    /// <summary>Ticks between incident waves, including tick zero when attached early.</summary>
    public int PeriodTicks { get; init; } = 20;

    /// <summary>Incidents in each wave.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Logical type assigned to deployed incidents.</summary>
    public string IncidentType { get; init; } = "StationaryIncident";
}

/// <summary>Deploys deterministic uniform incident waves across the continuous surface.</summary>
public sealed class PeriodicIncidentDeployer : ISphericalDeployer
{
    private readonly PeriodicIncidentOptions _options;

    /// <summary>Creates a periodic incident deployer.</summary>
    public PeriodicIncidentDeployer(PeriodicIncidentOptions? options = null) =>
        _options = options ?? new PeriodicIncidentOptions();

    /// <inheritdoc />
    public string Name => "PeriodicIncidents";

    /// <inheritdoc />
    public void Tick(SphericalWorld world)
    {
        if (world.CurrentTick % Math.Max(1, _options.PeriodTicks) != 0) return;
        for (var i = 0; i < Math.Max(1, _options.Count); i++)
            world.AddRandomIncident(_options.IncidentType);
    }
}

/// <summary>Configuration for one-shot uniform agent placement.</summary>
public sealed record UniformAgentOptions
{
    /// <summary>Number of agents placed by the deployer.</summary>
    public int Count { get; init; } = 10;

    /// <summary>Navigation behavior assigned to the deployed agents.</summary>
    public SphericalAgentKind Kind { get; init; } = SphericalAgentKind.CooperativeSearch;

    /// <summary>Optional sensor-range override.</summary>
    public double? SensorRange { get; init; }

    /// <summary>Optional communication-range override.</summary>
    public double? CommunicationRange { get; init; }

    /// <summary>Optional step-size override.</summary>
    public double? StepSize { get; init; }
}

/// <summary>Places agents uniformly once, on its first tick.</summary>
public sealed class UniformAgentDeployer : ISphericalDeployer
{
    private readonly UniformAgentOptions _options;
    private bool _hasDeployed;

    /// <summary>Creates the one-shot uniform deployer.</summary>
    public UniformAgentDeployer(UniformAgentOptions? options = null) =>
        _options = options ?? new UniformAgentOptions();

    /// <inheritdoc />
    public string Name => "UniformAgents";

    /// <inheritdoc />
    public void Tick(SphericalWorld world)
    {
        if (_hasDeployed) return;
        for (var i = 0; i < Math.Max(1, _options.Count); i++)
            world.AddRandomAgent(_options.Kind, _options.SensorRange, _options.CommunicationRange, _options.StepSize);
        _hasDeployed = true;
    }
}

/// <summary>Configuration for placing agents within a geodesic radius of a host agent.</summary>
public sealed record PlaceNearAgentOptions
{
    /// <summary>Host agent id.</summary>
    public int HostAgentId { get; init; }

    /// <summary>Number of agents to place.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Maximum placement radius as a multiple of the sensor range.</summary>
    public double RangeFactor { get; init; } = 1;

    /// <summary>Navigation behavior assigned to the deployed agents.</summary>
    public SphericalAgentKind Kind { get; init; } = SphericalAgentKind.CooperativeSearch;

    /// <summary>Optional sensor-range override.</summary>
    public double? SensorRange { get; init; }

    /// <summary>Optional communication-range override.</summary>
    public double? CommunicationRange { get; init; }

    /// <summary>Optional step-size override.</summary>
    public double? StepSize { get; init; }
}

/// <summary>Places agents near a host agent using dual-graph candidate faces.</summary>
public sealed class PlaceNearAgentDeployer : ISphericalDeployer
{
    private readonly PlaceNearAgentOptions _options;
    private bool _hasDeployed;

    /// <summary>Creates the one-shot near-host deployer.</summary>
    public PlaceNearAgentDeployer(PlaceNearAgentOptions options) => _options = options;

    /// <inheritdoc />
    public string Name => "PlaceNearAgent";

    /// <inheritdoc />
    public void Tick(SphericalWorld world)
    {
        if (_hasDeployed) return;
        var host = world.Agents.FirstOrDefault(agent => agent.Id == _options.HostAgentId);
        if (host is null) return;
        var sensor = _options.SensorRange ?? host.SensorRange;
        var range = Math.Max(0, sensor * _options.RangeFactor);
        var candidates = world.Mesh.FacesWithinHops(host.FaceId, world.RequiredHops(range) + 1);
        for (var i = 0; i < Math.Max(1, _options.Count); i++)
        {
            var face = world.Mesh.Faces[candidates[(host.Id + i * 17) % candidates.Count]];
            world.AddAgent(face.Center, _options.Kind, _options.SensorRange, _options.CommunicationRange, _options.StepSize);
        }
        _hasDeployed = true;
    }
}
