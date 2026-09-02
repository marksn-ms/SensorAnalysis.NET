using System.Collections.Generic;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Measurement-only view of local-progress co-presence vs progress-offense fire.
/// Implementations must not change the detector to chase ρ; counters are observational.
/// </summary>
public interface IProgressStallTelemetry
{
    /// <summary>Agent id of this skeptic observer.</summary>
    int Id { get; }

    /// <summary>Ticks of co-presence without extra shrink before a progress offense.</summary>
    int ProgressGraceTicks { get; }

    /// <summary>Mean Beta trust toward a peer (prior if unknown).</summary>
    double MeanTrustToward(int agentId);

    /// <summary>Current co-presence streak (ticks) per peer at the observer's station.</summary>
    IReadOnlyDictionary<int, int> CurrentCopresentTicks { get; }

    /// <summary>Lifetime ticks a peer spent co-present with this observer at a station.</summary>
    IReadOnlyDictionary<int, int> LifetimeCopresentTicks { get; }

    /// <summary>1 if the peer was on-station with this observer on the last Decide, else 0.</summary>
    IReadOnlyDictionary<int, int> CurrentlyOnStation { get; }

    /// <summary>Progress-channel offense event count toward <paramref name="targetId"/>.</summary>
    int ProgressOffenseCountToward(int targetId);

    /// <summary>Last sampled cue intensity at the observer's station (NaN if none).</summary>
    double LastSampledIntensity { get; }

    /// <summary>Completed grace windows that produced a progress offense (lifetime, all peers).</summary>
    int LifetimeProgressOffenseWindows { get; }
}
