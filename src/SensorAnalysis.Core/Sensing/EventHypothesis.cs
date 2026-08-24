using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Sensing;

/// <summary>Where a hypothesis last gained evidence.</summary>
public enum HypothesisSource
{
    /// <summary>Local sensor cue.</summary>
    Sensor,

    /// <summary>Radio rumor (peer or infiltrator claim).</summary>
    Rumor
}

/// <summary>
/// One suspected event an agent is tracking. Not a world incident: no oracle id,
/// just a local track, a position estimate, and confidence that decays without confirmation.
/// </summary>
public sealed class EventHypothesis
{
    /// <summary>Agent-local track id (not a world incident id).</summary>
    public int LocalId { get; init; }

    /// <summary>Smoothed perceived / claimed location.</summary>
    public Vec2 Estimate { get; set; }

    /// <summary>Belief in [0, 1]. Sensor hits raise it; silence lowers it.</summary>
    public double Confidence { get; set; }

    /// <summary>Ticks since the last cue or rumor that matched this track.</summary>
    public int TicksSinceEvidence { get; set; }

    /// <summary>How this track was last reinforced.</summary>
    public HypothesisSource LastSource { get; set; }

    /// <summary>Last radio sender, if the latest evidence was a rumor.</summary>
    public int? LastInformantId { get; set; }
}
