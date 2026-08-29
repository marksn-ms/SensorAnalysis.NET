using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Arguments supplied when a deployer creates a world object.</summary>
public sealed class DeployableSpawnRequest
{
    /// <summary>Placement position chosen by the deployer.</summary>
    public required Vec2 Position { get; init; }

    /// <summary>Display label for the new object.</summary>
    public required string Label { get; init; }

    /// <summary>Per-type configuration (e.g. sensor range).</summary>
    public required IReadOnlyDictionary<string, object?> Parameters { get; init; }

    /// <summary>World RNG stream.</summary>
    public required Random Rng { get; init; }
}

/// <summary>
/// Creates and installs one deployable type. Deployers depend only on this abstraction —
/// not on whether the product is an agent, incident, or future deployable.
/// </summary>
public interface IDeployableFactory
{
    /// <summary>Registry key used in deployer Type pickers.</summary>
    string Name { get; }

    /// <summary>Short description for UI tooltips.</summary>
    string Description { get; }

    /// <summary>
    /// Optional configuration type whose <see cref="DeployableParamAttribute"/> properties
    /// drive per-type fields in the directive form.
    /// </summary>
    Type? ConfigType { get; }

    /// <summary>Creates a world object at the requested position.</summary>
    WorldObject Create(DeployableSpawnRequest request);

    /// <summary>Adds the object to the appropriate world collection.</summary>
    void Install(SimWorld world, WorldObject obj);
}

/// <summary>Registry of deployable type factories.</summary>
public sealed class DeployableRegistry
{
    private readonly Dictionary<string, IDeployableFactory> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a factory, replacing any existing entry with the same name.</summary>
    public void Register(IDeployableFactory factory) => _factories[factory.Name] = factory;

    /// <summary>Gets a factory by name.</summary>
    public IDeployableFactory Get(string name) =>
        _factories.TryGetValue(name, out var f)
            ? f
            : throw new KeyNotFoundException(
                $"Unknown deployable type '{name}'. Available: {string.Join(", ", _factories.Keys)}");

    /// <summary>Registered type names.</summary>
    public IReadOnlyCollection<string> Names => _factories.Keys;

    /// <summary>Creates and installs one object.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="typeName">Deployable registry key.</param>
    /// <param name="request">Spawn arguments (position, label, parameters).</param>
    /// <returns>The created and installed object.</returns>
    public WorldObject Deploy(
        SimWorld world,
        string typeName,
        DeployableSpawnRequest request)
    {
        var factory = Get(typeName);
        var obj = factory.Create(request);
        factory.Install(world, obj);
        return obj;
    }
}
