using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Sphere.Core.Simulation;

namespace SensorAnalysis.Sphere.Core.Serialization;

/// <summary>A versioned, portable configuration for a spherical simulation world.</summary>
public sealed record SphericalScenarioDocument
{
    /// <summary>Current scenario document format version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Scenario format version.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Human-readable scenario title.</summary>
    public string Title { get; init; } = "Spherical world";

    /// <summary>Deterministic random seed.</summary>
    public int Seed { get; init; } = 42;

    /// <summary>World geometry and default agent parameters.</summary>
    public SphericalWorldOptions World { get; init; } = new();

    /// <summary>Scheduled deployment and watcher instructions.</summary>
    public List<SphericalDirective> Directives { get; init; } = [];
}

/// <summary>Reads and writes spherical scenario documents as indented JSON.</summary>
public static class SphericalScenarioSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Writes a scenario document to JSON at <paramref name="path"/>.</summary>
    public static void Save(string path, SphericalScenarioDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    /// <summary>Reads and validates a scenario document from JSON at <paramref name="path"/>.</summary>
    public static SphericalScenarioDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = JsonSerializer.Deserialize<SphericalScenarioDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Scenario document is empty.");
        if (document.Version != SphericalScenarioDocument.CurrentVersion)
            throw new InvalidDataException($"Unsupported spherical scenario version {document.Version}.");
        if (document.World.Radius <= 0 || document.World.Resolution < 1)
            throw new InvalidDataException("Scenario world geometry is invalid.");
        return document;
    }
}
