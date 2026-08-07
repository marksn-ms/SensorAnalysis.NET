using System.Globalization;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Messaging;

/// <summary>One narrated radio transmission for UI / forensics.</summary>
public sealed class RadioLogEntry
{
    /// <summary>Creates a log entry.</summary>
    public RadioLogEntry(
        long tick,
        int messageId,
        int? fromAgentId,
        string fromAgentType,
        string payload,
        string narrative,
        string kind)
    {
        Tick = tick;
        MessageId = messageId;
        FromAgentId = fromAgentId;
        FromAgentType = fromAgentType;
        Payload = payload;
        Narrative = narrative;
        Kind = kind;
    }

    /// <summary>World tick when transmitted.</summary>
    public long Tick { get; }

    /// <summary>Underlying message id.</summary>
    public int MessageId { get; }

    /// <summary>Sender agent id.</summary>
    public int? FromAgentId { get; }

    /// <summary>Sender agent type (for color coding).</summary>
    public string FromAgentType { get; }

    /// <summary>Raw payload.</summary>
    public string Payload { get; }

    /// <summary>Fighter-pilot style narration.</summary>
    public string Narrative { get; }

    /// <summary>Short kind label (Contact, Orbit, Status, Break, Disappointment, Other).</summary>
    public string Kind { get; }
}

/// <summary>
/// Global radio traffic log filled by <see cref="World.SimWorld.Transmit"/>.
/// Not an <see cref="Metrics.IWatcher"/> — text/forensic, not CSV metrics.
/// </summary>
public sealed class RadioTrafficLog
{
    private readonly List<RadioLogEntry> _entries = new();
    private readonly object _gate = new();

    /// <summary>Maximum retained entries (oldest dropped).</summary>
    public int MaxEntries { get; set; } = 500;

    /// <summary>Snapshot of retained entries (oldest → newest).</summary>
    public IReadOnlyList<RadioLogEntry> Entries
    {
        get
        {
            lock (_gate)
                return _entries.ToList();
        }
    }

    /// <summary>Raised after each new entry (may be off the UI thread).</summary>
    public event Action<RadioLogEntry>? EntryAdded;

    /// <summary>Clears the log (e.g. on New World).</summary>
    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }

    /// <summary>
    /// Records a transmission with auto-narration.
    /// </summary>
    /// <param name="tick">Current world tick.</param>
    /// <param name="messageId">Message id.</param>
    /// <param name="fromAgentId">Sender id.</param>
    /// <param name="fromAgentType">Sender type.</param>
    /// <param name="payload">Raw payload.</param>
    /// <param name="senderPosition">Transmitter position (for heuristics).</param>
    /// <param name="sensorRange">Sender sensor range (for confirm vs spot).</param>
    public RadioLogEntry Record(
        long tick,
        int messageId,
        int? fromAgentId,
        string fromAgentType,
        string payload,
        Vec2 senderPosition,
        double sensorRange)
    {
        var (kind, narrative) = RadioNarrator.Narrate(
            fromAgentId, fromAgentType, payload, senderPosition, sensorRange);
        var entry = new RadioLogEntry(
            tick, messageId, fromAgentId, fromAgentType, payload, narrative, kind);

        lock (_gate)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }

        EntryAdded?.Invoke(entry);
        return entry;
    }
}

/// <summary>Turns opaque radio payloads into short fighter-pilot style callouts.</summary>
public static class RadioNarrator
{
    /// <summary>
    /// Produces a kind label and narrative line for a transmission.
    /// </summary>
    public static (string Kind, string Narrative) Narrate(
        int? fromAgentId,
        string fromAgentType,
        string payload,
        Vec2 senderPosition,
        double sensorRange)
    {
        var callsign = fromAgentId is int id ? $"Agent {id}" : "Unknown";
        var deceptive = IsDeceptiveType(fromAgentType);

        if (RadioNet.TryParseClaim(payload, out var claimKind, out var iid, out var pos))
        {
            var coord = FormatCoord(pos);
            return claimKind switch
            {
                RadioClaimKind.Contact => (
                    "Contact",
                    deceptive
                        ? $"Agents nearby, this is {callsign}, possible Tango spotted at {coord}."
                        : $"Agents nearby, this is {callsign}, possible Tango spotted at {coord}."),
                RadioClaimKind.Orbit => (
                    "Orbit",
                    $"Agents nearby, this is {callsign}, Tango confirmed at {coord}. Monitoring."),
                _ => (
                    "Status",
                    DistanceHint(senderPosition, pos, sensorRange) switch
                    {
                        <= 0.55 when !deceptive =>
                            $"Agents nearby, this is {callsign}, Tango update at {coord}. Still monitoring.",
                        _ when deceptive =>
                            $"Agents nearby, this is {callsign}, possible Tango at {coord}.",
                        _ =>
                            $"Agent {fromAgentId?.ToString() ?? "?"} responding to possible Tango at {coord}."
                    })
            };
        }

        if (RadioNet.TryParseBreak(payload, out var broken))
        {
            return (
                "Break",
                $"Agents nearby, this is {callsign}, breaking off Tango {broken}. Resuming search.");
        }

        if (IncidentMarker.TryParseDisappointment(payload, out var accused, out var reason))
        {
            var why = string.IsNullOrWhiteSpace(reason) ? "unreliable traffic" : reason;
            return (
                "Disappointment",
                $"Agents nearby, this is {callsign}, negative on Agent {accused} — {why}.");
        }

        return ("Other", $"{callsign}: {payload}");
    }

    private static bool IsDeceptiveType(string type) =>
        type.Contains("Deceptive", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("Herding", StringComparison.OrdinalIgnoreCase);

    private static double DistanceHint(Vec2 a, Vec2 b, double sensorRange)
    {
        if (sensorRange <= 1e-9) return 1.0;
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy) / sensorRange;
    }

    private static string FormatCoord(Vec2 p) =>
        string.Create(CultureInfo.InvariantCulture, $"({p.X:F0},{p.Y:F0})");
}
