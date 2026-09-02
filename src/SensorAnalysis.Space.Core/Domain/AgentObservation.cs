namespace SensorAnalysis.Space.Core.Domain;

/// <summary>Snapshot of a nearby agent for sensing.</summary>
/// <param name="Id">Runtime id.</param>
/// <param name="Displacement">Toroidal displacement from the observer to this agent.</param>
/// <param name="Velocity">World velocity of the agent.</param>
/// <param name="AgentType">Factory / strategy name.</param>
public readonly record struct AgentInfo(int Id, Vec3 Displacement, Vec3 Velocity, string AgentType);

/// <summary>Snapshot of a nearby celestial body for sensing.</summary>
/// <param name="Id">Runtime id.</param>
/// <param name="Displacement">Toroidal displacement from the observer to the body.</param>
/// <param name="Radius">Visual radius of the body.</param>
/// <param name="Mu">Gravitational parameter (0 if the body is not a source).</param>
/// <param name="ObjectType">Catalog type name.</param>
public readonly record struct BodyInfo(int Id, Vec3 Displacement, double Radius, double Mu, string ObjectType);

/// <summary>Snapshot of a nearby incident for sensing.</summary>
/// <param name="Id">Runtime id.</param>
/// <param name="Displacement">Toroidal displacement from the observer to the incident.</param>
/// <param name="Intensity">Remaining intensity.</param>
public readonly record struct IncidentInfo(int Id, Vec3 Displacement, double Intensity);

/// <summary>Local observation available to an agent when deciding.</summary>
public sealed class AgentObservation
{
    /// <summary>Creates an observation bundle.</summary>
    /// <param name="origin">Observer world position (wrapped).</param>
    /// <param name="tick">Current world tick.</param>
    /// <param name="nearbyAgents">Agents within sensor range (excluding self).</param>
    /// <param name="nearbyBodies">Bodies within sensor range.</param>
    /// <param name="nearbyIncidents">Active incidents within sensor range.</param>
    public AgentObservation(
        Vec3 origin,
        long tick,
        IReadOnlyList<AgentInfo> nearbyAgents,
        IReadOnlyList<BodyInfo> nearbyBodies,
        IReadOnlyList<IncidentInfo> nearbyIncidents)
    {
        Origin = origin;
        Tick = tick;
        NearbyAgents = nearbyAgents;
        NearbyBodies = nearbyBodies;
        NearbyIncidents = nearbyIncidents;
    }

    /// <summary>Gets the observer position.</summary>
    public Vec3 Origin { get; }

    /// <summary>Gets the current tick.</summary>
    public long Tick { get; }

    /// <summary>Gets nearby agents.</summary>
    public IReadOnlyList<AgentInfo> NearbyAgents { get; }

    /// <summary>Gets nearby bodies.</summary>
    public IReadOnlyList<BodyInfo> NearbyBodies { get; }

    /// <summary>Gets nearby incidents.</summary>
    public IReadOnlyList<IncidentInfo> NearbyIncidents { get; }
}
