using SensorAnalysis.Agents.Deployables;
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
        registry.Register(new PersonalSpaceCoopAgentFactory());
        registry.Register(new CrowdOnlyDeceptiveAgentFactory());
        registry.Register(new CrowdAwareMitigatedAgentFactory());
        registry.Register(new ProgressCoopAgentFactory());
        registry.Register(new ProgressLoiterDeceptiveAgentFactory());
        registry.Register(new ProgressAwareMitigatedAgentFactory());
        registry.Register(new LloydCoopAgentFactory());
        registry.Register(new LloydParkDeceptiveAgentFactory());
        registry.Register(new PheromoneCoopAgentFactory());
        registry.Register(new PheromoneFakeDeceptiveAgentFactory());
        registry.Register(new BoidsCoopAgentFactory());
        registry.Register(new BoidsPeelDeceptiveAgentFactory());
        registry.Register(new BoidsAwareMitigatedAgentFactory());
        return registry;
    }

    /// <summary>Unified deployable registry (agents + incidents + future types).</summary>
    public static DeployableRegistry CreateDeployables()
    {
        var agents = CreateAgents();
        var registry = new DeployableRegistry();
        foreach (var name in agents.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            registry.Register(new AgentDeployableFactory(agents.Get(name)));

        registry.Register(new IncidentDeployableFactory());
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

        Add(() => new UniformDeployer());
        Add(() => new GaussianDeployer());
        Add(() => new PeriodicDeployer());
        Add(() => new PlaceNearDeployer());

        map["Random"] = map["Uniform"];
        map["UniformIncidents"] = map["Uniform"];
        map["PeriodicIncidents"] = map["Periodic"];

        return map;
    }
}
