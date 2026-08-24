using SensorAnalysis.Sphere.Core.Geometry;

namespace SensorAnalysis.Sphere.Core.Simulation;

/// <summary>An incident located on one geodesic face.</summary>
public sealed class SphericalIncident
{
    /// <summary>Creates an incident on a face with unit starting intensity.</summary>
    internal SphericalIncident(int id, SphereVector position, int faceId, string incidentType = "Incident")
    {
        Id = id;
        Position = position;
        FaceId = faceId;
        IncidentType = incidentType;
    }

    /// <summary>Stable incident id.</summary>
    public int Id { get; }

    /// <summary>Continuous unit-sphere position of the incident.</summary>
    public SphereVector Position { get; }

    /// <summary>Containing face used only by the world spatial index.</summary>
    internal int FaceId { get; }

    /// <summary>Logical incident type for visualization and metrics.</summary>
    public string IncidentType { get; }

    /// <summary>Remaining incident intensity. Zero means depleted.</summary>
    public double Intensity { get; set; } = 1;

    /// <summary>Gets whether this incident has been fully resolved.</summary>
    public bool IsDepleted => Intensity <= 0;
}
