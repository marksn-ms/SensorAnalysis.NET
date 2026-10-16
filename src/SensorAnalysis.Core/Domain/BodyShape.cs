namespace SensorAnalysis.Core.Domain;

/// <summary>
/// Fixed extent of a <see cref="Body"/>. Discs match the space engine's spherical
/// asteroids. Segments are capsules: a centerline of <see cref="Body.Length"/>
/// with circular caps of <see cref="Body.Radius"/>.
/// </summary>
public enum BodyShape
{
    /// <summary>Circle centered on <see cref="WorldObject.Position"/>.</summary>
    Disc = 0,

    /// <summary>Capsule along <see cref="Body.HeadingRadians"/>.</summary>
    Segment = 1
}
