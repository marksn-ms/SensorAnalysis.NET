using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Shared lure geometry: one claimed location per victim, at ~90% of sensor range
/// in the direction away from the real (or perceived) event.
/// </summary>
public static class DecoyLure
{
    /// <summary>Fraction of sensor range from the victim to the claimed fake event.</summary>
    public const double RangeFraction = 0.9;

    /// <summary>
    /// Place a decoy where the victim would reasonably expect to see an event:
    /// along the ray from the true event through the victim, at 90% of <paramref name="sensorRange"/>.
    /// Positions are observer-relative. Returns null when the victim is on the event (no bearing).
    /// </summary>
    public static Vec2? AwayFromEvent(
        Vec2 victimRelative,
        Vec2 eventRelative,
        double sensorRange,
        AgentObservation observation)
    {
        var away = observation.DisplacementBetween(eventRelative, victimRelative);
        if (away.Length < 1e-6)
            return null;
        return victimRelative + away.Normalize() * (sensorRange * RangeFraction);
    }
}
