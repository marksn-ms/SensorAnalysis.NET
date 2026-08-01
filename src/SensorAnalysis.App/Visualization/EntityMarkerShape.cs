namespace SensorAnalysis.App.Visualization;

/// <summary>Marker geometry used when drawing an entity on the world canvas.</summary>
public enum EntityMarkerShape
{
    /// <summary>Disk (default for most agents).</summary>
    Circle = 0,

    /// <summary>Axis-aligned square.</summary>
    Square,

    /// <summary>Rotated square (diamond).</summary>
    Diamond,

    /// <summary>Upward-pointing triangle (default for incidents).</summary>
    Triangle
}
