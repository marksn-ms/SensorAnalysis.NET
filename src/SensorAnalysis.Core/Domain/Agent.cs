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
    /// The action the agent performed on the most recent tick.
    /// </summary>
    public AgentAction LastAction { get; protected set; } = AgentAction.Stay;

    /// <summary>
    /// Distance moved per tick when the agent issues a movement action.
    /// Default: 5.0 world units.
    /// </summary>
    public double Velocity { get; set; } = 5.0;

    /// <summary>
    /// Base ticks between periodic actions such as radio reports.
    /// Subclasses may override; each instance draws <see cref="ActionInterval"/> from a Gaussian around this value.
    /// </summary>
    public virtual int ActionIntervalBase { get; set; } = 10;

    /// <summary>
    /// Per-instance interval sampled around <see cref="ActionIntervalBase"/> to desynchronize agents.
    /// </summary>
    public int ActionInterval { get; private set; }

    /// <summary>TTL for the optional world echo of a transmission (UI/metrics).</summary>
    public virtual int MessageEchoTtl { get; set; } = 50;

    /// <summary>Ticks to persist a random-walk heading before resampling.</summary>
    public virtual int HeadingChangeInterval { get; set; } = 8;

    /// <summary>World RNG for the current tick; set by <see cref="BindContext"/>.</summary>
    protected Random Rng { get; private set; } = null!;

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
    /// Delivery is range-limited by the sender; readability is applied in <see cref="World.SimWorld.Observe"/>.
    /// </summary>
    /// <param name="message">Delivered message snapshot.</param>
    public void Receive(MessageInfo message) => _inbox.Add(message);

    /// <summary>Returns inbox messages accumulated since the last observation, then clears the inbox.</summary>
    public IReadOnlyList<MessageInfo> DrainInbox()
    {
        var list = _inbox.ToList();
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

    /// <summary>Binds the world's RNG for this tick. Call at the start of overridden <see cref="Tick"/> methods.</summary>
    protected void BindContext(WorldContext ctx) => Rng = ctx.Rng;

    /// <summary>
    /// Samples <see cref="ActionInterval"/> from the world's RNG when the agent enters a world.
    /// </summary>
    internal void InitializeTiming(Random rng)
    {
        if (ActionInterval > 0) return;
        ActionInterval = SampleGaussianInterval(rng, ActionIntervalBase);
    }

    /// <summary>Draws an integer interval from a Gaussian centered on <paramref name="baseInterval"/>.</summary>
    public static int SampleGaussianInterval(Random rng, int baseInterval, double stdDev = 1.5) =>
        Math.Max(1, (int)Math.Round(baseInterval + SampleGaussian(rng) * stdDev));

    /// <summary>
    /// Performs a simulation tick for this agent: observe, decide, move and collect reward.
    /// </summary>
    /// <param name="ctx">World context used for observations and toroidal wrapping.</param>
    public override void Tick(WorldContext ctx)
    {
        BindContext(ctx);
        var observation = ctx.World.Observe(this);
        LastAction = Decide(observation);
        var step = LastAction.ToStep() * Velocity;
        var next = Position + step;
        Position = ctx.World.Wrap(next);
        TotalReward += observation.InstantReward;
    }

    private static double SampleGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
