using System.Globalization;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Messaging;

/// <summary>High-level radio claim / state-change kinds used by agents and the narrator.</summary>
public enum RadioClaimKind
{
    /// <summary>Initial discovery or join report.</summary>
    Contact,

    /// <summary>Confirmed on-station / orbiting at ~r/2.</summary>
    Orbit,

    /// <summary>Periodic status while monitoring (legacy algorithmic rebroadcast).</summary>
    Status,

    /// <summary>Abandoned commitment; back to search.</summary>
    Break
}

/// <summary>
/// Compact radio payload codecs for significant agent state changes and status.
/// Keeps agents from being chatty: emit only on transitions (plus sparse status).
/// </summary>
public static class RadioNet
{
    /// <summary>First contact / join prefix.</summary>
    public const string ContactPrefix = "contact:";

    /// <summary>On-station / orbit confirmed prefix.</summary>
    public const string OrbitPrefix = "orbit:";

    /// <summary>Break / abandoned prefix.</summary>
    public const string BreakPrefix = "break:";

    /// <summary>Formats a first-contact or join report.</summary>
    public static string FormatContact(int incidentId, Vec2 position) =>
        FormatPos(ContactPrefix, incidentId, position);

    /// <summary>Formats an on-station orbit confirmation.</summary>
    public static string FormatOrbit(int incidentId, Vec2 position) =>
        FormatPos(OrbitPrefix, incidentId, position);

    /// <summary>Formats a break / return-to-search announcement.</summary>
    public static string FormatBreak(int incidentId) =>
        string.Create(CultureInfo.InvariantCulture, $"{BreakPrefix}{incidentId}");

    /// <summary>
    /// Parses contact, orbit, or legacy <see cref="IncidentMarker"/> status claims.
    /// </summary>
    public static bool TryParseClaim(
        string payload,
        out RadioClaimKind kind,
        out int incidentId,
        out Vec2 position)
    {
        kind = RadioClaimKind.Status;
        incidentId = 0;
        position = default;

        if (TryParsePos(payload, ContactPrefix, out incidentId, out position))
        {
            kind = RadioClaimKind.Contact;
            return true;
        }

        if (TryParsePos(payload, OrbitPrefix, out incidentId, out position))
        {
            kind = RadioClaimKind.Orbit;
            return true;
        }

        if (IncidentMarker.TryParse(payload, out incidentId, out position))
        {
            kind = RadioClaimKind.Status;
            return true;
        }

        return false;
    }

    /// <summary>Parses a break announcement.</summary>
    public static bool TryParseBreak(string payload, out int incidentId)
    {
        incidentId = 0;
        if (string.IsNullOrEmpty(payload) ||
            !payload.StartsWith(BreakPrefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(payload[BreakPrefix.Length..], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out incidentId);
    }

    private static string FormatPos(string prefix, int incidentId, Vec2 position) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{prefix}{incidentId}:{position.X:F2}:{position.Y:F2}");

    private static bool TryParsePos(string payload, string prefix, out int incidentId, out Vec2 position)
    {
        incidentId = 0;
        position = default;
        if (string.IsNullOrEmpty(payload) ||
            !payload.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = payload.Split(':');
        if (parts.Length < 4) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out incidentId))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        position = new Vec2(x, y);
        return true;
    }
}
