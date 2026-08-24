using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;

namespace SensorAnalysis.Agents;

/// <summary>
/// Built-in agent factories and deployers shared by the CLI and GUI so a recipe
/// file names the same types in both places.
/// </summary>
public static class BuiltinCatalog
{
    /// <summary>All shipped agent factories.</summary>
    public static AgentFactoryRegistry CreateAgents()
    {
        var registry = new AgentFactoryRegistry();
        registry.Register(new RandomWalkAgentFactory());
        registry.Register(new GreedyIncidentAgentFactory());
        registry.Register(new StigmergyAgentFactory());
        registry.Register(new CooperativeSearchAgentFactory());
        registry.Register(new DeceptiveStigmergyAgentFactory());
        registry.Register(new TrustAwareSearchAgentFactory());
        registry.Register(new OrbitingCoopAgentFactory());
        registry.Register(new HerdingDeceptiveAgentFactory());
        registry.Register(new HerdingDeceptiveOracleAgentFactory());
        registry.Register(new MitigatedOrbitAgentFactory());
        return registry;
    }

    /// <summary>All shipped deployer factories, keyed by <see cref="IDeployer.Name"/>.</summary>
    public static Dictionary<string, Func<IDeployer>> CreateDeployers()
    {
        var map = new Dictionary<string, Func<IDeployer>>(StringComparer.OrdinalIgnoreCase);
        void Add(Func<IDeployer> factory)
        {
            var sample = factory();
            map[sample.Name] = factory;
        }

        Add(() => new RandomDeployer());
        Add(() => new GaussianDeployer());
        Add(() => new UniformIncidentDeployer());
        Add(() => new PeriodicIncidentDeployer());
        Add(() => new PlaceNearDeployer());
        return map;
    }
}
