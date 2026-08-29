using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Domain;

/// <summary>
/// Self-centered sensor snapshot for one tick. The observer is always at the origin;
/// every reported position is a minimum-image displacement from the observer.
/// </summary>
public sealed class AgentObservation
{
    internal AgentObservation(
        SimWorld world,
        Vec2 observerOrigin,
        long tick,
        IReadOnlyList<AgentInfo> nearbyAgents,
        IReadOnlyList<IncidentInfo> nearbyIncidents,
        IReadOnlyList<MessageInfo> nearbyMessages,
        double instantReward = 0)
    {
        _world = world;
        _observerOrigin = observerOrigin;
        Tick = tick;
        NearbyAgents = nearbyAgents;
        NearbyIncidents = nearbyIncidents;
        NearbyMessages = nearbyMessages;
        InstantReward = instantReward;
    }

    private readonly SimWorld _world;
    private readonly Vec2 _observerOrigin;

    /// <summary>The current tick index when the observation was taken.</summary>
    public long Tick { get; }

    /// <summary>Peers within sensor range, positions relative to the observer.</summary>
    public IReadOnlyList<AgentInfo> NearbyAgents { get; }

    /// <summary>Local event cues, positions relative to the observer.</summary>
    public IReadOnlyList<IncidentInfo> NearbyIncidents { get; }

    /// <summary>
    /// Radio messages delivered since the previous observation (observer-relative positions).
    /// Broadcasts and traffic addressed to this agent are readable; other addressed traffic
    /// appears garbled (only <see cref="MessageInfo.Id"/> and <see cref="MessageInfo.Position"/>).
    /// </summary>
    public IReadOnlyList<MessageInfo> NearbyMessages { get; }

    /// <summary>Immediate reward observed on this tick (may be zero or domain-specific).</summary>
    public double InstantReward { get; internal set; }

    /// <summary>Observer absolute position (for reward/coverage only — prefer relative geometry in heuristics).</summary>
    internal Vec2 ObserverOrigin => _observerOrigin;

    /// <summary>Distance from the observer to a relative point.</summary>
    public double DistanceTo(Vec2 relativePosition) => relativePosition.Length;

    /// <summary>Displacement from the observer to a relative point (the point itself).</summary>
    public Vec2 DisplacementTo(Vec2 relativePosition) => relativePosition;

    /// <summary>Toroidal distance between two points given as observer-relative displacements.</summary>
    public double DistanceBetween(Vec2 relativeA, Vec2 relativeB) =>
        _world.Distance(AbsoluteFromRelative(relativeA), AbsoluteFromRelative(relativeB));

    /// <summary>Toroidal displacement from <paramref name="relativeFrom"/> to <paramref name="relativeTo"/>.</summary>
    public Vec2 DisplacementBetween(Vec2 relativeFrom, Vec2 relativeTo) =>
        _world.Displacement(AbsoluteFromRelative(relativeFrom), AbsoluteFromRelative(relativeTo));

    /// <summary>Converts an observer-relative displacement to an absolute world position.</summary>
    public Vec2 AbsoluteFromRelative(Vec2 relativePosition) =>
        _world.Wrap(_observerOrigin + relativePosition);

    /// <summary>Converts an absolute world position to an observer-relative displacement.</summary>
    public Vec2 RelativeFromAbsolute(Vec2 absolutePosition) =>
        _world.Displacement(_observerOrigin, absolutePosition);
}

/// <summary>Lightweight information about another agent in the observer frame.</summary>
public readonly record struct AgentInfo(
    int Id,
    Vec2 Position,
    Vec2 Velocity,
    AgentAction Heading,
    string AgentType);

/// <summary>Lightweight information about an incident or a local cue in the observer frame.</summary>
public readonly record struct IncidentInfo(
    int Id,
    Vec2 Position,
    double Intensity,
    double Strength = 1.0);

/// <summary>Lightweight information about a message observed by an agent.</summary>
public readonly record struct MessageInfo(
    int Id,
    Vec2 Position,
    string FromType,
    int? FromAgentId,
    int? ToAgentId,
    string Payload,
    int TtlRemaining)
{
    /// <summary>
    /// Maps an inbox delivery into the observer frame. Readable traffic keeps full content;
    /// traffic addressed to another agent is garbled (presence only).
    /// </summary>
    public static MessageInfo ForObserver(MessageInfo message, Vec2 relativePosition, int observerId)
    {
        if (message.ToAgentId is null || message.ToAgentId == observerId)
            return message with { Position = relativePosition };

        return new MessageInfo(message.Id, relativePosition, "", null, null, "", 0);
    }
}
