namespace SensorAnalysis.Core.Domain;

/// <summary>
/// A mobile sensor agent (classic "Inhabitant").
/// Subclass and override <see cref="Decide"/> to implement search / communication strategies.
/// </summary>
public class Agent : WorldObject
{
    private readonly List<MessageInfo> _inbox = new();

    /// <summary>
    /// Maximum distance (in world units) this agent can sense incidents and other agents.
    /// Default: 100.
    /// </summary>
    public double SensorRange { get; set; } = 100;

    /// <summary>
    /// Radio range used when transmitting. Defaults to <see cref="SensorRange"/> when null.
    /// </summary>
    public double? CommRange { get; set; }

    /// <summary>
    /// When true (honest default), the agent only considers broadcasts (<c>ToAgentId == null</c>)
    /// and messages addressed to itself. When false, the agent may eavesdrop on traffic
    /// addressed to others (adversary / promiscuous-receiver mode).
    /// </summary>
    public bool FilterToAddressedTraffic { get; set; } = true;

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
    /// When true (default), this agent drains incident intensity while in range.
    /// Set false for infiltrators that should not help clear events.
    /// </summary>
    public bool ContributesToIncidentDrain { get; set; } = true;

    /// <summary>Effective transmit/receive range for radio messages.</summary>
    public double EffectiveCommRange => CommRange ?? SensorRange;

    /// <summary>
    /// Enqueue a received radio message for processing on the next observation.
    /// Delivery is range-limited by the sender; address filtering happens in <see cref="DrainInbox"/>.
    /// </summary>
    /// <param name="message">Delivered message snapshot.</param>
    public void Receive(MessageInfo message) => _inbox.Add(message);

    /// <summary>
    /// Returns inbox messages this agent is willing to consider, then clears the inbox.
    /// </summary>
    public IReadOnlyList<MessageInfo> DrainInbox()
    {
        IEnumerable<MessageInfo> q = _inbox;
        if (FilterToAddressedTraffic)
        {
            q = q.Where(m => m.ToAgentId is null || m.ToAgentId == Id);
        }

        var list = q.ToList();
        _inbox.Clear();
        return list;
    }

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
    /// <param name="ctx">World context used for observations and toroidal wrapping.</param>
    public override void Tick(WorldContext ctx)
    {
        var observation = ctx.World.Observe(this);
        LastAction = Decide(observation);
        var step = LastAction.ToStep() * StepSize;
        var next = Position + step;
        Position = ctx.World.Wrap(next);
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

    /// <summary>Toroidal world width (same units as positions).</summary>
    public required double WorldWidth { get; init; }

    /// <summary>Toroidal world height (same units as positions).</summary>
    public required double WorldHeight { get; init; }

    /// <summary>Information about other agents within sensory range.</summary>
    public required IReadOnlyList<AgentInfo> NearbyAgents { get; init; }

    /// <summary>
    /// Local event cues this tick (not an oracle dump of world incidents).
    /// In cue-sensing mode <see cref="IncidentInfo.Id"/> is 0 and Position is perceived.
    /// </summary>
    public required IReadOnlyList<IncidentInfo> NearbyIncidents { get; init; }

    /// <summary>
    /// Radio messages delivered to this agent's inbox since the previous observation
    /// (after optional address filtering).
    /// </summary>
    public required IReadOnlyList<MessageInfo> NearbyMessages { get; init; }

    /// <summary>Immediate reward observed on this tick (may be zero or domain-specific).</summary>
    public double InstantReward { get; init; }

    /// <summary>Toroidal distance from the observer to <paramref name="p"/>.</summary>
    /// <param name="p">Target point.</param>
    /// <returns>Shortest distance on the torus.</returns>
    public double DistanceTo(Vec2 p) =>
        Vec2.ToroidalDistance(SelfPosition, p, WorldWidth, WorldHeight);

    /// <summary>Shortest toroidal displacement from the observer to <paramref name="p"/>.</summary>
    /// <param name="p">Target point.</param>
    /// <returns>Minimum-image delta vector.</returns>
    public Vec2 DisplacementTo(Vec2 p) =>
        Vec2.ToroidalDisplacement(SelfPosition, p, WorldWidth, WorldHeight);
}

/// <summary>Lightweight information about another agent.</summary>
/// <param name="Id">Unique runtime identifier of the agent.</param>
/// <param name="Position">Agent position.</param>
/// <param name="AgentType">Type/factory name of the agent.</param>
public readonly record struct AgentInfo(int Id, Vec2 Position, string AgentType);

/// <summary>Lightweight information about an incident or a local cue.</summary>
/// <param name="Id">World incident id, or 0 for a cue (no oracle identity).</param>
/// <param name="Position">True position (exact sensing) or perceived position (cue).</param>
/// <param name="Intensity">Incident intensity (domain-specific meaning).</param>
/// <param name="Strength">Cue strength in [0, 1]; 1 for exact sensing.</param>
public readonly record struct IncidentInfo(int Id, Vec2 Position, double Intensity, double Strength = 1.0);

/// <summary>Lightweight information about a message observed by an agent.</summary>
/// <param name="Id">Unique runtime identifier of the message.</param>
/// <param name="Position">Origin position of the transmitter when sent.</param>
/// <param name="FromType">Agent type that sent the message.</param>
/// <param name="FromAgentId">Runtime id of the sender, if known.</param>
/// <param name="ToAgentId">Intended recipient, or <c>null</c> for broadcast.</param>
/// <param name="Payload">Message payload.</param>
/// <param name="TtlRemaining">Ticks remaining before an optional world echo expires.</param>
public readonly record struct MessageInfo(
    int Id,
    Vec2 Position,
    string FromType,
    int? FromAgentId,
    int? ToAgentId,
    string Payload,
    int TtlRemaining);
