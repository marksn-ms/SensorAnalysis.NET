using System.Windows.Media;
using SensorAnalysis.App.Visualization;
using SensorAnalysis.Core.Messaging;

namespace SensorAnalysis.App.Models;

/// <summary>One narrated radio line for the traffic panel.</summary>
public sealed class RadioLogLine
{
    /// <summary>Creates a display line from a core log entry.</summary>
    public RadioLogLine(RadioLogEntry entry)
    {
        Tick = entry.Tick;
        Kind = entry.Kind;
        FromAgentType = entry.FromAgentType;
        Narrative = entry.Narrative;
        var style = EntityVisualCatalog.Default.ResolveAgent(entry.FromAgentType);
        TypeBrush = new SolidColorBrush(Color.FromRgb(style.R, style.G, style.B));
        TypeBrush.Freeze();
    }

    /// <summary>World tick.</summary>
    public long Tick { get; }

    /// <summary>Contact / Orbit / Status / Break / …</summary>
    public string Kind { get; }

    /// <summary>Sender agent type.</summary>
    public string FromAgentType { get; }

    /// <summary>Narrated callout.</summary>
    public string Narrative { get; }

    /// <summary>Color matching the sender type on the canvas.</summary>
    public SolidColorBrush TypeBrush { get; }

    /// <summary>Prefix shown in the list.</summary>
    public string Prefix => $"t{Tick} [{Kind}]";
}
