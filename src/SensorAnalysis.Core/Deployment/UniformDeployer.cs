using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Options for <see cref="UniformDeployer"/>.</summary>
public sealed class UniformDeployOptions : DeployerOptionsBase
{
    /// <summary>How many objects to place.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 10;

    /// <summary>Deployable type name.</summary>
    [DeployerParam("Type", IsDeployableType = true)]
    public string TypeName { get; set; } = "OrbitingCoop";
}

/// <summary>
/// Immediate deployer: uniform random placement, then dormant.
/// </summary>
public sealed class UniformDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "Uniform";

    /// <inheritdoc />
    public string Description =>
        "Immediate: uniform random placement of Count objects of Type; then dormant.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(UniformDeployOptions);

    /// <inheritdoc />
    public object CreateOptions() => new UniformDeployOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        var o = (UniformDeployOptions)options;
        DeployableSpawner.SpawnUniform(world, context, o.Count, o.TypeName, "obj", o.MinDistance);
        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx) { }
}
