using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Plugins;

/// <summary>
/// Places an <see cref="IAgentFactory"/> through the deployable registry.
/// Research libraries that only export an agent factory still show up in deployer type lists.
/// </summary>
public sealed class AgentDeployableAdapter : IDeployableFactory
{
    private readonly IAgentFactory _inner;

    /// <summary>Wraps an agent factory.</summary>
    /// <param name="inner">Factory invoked for each placement.</param>
    public AgentDeployableAdapter(IAgentFactory inner) => _inner = inner;

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
