using System.Globalization;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Messaging;

/// <summary>
/// Shared payload format for incident <em>report messages</em>:
/// <c>incident:{id}:{x}:{y}</c> where (x,y) is the claimed location.
/// Used as the opaque <see cref="Message.Payload"/> on radio transmits (not a separate world channel).
/// </summary>
public static class IncidentMarker
{
    /// <summary>Payload prefix for incident reports.</summary>
    public const string Prefix = "incident:";

    /// <summary>Formats a truthful or deceptive incident claim.</summary>
    public static string Format(int incidentId, Vec2 claimedPosition) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{incidentId}:{claimedPosition.X:F2}:{claimedPosition.Y:F2}");

    /// <summary>Attempts to parse an incident report payload.</summary>
    public static bool TryParse(string payload, out int incidentId, out Vec2 claimedPosition)
    {
        incidentId = 0;
        claimedPosition = default;
        if (string.IsNullOrEmpty(payload) ||
            !payload.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = payload.Split(':');
        if (parts.Length < 4) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out incidentId))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        claimedPosition = new Vec2(x, y);
        return true;
    }
}
