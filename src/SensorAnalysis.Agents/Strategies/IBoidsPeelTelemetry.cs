using System.Collections.Generic;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Measurement-only view of boids peel occupancy vs heading-mismatch offense fire.
/// </summary>
public interface IBoidsPeelTelemetry
{
    /// <summary>Agent id of this skeptic observer.</summary>
    int Id { get; }

    /// <summary>Consecutive anti-aligned ticks before a peel offense.</summary>
    int PeelGraceTicks { get; }

    /// <summary>Mean Beta trust toward a peer (prior if unknown).</summary>
    double MeanTrustToward(int agentId);

    /// <summary>Lifetime ticks a peer was close and anti-aligned with the local flock mean.</summary>
    IReadOnlyDictionary<int, int> LifetimeAntiAlignTicks { get; }

    /// <summary>1 if the peer was anti-aligned and close on the last Decide.</summary>
    IReadOnlyDictionary<int, int> CurrentlyAntiAligned { get; }

    /// <summary>Peel-channel offense count toward <paramref name="targetId"/>.</summary>
    int PeelOffenseCountToward(int targetId);
}

/// <summary>Measurement-only view of a peel-adversary's current phase (not a skeptic).</summary>
public interface IBoidsPeelAttackTelemetry
{
    /// <summary>Agent id.</summary>
    int Id { get; }

    /// <summary>1 if currently in the dart-away peel phase.</summary>
    int IsPeeling { get; }

    /// <summary>Lifetime ticks spent in the peel phase.</summary>
    int LifetimePeelTicks { get; }
}
