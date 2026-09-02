namespace SensorAnalysis.Space.Core.World;

/// <summary>Read-only view of world state passed into objects each tick.</summary>
public sealed class WorldContext
{
    /// <summary>Gets the current simulation tick number.</summary>
    public required long Tick { get; init; }

    /// <summary>Gets the simulation world instance for this tick.</summary>
    public required SimWorld World { get; init; }

    /// <summary>Gets the deterministic random source owned by the world.</summary>
    public required Random Rng { get; init; }
}
