using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployers;

/// <summary>Options for <see cref="GaussianDeployer"/>.</summary>
public sealed class GaussianDeployOptions
{
    /// <summary>How many objects to place.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 10;

    /// <summary>Deployable type name.</summary>
    [DeployerParam("Type", IsDeployableType = true)]
    public string TypeName { get; set; } = "OrbitingCoop";

    /// <summary>Gaussian σ about world center.</summary>
    [DeployerParam("Deploy sigma")]
    public double DeploySigma { get; set; } = 80;
}

/// <summary>
/// Immediate deployer: Gaussian drop about world center, then dormant.
/// </summary>
public sealed class GaussianDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "Gaussian";

    /// <inheritdoc />
    public string Description =>
        "Immediate: Gaussian drop of Count objects of Type; then dormant.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(GaussianDeployOptions);

    /// <inheritdoc />
    public object CreateOptions() => new GaussianDeployOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        var o = (GaussianDeployOptions)options;
        DeployableSpawner.SpawnGaussian(
            world, context, o.Count, o.TypeName, o.DeploySigma, "obj");
        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx) { }
}
