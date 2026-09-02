using System.Collections.Generic;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Measurement-only view of personal-space occupancy vs herding-offense fire.
/// Implementations must not change Decide; counters are observational.
/// </summary>
public interface ICrowdIntrusionTelemetry
{
    /// <summary>Agent id of this skeptic observer.</summary>
    int Id { get; }

    /// <summary>Ticks of persistent closing-inside-space before a herding offense (unchanged knob).</summary>
    int HerdingGraceTicks { get; }

    /// <summary>Mean Beta trust toward a peer (prior if unknown).</summary>
    double MeanTrustToward(int agentId);

    /// <summary>Current closing-inside streak per peer (0 if outside personal space).</summary>
    IReadOnlyDictionary<int, int> CurrentIntrusionTicks { get; }

    /// <summary>Lifetime ticks a peer spent inside this observer's personal space.</summary>
    IReadOnlyDictionary<int, int> LifetimeInsidePersonalSpaceTicks { get; }

    /// <summary>Lifetime ticks a peer was closing while already inside personal space.</summary>
    IReadOnlyDictionary<int, int> LifetimeClosingTicks { get; }

    /// <summary>1 if the peer was inside personal space on the last Decide, else 0 (missing = not seen).</summary>
    IReadOnlyDictionary<int, int> CurrentlyInsidePersonalSpace { get; }

    /// <summary>Herding-channel offense event count toward <paramref name="targetId"/>.</summary>
    int HerdingOffenseCountToward(int targetId);
}
