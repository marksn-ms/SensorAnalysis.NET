namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Shared sensor-range fractions for orbiting cooperative agents.
/// Personal space and orbit distance both use <see cref="FractionOfSensorRange"/> (0.5 × r).
/// </summary>
public static class CooperativeRadii
{
    /// <summary>Fraction of sensor range used for personal space and orbit radius.</summary>
    public const double FractionOfSensorRange = 0.5;

    /// <summary>Minimum association radius in world units (floor when r is small).</summary>
    public const double MinAssociationRadius = 12;

    /// <summary>Personal-space yielding radius (0.5 × r).</summary>
    public static double PersonalSpace(double sensorRange) =>
        sensorRange * FractionOfSensorRange;

    /// <summary>Preferred orbit radius about an event (0.5 × r).</summary>
    public static double OrbitRadius(double sensorRange) =>
        sensorRange * FractionOfSensorRange;

    /// <summary>Hypothesis merge radius (0.5 × r, floored at <see cref="MinAssociationRadius"/>).</summary>
    public static double AssociationRadius(double sensorRange) =>
        Math.Max(MinAssociationRadius, sensorRange * FractionOfSensorRange);
}
