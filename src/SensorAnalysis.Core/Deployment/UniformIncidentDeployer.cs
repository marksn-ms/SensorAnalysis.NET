using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="UniformIncidentDeployer"/>.</summary>
public sealed class UniformIncidentOptions
{
    /// <summary>Number of incidents to place once.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 1;

    /// <summary>Logical incident kind for styling / filtering (default <c>Incident</c>).</summary>
    [DeployerParam("Incident type", IsIncidentType = true, Required = false)]
    public string IncidentType { get; set; } = "Incident";
}

/// <summary>
/// Immediate deployer: places <see cref="UniformIncidentOptions.Count"/> incidents uniformly, then dormant.
/// </summary>
public sealed class UniformIncidentDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "UniformIncidents";

    /// <inheritdoc />
    public string Description => "Immediate: place incidents uniformly at random; then dormant.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(UniformIncidentOptions);

    /// <inheritdoc />
    public object CreateOptions() => new UniformIncidentOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        var o = (UniformIncidentOptions)options;
        var rng = new Random(unchecked(world.Seed ^ context.RandomSalt));
        var n = Math.Max(0, o.Count);
        for (int i = 0; i < n; i++)
        {
            world.Add(new Incident
            {
                Position = IncidentPlacementHelper.PlaceUniform(world, rng),
                Intensity = 1.0,
                IncidentType = string.IsNullOrWhiteSpace(o.IncidentType) ? "Incident" : o.IncidentType,
                Label = $"I-{i}"
            });
        }

        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx) { }
}
