using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SensorAnalysis.App.Visualization;

namespace SensorAnalysis.App.Models;

/// <summary>One row in the on-canvas legend (agent or incident type currently present).</summary>
public partial class LegendEntry : ObservableObject
{
    /// <summary>Creates a legend row from a resolved visual style.</summary>
    /// <param name="style">Style to summarize.</param>
    /// <param name="kindLabel">Short category label such as <c>agent</c> or <c>incident</c>.</param>
    public LegendEntry(EntityVisualStyle style, string kindLabel)
    {
        TypeKey = style.TypeKey;
        KindLabel = kindLabel;
        ShapeName = style.Shape.ToString();
        SwatchBrush = new SolidColorBrush(Color.FromRgb(style.R, style.G, style.B));
        SwatchBrush.Freeze();
    }

    /// <summary>Agent or incident type key.</summary>
    public string TypeKey { get; }

    /// <summary>Category label (<c>agent</c> / <c>incident</c>).</summary>
    public string KindLabel { get; }

    /// <summary>Shape name for display.</summary>
    public string ShapeName { get; }

    /// <summary>Fill brush matching the canvas marker.</summary>
    public SolidColorBrush SwatchBrush { get; }

    /// <summary>Combined caption, e.g. <c>OrbitingCoop · Circle</c>.</summary>
    public string Caption => $"{TypeKey} · {ShapeName}";
}
