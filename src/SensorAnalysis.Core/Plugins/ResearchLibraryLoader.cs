using System.Reflection;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Metrics;

namespace SensorAnalysis.Core.Plugins;

/// <summary>
/// Loads a research class library at runtime and finds the agent factories,
/// deployable factories, deployers, and watchers it exports.
/// The engine does not reference the library. A public concrete type with a
/// public parameterless constructor is imported when it implements one of those
/// contracts. Paths may be loaded more than once; the host decides whether to
/// register the result again.
/// </summary>
public static class ResearchLibraryLoader
{
    private static int _resolveHooked;

    /// <summary>Load each assembly and collect the research types it exports.</summary>
    /// <param name="assemblyPaths">Absolute or relative paths to plugin DLLs.</param>
    /// <returns>Factories and human-readable notes. Missing files throw.</returns>
    public static ResearchImport Import(IEnumerable<string> assemblyPaths)
    {
        EnsureResolveHook();
        var agents = new List<IAgentFactory>();
        var deployables = new List<IDeployableFactory>();
        var deployers = new List<Func<IDeployer>>();
        var watchers = new List<Func<IWatcher>>();
        var messages = new List<string>();

        foreach (var raw in assemblyPaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var path = Path.GetFullPath(raw);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Research library not found: {path}", path);

            var assembly = Load(path);
            var found = Scan(assembly, agents, deployables, deployers, watchers);
            messages.Add(
                $"Loaded {assembly.GetName().Name} ({found.agents} agents, {found.deployables} deployables, {found.deployers} deployers, {found.watchers} watchers) from {path}");
        }

        return new ResearchImport(agents, deployables, deployers, watchers, messages);
    }

    private static Assembly Load(string path)
    {
        var name = AssemblyName.GetAssemblyName(path);
        var existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
        return existing ?? Assembly.LoadFrom(path);
    }

    private static (int agents, int deployables, int deployers, int watchers) Scan(
        Assembly assembly,
        List<IAgentFactory> agents,
        List<IDeployableFactory> deployables,
        List<Func<IDeployer>> deployers,
        List<Func<IWatcher>> watchers)
    {
        var beforeA = agents.Count;
        var beforeD = deployables.Count;
        var beforeP = deployers.Count;
        var beforeW = watchers.Count;

        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null)!;
        }

        foreach (var type in types)
        {
            if (type is null || !type.IsClass || type.IsAbstract || !type.IsPublic)
                continue;
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            if (typeof(IAgentFactory).IsAssignableFrom(type))
                agents.Add((IAgentFactory)Activator.CreateInstance(type)!);

            if (typeof(IDeployableFactory).IsAssignableFrom(type))
                deployables.Add((IDeployableFactory)Activator.CreateInstance(type)!);

            if (typeof(IDeployer).IsAssignableFrom(type))
            {
                var captured = type;
                deployers.Add(() => (IDeployer)Activator.CreateInstance(captured)!);
            }

            if (typeof(IWatcher).IsAssignableFrom(type))
            {
                var captured = type;
                watchers.Add(() => (IWatcher)Activator.CreateInstance(captured)!);
            }
        }

        return (
            agents.Count - beforeA,
            deployables.Count - beforeD,
            deployers.Count - beforeP,
            watchers.Count - beforeW);
    }

    private static void EnsureResolveHook()
    {
        if (Interlocked.Exchange(ref _resolveHooked, 1) == 1)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name).Name;
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
        };
    }
}

/// <summary>Types discovered in one <see cref="ResearchLibraryLoader.Import"/> call.</summary>
public sealed class ResearchImport
{
    internal ResearchImport(
        IReadOnlyList<IAgentFactory> agentFactories,
        IReadOnlyList<IDeployableFactory> deployableFactories,
        IReadOnlyList<Func<IDeployer>> deployerFactories,
        IReadOnlyList<Func<IWatcher>> watcherFactories,
        IReadOnlyList<string> messages)
    {
        AgentFactories = agentFactories;
        DeployableFactories = deployableFactories;
        DeployerFactories = deployerFactories;
        WatcherFactories = watcherFactories;
        Messages = messages;
    }

    /// <summary>Agent factories exported by the loaded libraries.</summary>
    public IReadOnlyList<IAgentFactory> AgentFactories { get; }

    /// <summary>Deployable factories exported by the loaded libraries.</summary>
    public IReadOnlyList<IDeployableFactory> DeployableFactories { get; }

    /// <summary>Fresh-instance factories for exported deployers, keyed later by <see cref="IDeployer.Name"/>.</summary>
    public IReadOnlyList<Func<IDeployer>> DeployerFactories { get; }

    /// <summary>Fresh-instance factories for exported watchers.</summary>
    public IReadOnlyList<Func<IWatcher>> WatcherFactories { get; }

    /// <summary>One note per loaded assembly.</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Construct one watcher instance per exported watcher type.</summary>
    public IReadOnlyList<IWatcher> CreateWatchers() =>
        WatcherFactories.Select(f => f()).ToList();

    /// <summary>
    /// Merge discovered types into the host registries.
    /// Agent factories with no matching deployable are wrapped so deployer type pickers can place them.
    /// </summary>
    public void Register(
        AgentFactoryRegistry agents,
        DeployableRegistry deployables,
        IDictionary<string, Func<IDeployer>> deployers)
    {
        foreach (var factory in AgentFactories)
            agents.Register(factory);

        foreach (var factory in DeployableFactories)
            deployables.Register(factory);

        foreach (var factory in AgentFactories)
        {
            if (!deployables.Names.Contains(factory.Name, StringComparer.OrdinalIgnoreCase))
                deployables.Register(new AgentDeployableAdapter(factory));
        }

        foreach (var factory in DeployerFactories)
        {
            var sample = factory();
            if (!string.IsNullOrWhiteSpace(sample.Name))
                deployers[sample.Name] = factory;
        }
    }
}
