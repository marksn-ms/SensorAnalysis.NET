using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployers;

/// <summary>Options for <see cref="RandomDeployer"/> — one agent kind per directive.</summary>
public sealed class RandomDeployOptions
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
}

/// <summary>
/// Immediate deployer: uniform random placement of <see cref="RandomDeployOptions.Count"/> agents of one type.
/// Typical for adversaries.
/// </summary>
public sealed class RandomDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "Random";

    /// <inheritdoc />
    public string Description => "Immediate: uniform random Count agents of Type; then dormant.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(RandomDeployOptions);

    /// <inheritdoc />
    public object CreateOptions() => new RandomDeployOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        var o = (RandomDeployOptions)options;
        var rng = new Random(unchecked(world.Seed ^ context.RandomSalt));
        var w = world.WorldWidthPx;
        var h = world.WorldHeightPx;
        var n = Math.Max(0, o.Count);

        for (int i = 0; i < n; i++)
        {
            var agent = context.ResolveAgent(o.TypeName);
            agent.Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            agent.SensorRange = o.SensorRange;
            agent.Label = $"{agent.AgentType}-{i}";
            world.Add(agent);
        }

        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx) { }
}
