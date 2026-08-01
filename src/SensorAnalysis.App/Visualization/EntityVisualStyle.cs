namespace SensorAnalysis.App.Visualization;

/// <summary>
/// Presentation hint for an entity type on the world canvas.
/// Kept in the App layer so Core/CLI stay free of UI concerns.
/// </summary>
/// <param name="TypeKey">Agent type or incident type name this style applies to.</param>
/// <param name="Shape">Marker geometry.</param>
/// <param name="R">Fill red channel (0–255).</param>
/// <param name="G">Fill green channel (0–255).</param>
/// <param name="B">Fill blue channel (0–255).</param>
/// <param name="Radius">Base marker radius in screen pixels before zoom scaling.</param>
public readonly record struct EntityVisualStyle(
    string TypeKey,
    EntityMarkerShape Shape,
    byte R,
    byte G,
    byte B,
    double Radius = 5.0);
