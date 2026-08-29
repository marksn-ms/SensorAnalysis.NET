using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployables;

/// <summary>Adapts an <see cref="IAgentFactory"/> into a type-agnostic deployable factory.</summary>
public sealed class AgentDeployableFactory : IDeployableFactory
{
    private readonly IAgentFactory _inner;

    /// <summary>Wraps an existing agent factory.</summary>
    /// <param name="inner">Agent factory to adapt.</param>
    public AgentDeployableFactory(IAgentFactory inner) => _inner = inner;

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public string Description => _inner.Description;

    /// <inheritdoc />
    public Type? ConfigType => typeof(AgentDeployDefaults);

    /// <inheritdoc />
    public WorldObject Create(DeployableSpawnRequest request)
    {
        var defaults = new AgentDeployDefaults();
        DeployableParameterCatalog.Apply(defaults, request.Parameters);
        var agent = _inner.Create(request.Label);
        agent.Position = request.Position;
        agent.SensorRange = defaults.SensorRange;
        return agent;
    }

    /// <inheritdoc />
    public void Install(SimWorld world, WorldObject obj) => world.Add((Agent)obj);
}
