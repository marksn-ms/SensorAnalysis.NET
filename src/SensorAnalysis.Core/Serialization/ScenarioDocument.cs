using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Serialization;

/// <summary>
/// JSON-serializable description of a world setup (size, seed, entities).
/// Used for save/load from the GUI and for reproducible CLI runs.
/// </summary>
public sealed class ScenarioDocument
{
    /// <summary>Human readable title for the scenario.</summary>
    public string Title { get; set; } = "Scenario";

    /// <summary>Seed used to initialize deterministic aspects of the world.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Number of tile columns in the world grid.</summary>
    public int TileColumns { get; set; } = 12;

    /// <summary>Number of tile rows in the world grid.</summary>
    public int TileRows { get; set; } = 12;

    /// <summary>Tile width in pixels for rendering.</summary>
    public int TileWidthPx { get; set; } = 40;

    /// <summary>Tile height in pixels for rendering.</summary>
    public int TileHeightPx { get; set; } = 40;

    /// <summary>Collection of agents to place when constructing the world.</summary>
    public List<AgentPlacement> Agents { get; set; } = new();

    /// <summary>Collection of incidents to place when constructing the world.</summary>
    public List<IncidentPlacement> Incidents { get; set; } = new();

    /// <summary>Optional user notes attached to the scenario.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Convert the document into runtime world options.</summary>
    /// <returns>A <see cref="WorldOptions"/> instance populated from this document.</returns>
    public WorldOptions ToWorldOptions() => new()
    {
        Title = Title,
        TileCount = new GridSize(TileColumns, TileRows),
        TileSizePx = new GridSize(TileWidthPx, TileHeightPx)
    };

    /// <summary>Create a scenario document from an existing <see cref="SimWorld"/> instance.</summary>
    /// <param name="world">World instance to extract a serializable scenario from.</param>
    /// <returns>A <see cref="ScenarioDocument"/> describing the provided world.</returns>
    public static ScenarioDocument FromWorld(SimWorld world) => new()
    {
        Title = world.Title,
        Seed = world.Seed,
        TileColumns = world.Options.TileCount.Width,
        TileRows = world.Options.TileCount.Height,
        TileWidthPx = world.Options.TileSizePx.Width,
        TileHeightPx = world.Options.TileSizePx.Height,
        Agents = world.Agents.Select(a => new AgentPlacement
        {
            Type = a.AgentType,
            X = a.Position.X,
            Y = a.Position.Y,
            SensorRange = a.SensorRange,
            Label = a.Label
        }).ToList(),
        Incidents = world.Incidents.Select(i => new IncidentPlacement
        {
            X = i.Position.X,
            Y = i.Position.Y,
            Intensity = i.Intensity,
            IncidentType = i.IncidentType,
            Label = i.Label
        }).ToList()
    };
}

/// <summary>Placement description for a single agent when serializing a scenario.</summary>
public sealed class AgentPlacement
{
    /// <summary>Agent factory/type name. Example: "RandomWalk".</summary>
    public string Type { get; set; } = "RandomWalk";

    /// <summary>X-coordinate of agent placement in world space.</summary>
    public double X { get; set; }

    /// <summary>Y-coordinate of agent placement in world space.</summary>
    public double Y { get; set; }

    /// <summary>Sensor range for the agent in world units.</summary>
    public double SensorRange { get; set; } = 80;

    /// <summary>Optional label for the agent.</summary>
    public string Label { get; set; } = "";
}

/// <summary>Placement description for a single incident when serializing a scenario.</summary>
public sealed class IncidentPlacement
{
    /// <summary>X-coordinate of incident placement in world space.</summary>
    public double X { get; set; }

    /// <summary>Y-coordinate of incident placement in world space.</summary>
    public double Y { get; set; }

    /// <summary>Intensity of the incident (domain-specific meaning).</summary>
    public double Intensity { get; set; } = 1.0;

    /// <summary>Logical incident kind used for GUI styling (default: <c>Incident</c>).</summary>
    public string IncidentType { get; set; } = "Incident";

    /// <summary>Optional label for the incident.</summary>
    public string Label { get; set; } = "";
}

/// <summary>Helpers to serialize/deserialize <see cref="ScenarioDocument"/> instances.</summary>
public static class ScenarioSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Saves the specified <see cref="ScenarioDocument"/> to a file at the given path in JSON format.
    /// </summary>
    /// <param name="path">The file path where the scenario document will be saved.</param>
    /// <param name="doc">The <see cref="ScenarioDocument"/> instance to serialize and save.</param>
    public static void Save(string path, ScenarioDocument doc) =>
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options));

    /// <summary>
    /// Loads a <see cref="ScenarioDocument"/> from a file at the given path in JSON format.
    /// </summary>
    /// <param name="path">The file path from which the scenario document will be loaded.</param>
    /// <returns>The deserialized <see cref="ScenarioDocument"/> instance.</returns>
    public static ScenarioDocument Load(string path) =>
        JsonSerializer.Deserialize<ScenarioDocument>(File.ReadAllText(path), Options)
        ?? throw new InvalidOperationException($"Failed to parse scenario: {path}");
}
