using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployers;

/// <summary>Options for <see cref="GaussianDeployer"/> — one agent kind per directive.</summary>
public sealed class GaussianDeployOptions
{
    /// <summary>How many agents to place.</summary>
    [DeployerParam("Count")]
    public int Count { get; set; } = 10;

    /// <summary>Agent factory name.</summary>
    [DeployerParam("Type", IsAgentType = true)]
    public string TypeName { get; set; } = "OrbitingCoop";

    /// <summary>Sensor range for placed agents.</summary>
    [DeployerParam("Sensor range")]
    public double SensorRange { get; set; } = 80;

    /// <summary>Gaussian σ about world center.</summary>
    [DeployerParam("Deploy sigma")]
    public double DeploySigma { get; set; } = 80;
}

/// <summary>
/// Immediate deployer: Gaussian drop of <see cref="GaussianDeployOptions.Count"/> agents of one type.
/// </summary>
public sealed class GaussianDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "Gaussian";

    /// <inheritdoc />
    public string Description => "Immediate: Gaussian drop of Count agents of Type; then dormant.";

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
        var rng = new Random(unchecked(world.Seed ^ context.RandomSalt));
        var drop = new Vec2(world.WorldWidthPx / 2.0, world.WorldHeightPx / 2.0);
        var sigma = Math.Max(1.0, o.DeploySigma);
        var n = Math.Max(0, o.Count);

        for (int i = 0; i < n; i++)
        {
            var agent = context.ResolveAgent(o.TypeName);
            agent.Position = SampleGaussian(rng, drop, sigma, world);
            agent.SensorRange = o.SensorRange;
            agent.Label = $"{agent.AgentType}-{i}";
            world.Add(agent);
        }

        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx) { }

    private static Vec2 SampleGaussian(Random rng, Vec2 mean, double sigma, SimWorld world)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var r = Math.Sqrt(-2.0 * Math.Log(u1));
        var theta = 2.0 * Math.PI * u2;
        var p = new Vec2(mean.X + r * Math.Cos(theta) * sigma, mean.Y + r * Math.Sin(theta) * sigma);
        return world.Wrap(p);
    }
}
