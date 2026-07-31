using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Messaging;

/// <summary>
/// A message / marker left in the environment (or addressed to another agent).
/// TTL counts down each tick; when zero the message is removed.
/// </summary>
public class Message : WorldObject
{
    /// <summary>Agent type of the sender.</summary>
    public required string FromAgentType { get; init; }

    /// <summary>Runtime id of the sending agent, if known.</summary>
    public int? FromAgentId { get; init; }

    /// <summary>Opaque payload string (e.g. stigmergy marker content).</summary>
    public string Payload { get; set; } = "";

    /// <summary>Ticks remaining before this message expires. Default: 50.</summary>
    public int Ttl { get; set; } = 50;

    /// <summary>Target agent id, or <c>null</c> for a broadcast / environmental marker.</summary>
    public int? ToAgentId { get; init; }

    /// <summary>Decrements <see cref="Ttl"/> by one for the current tick.</summary>
    /// <param name="ctx">World context for the current tick.</param>
    public override void Tick(WorldContext ctx)
    {
        Ttl--;
    }

    /// <summary>True when <see cref="Ttl"/> has reached zero or below.</summary>
    public bool IsExpired => Ttl <= 0;
}
