namespace SensorAnalysis.Core.Sensing;

/// <summary>
/// How an event presents to a local sensor. Ground-truth <c>Incident</c> objects stay exact;
/// this profile only shapes the cues agents are allowed to see.
/// </summary>
public sealed class IncidentSensingProfile
{
    /// <summary>
    /// Detection probability along the sensor disc:
    /// <c>P(d) = (1 - d/r)^DetectionExponent</c> for <c>d &lt; r</c>, else 0.
    /// Exponent 2 is a gentle power fade: certain at the source, rare at the horizon.
    /// </summary>
    public double DetectionExponent { get; set; } = 2.0;

    /// <summary>
    /// Position-error growth uses the same shape: <c>σ(d) = σ_max · (d/r)^ErrorExponent</c>.
    /// Default matches <see cref="DetectionExponent"/>.
    /// </summary>
    public double ErrorExponent { get; set; } = 2.0;

    /// <summary>
    /// Cap on localization error at range <c>r</c>, as a fraction of sensor range.
    /// Default 0.25 ⇒ <c>r/4</c>: a horizon ping still points into the correct quadrant,
    /// but is not a GPS fix. Sampled Gaussian error is also clamped to this cap.
    /// </summary>
    public double PositionErrorAtRangeFactor { get; set; } = 0.25;

    /// <summary>Copy suitable for cloning <c>WorldOptions</c>.</summary>
    public IncidentSensingProfile Clone() => new()
    {
        DetectionExponent = DetectionExponent,
        ErrorExponent = ErrorExponent,
        PositionErrorAtRangeFactor = PositionErrorAtRangeFactor
    };
}
