namespace SensorAnalysis.Core.Domain;

/// <summary>
/// A mobile sensor agent (classic "Inhabitant").
/// Subclass and override <see cref="Decide"/> to implement search / communication strategies.
/// </summary>
public class Agent : WorldObject
{
    /// <summary>
    /// Maximum distance (in world units) this agent can sense incidents and other agents.
    /// Default: 100.
    /// </summary>
    public double SensorRange { get; set; } = 100;

    /// <summary>
    /// The action the agent performed on the most recent tick.
    /// </summary>
    public AgentAction LastAction { get; protected set; } = AgentAction.Stay;

    /// <summary>
    /// Distance moved when the agent issues a movement action.
    /// Default: 5.0 world units.
    /// </summary>
    public double StepSize { get; set; } = 5.0;

    /// <summary>
    /// Accumulated reward collected by this agent over time.
    /// </summary>
    public double TotalReward { get; set; }

    /// <summary>
    /// Identifier of the agent type (factory name or class label).
    /// </summary>
    public string AgentType { get; init; } = "Agent";

    /// <summary>
    /// Override to choose the next action from local observations.
    /// Default: stay put.
    /// </summary>
    /// <param name="observation">Observation available to the agent when making a decision, including nearby agents, incidents, messages, and immediate reward.</param>
    /// <returns>The chosen <see cref="AgentAction"/> for the current tick (default implementation returns <see cref="AgentAction.Stay"/>).</returns>
    public virtual AgentAction Decide(AgentObservation observation) => AgentAction.Stay;

    /// <summary>
    /// Performs a simulation tick for this agent: observe, decide, move and collect reward.
    /// </summary>
    /// <param name="ctx">World context used for observations and bounds clamping.</param>
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        LastAction = Decide(observation);
        var step = LastAction.ToStep() * StepSize;
        var next = Position + step;
        Position = ctx.World.Clamp(next);
        TotalReward += observation.InstantReward;
    }
}

/// <summary>What an agent can sense on a given tick.</summary>
public sealed record AgentObservation
{
    /// <summary>The current tick index when the observation was taken.</summary>
    public required long Tick { get; init; }

    /// <summary>The observing agent's current position.</summary>
    public required Vec2 SelfPosition { get; init; }

    /// <summary>Information about other agents within sensory range.</summary>
    public required IReadOnlyList<AgentInfo> NearbyAgents { get; init; }

    /// <summary>Information about incidents within sensory range.</summary>
    public required IReadOnlyList<IncidentInfo> NearbyIncidents { get; init; }

    /// <summary>Messages (broadcast/local) visible to the agent on this tick.</summary>
    public required IReadOnlyList<MessageInfo> NearbyMessages { get; init; }

    /// <summary>Immediate reward observed on this tick (may be zero or domain-specific).</summary>
    public double InstantReward { get; init; }
}

/// <summary>Lightweight information about another agent.</summary>
/// <param name="Id">Unique runtime identifier of the agent.</param>
/// <param name="Position">Agent position.</param>
/// <param name="AgentType">Type/factory name of the agent.</param>
public readonly record struct AgentInfo(int Id, Vec2 Position, string AgentType);

/// <summary>Lightweight information about an incident.</summary>
/// <param name="Id">Unique runtime identifier of the incident.</param>
/// <param name="Position">Incident position.</param>
/// <param name="Intensity">Incident intensity (domain-specific meaning).</param>
public readonly record struct IncidentInfo(int Id, Vec2 Position, double Intensity);

/// <summary>Lightweight information about a message observed by an agent.</summary>
/// <param name="Id">Unique runtime identifier of the message.</param>
/// <param name="Position">Origin position of the message.</param>
/// <param name="FromType">Agent type that sent the message.</param>
/// <param name="Payload">Message payload.</param>
/// <param name="TtlRemaining">Ticks remaining before the message expires.</param>
public readonly record struct MessageInfo(int Id, Vec2 Position, string FromType, string Payload, int TtlRemaining);
