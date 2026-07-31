using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Domain;

/// <summary>Base for anything that lives in the world and can be drawn / selected.</summary>
public abstract class WorldObject
{
    private static int _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldObject"/> class and assigns a unique identifier.
    /// </summary>
    protected WorldObject()
    {
        Id = Interlocked.Increment(ref _nextId);
    }

    /// <summary>Gets the unique identifier for this world object.</summary>
    public int Id { get; }

    /// <summary>Gets or sets the position of this object in world coordinates.</summary>
    public Vec2 Position { get; set; }

    /// <summary>Gets or sets the display label for this object.</summary>
    public string Label { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether this object is currently selected in the UI.</summary>
    public bool IsSelected { get; set; }

    /// <summary>Called once per simulation tick.</summary>
    /// <param name="ctx">Read-only view of world state for the current tick.</param>
    public virtual void Tick(WorldContext ctx) { }

    /// <summary>Returns a short, human-readable identifier for the object used in debugging and logs.</summary>
    /// <returns>A string containing the object's type name and id.</returns>
    public override string ToString() => $"{GetType().Name}#{Id}";
}

/// <summary>Read-only view of world state passed into agents and objects each tick.</summary>
public sealed class WorldContext
{
    /// <summary>Gets the current simulation tick number.</summary>
    public required long Tick { get; init; }

    /// <summary>Gets the current trial number within an experiment.</summary>
    public required int Trial { get; init; }

    /// <summary>Gets the simulation world instance for this tick.</summary>
    public required SimWorld World { get; init; }

    /// <summary>Gets the random number generator that should be used for deterministic/random behaviour during this tick.</summary>
    public required Random Rng { get; init; }
}
