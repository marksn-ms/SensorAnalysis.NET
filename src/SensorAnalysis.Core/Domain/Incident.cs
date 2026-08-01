namespace SensorAnalysis.Core.Domain;

/// <summary>
/// A point of interest / event in the environment that agents try to discover or cover
/// (classic "Incident").
/// </summary>
public class Incident : WorldObject
{
    /// <summary>Remaining "resources" or intensity. Agents reduce this when nearby.</summary>
    public double Intensity { get; set; } = 1.0;

    /// <summary>How much intensity an agent drains per tick while in range (scaled by multi-agent exponent).</summary>
    public double DrainPerAgentPerTick { get; set; } = 0.01;

    /// <summary>
    /// Exponent on the number of draining agents. 1 = linear; &gt;1 rewards multiple eyes on an event.
    /// </summary>
    public double MultiAgentDrainExponent { get; set; } = 1.5;

    /// <summary>True when intensity has been fully drained.</summary>
    public bool IsDepleted => Intensity <= 0;

    /// <summary>
    /// Per-tick update for this incident. Intensity draining is handled by the world engine after agents move.
    /// </summary>
    /// <param name="ctx">World context for the current tick.</param>
    public override void Tick(WorldContext ctx)
    {
        // Intensity is drained by agents in the engine's post-agent phase.
    }
}
