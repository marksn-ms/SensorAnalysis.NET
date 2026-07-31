using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Agents;

/// <summary>Creates agent instances by type name (for plugins and config-driven scenarios).</summary>
public interface IAgentFactory
{
    /// <summary>
    /// Gets the unique name of this factory. This name is used to identify the factory
    /// in configuration, registries and when creating agents by type name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a short human-readable description of the agent type produced by this factory.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Creates a new <see cref="Agent"/> instance.
    /// </summary>
    /// <param name="label">Optional label to assign to the created agent.</param>
    /// <returns>A newly created <see cref="Agent"/>.</returns>
    Agent Create(string? label = null);
}

/// <summary>Registry of known agent factories.</summary>
public sealed class AgentFactoryRegistry
{
    private readonly Dictionary<string, IAgentFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers the specified <paramref name="factory"/>. If a factory with the same
    /// <see cref="IAgentFactory.Name"/> already exists it will be replaced.
    /// </summary>
    /// <param name="factory">Factory instance to register.</param>
    public void Register(IAgentFactory factory) => _factories[factory.Name] = factory;

    /// <summary>
    /// Gets the registered factory with the given <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the factory to retrieve.</param>
    /// <returns>The matching <see cref="IAgentFactory"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no factory with the specified name is registered.</exception>
    public IAgentFactory Get(string name) =>
        _factories.TryGetValue(name, out var f)
            ? f
            : throw new KeyNotFoundException($"Unknown agent type '{name}'. Available: {string.Join(", ", _factories.Keys)}");

    /// <summary>
    /// Gets the collection of registered factory names.
    /// </summary>
    public IReadOnlyCollection<string> Names => _factories.Keys;
}
