using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Sensing;

/// <summary>
/// Environment-side sensor model: events are not an exact in-range list.
/// Detection is a power-curve coin flip; a hit returns a noisy bearing whose
/// error grows with range and is capped (default <c>r/4</c>).
/// Deterministic given world seed, tick, observer id, and incident id.
/// </summary>
public static class CueSensor
{
    /// <summary>Detection probability at distance <paramref name="distance"/> for range <paramref name="sensorRange"/>.</summary>
    public static double DetectionProbability(
        double distance,
        double sensorRange,
        IncidentSensingProfile profile)
    {
        if (sensorRange <= 1e-9 || distance >= sensorRange)
            return 0;
        if (distance <= 1e-9)
            return 1;
        var u = distance / sensorRange;
        return Math.Pow(1.0 - u, Math.Max(0.01, profile.DetectionExponent));
    }

    /// <summary>
    /// Localization σ at this range. Zero at the source; <c>r · PositionErrorAtRangeFactor</c> at the horizon.
    /// </summary>
    public static double PositionSigma(
        double distance,
        double sensorRange,
        IncidentSensingProfile profile)
    {
        if (sensorRange <= 1e-9)
            return 0;
        var sigmaMax = sensorRange * Math.Max(0, profile.PositionErrorAtRangeFactor);
        var u = Math.Clamp(distance / sensorRange, 0, 1);
        return sigmaMax * Math.Pow(u, Math.Max(0.01, profile.ErrorExponent));
    }

    /// <summary>
    /// Try to produce a local cue for <paramref name="incident"/>. On success, <paramref name="cue"/>.Id is 0
    /// (no world identifier) and Position is the perceived location.
    /// </summary>
    public static bool TrySense(
        Incident incident,
        Agent observer,
        long tick,
        int worldSeed,
        IncidentSensingProfile profile,
        double worldWidth,
        double worldHeight,
        out IncidentInfo cue)
    {
        cue = default;
        var range = observer.SensorRange;
        var d = Vec2.ToroidalDistance(
            observer.Position, incident.Position, worldWidth, worldHeight);
        var p = DetectionProbability(d, range, profile);
        if (p <= 0)
            return false;

        var draw = Hash01(worldSeed, tick, observer.Id, incident.Id, 1);
        if (draw >= p)
            return false;

        var sigma = PositionSigma(d, range, profile);
        var sigmaMax = range * Math.Max(0, profile.PositionErrorAtRangeFactor);
        var (nx, ny) = HashGaussian(worldSeed, tick, observer.Id, incident.Id);
        var err = new Vec2(nx * sigma, ny * sigma);
        var errLen = err.Length;
        if (sigmaMax > 1e-9 && errLen > sigmaMax)
            err *= sigmaMax / errLen;

        var perceived = Vec2.Wrap(incident.Position + err, worldWidth, worldHeight);
        cue = new IncidentInfo(
            Id: 0,
            Position: perceived,
            Intensity: incident.Intensity,
            Strength: p);
        return true;
    }

    private static (double N1, double N2) HashGaussian(int seed, long tick, int observerId, int incidentId)
    {
        var u1 = Math.Max(1e-12, Hash01(seed, tick, observerId, incidentId, 2));
        var u2 = Hash01(seed, tick, observerId, incidentId, 3);
        var r = Math.Sqrt(-2.0 * Math.Log(u1));
        var theta = 2.0 * Math.PI * u2;
        return (r * Math.Cos(theta), r * Math.Sin(theta));
    }

    private static double Hash01(int seed, long tick, int observerId, int incidentId, int channel)
    {
        unchecked
        {
            var x = Mix((ulong)(uint)seed, (ulong)tick);
            x = Mix(x, (ulong)(uint)observerId);
            x = Mix(x, (ulong)(uint)incidentId);
            x = Mix(x, (ulong)(uint)channel);
            // 53-bit mantissa in [0, 1)
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }

    private static ulong Mix(ulong a, ulong b)
    {
        unchecked
        {
            var x = a + 0x9E3779B97F4A7C15UL + (b << 6) + (b >> 2);
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
