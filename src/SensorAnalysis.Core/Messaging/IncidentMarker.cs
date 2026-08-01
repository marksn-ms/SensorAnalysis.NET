using System.Globalization;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Messaging;

/// <summary>
/// Shared payload codecs for incident reports and non-forgeable disappointment gossip.
/// Disappointment authenticity is enforced by receivers checking the live sender's agent type.
/// </summary>
public static class IncidentMarker
{
    /// <summary>Payload prefix for incident reports.</summary>
    public const string Prefix = "incident:";

    /// <summary>Payload prefix for disappointment / blame gossip.</summary>
    public const string DisappointmentPrefix = "disappointment:";

    /// <summary>Agent types allowed to emit authentic disappointment gossip.</summary>
    public static readonly HashSet<string> DisappointmentAuthorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MitigatedOrbit"
    };

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

    /// <summary>Formats disappointment gossip naming an accused sender.</summary>
    public static string FormatDisappointment(int accusedAgentId, string reason) =>
        $"{DisappointmentPrefix}{accusedAgentId}:{reason}";

    /// <summary>Attempts to parse disappointment gossip.</summary>
    public static bool TryParseDisappointment(string payload, out int accusedAgentId, out string reason)
    {
        accusedAgentId = 0;
        reason = "";
        if (string.IsNullOrEmpty(payload) ||
            !payload.StartsWith(DisappointmentPrefix, StringComparison.Ordinal))
            return false;

        var rest = payload[DisappointmentPrefix.Length..];
        var idx = rest.IndexOf(':');
        var idPart = idx < 0 ? rest : rest[..idx];
        reason = idx < 0 ? "" : rest[(idx + 1)..];
        return int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out accusedAgentId);
    }
}
