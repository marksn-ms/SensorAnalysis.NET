using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.Services;

/// <summary>Persisted GUI defaults: world options, seed, and environment directives.</summary>
public sealed class AppSettingsDocument
{
    /// <summary>Schema version for forward-compatible loads.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Last-used world configuration.</summary>
    public WorldSettings World { get; set; } = WorldSettings.CreateDefault();

    /// <summary>Environment directive log.</summary>
    public List<DirectiveSettings> Directives { get; set; } = [];
}

/// <summary>Serializable world options snapshot.</summary>
public sealed class WorldSettings
{
    /// <summary>World title.</summary>
    public string Title { get; set; } = "New World";

    /// <summary>Tile columns.</summary>
    public int TileColumns { get; set; } = 10;

    /// <summary>Tile rows.</summary>
    public int TileRows { get; set; } = 10;

    /// <summary>Tile width in pixels.</summary>
    public int TileWidthPx { get; set; } = 100;

    /// <summary>Tile height in pixels.</summary>
    public int TileHeightPx { get; set; } = 100;

    /// <summary>RNG seed.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Exact (non-noisy) sensing flag.</summary>
    public bool ExactSensing { get; set; }

    /// <summary>Default 10×10 grid with 100 px tiles.</summary>
    public static WorldSettings CreateDefault() => new();

    /// <summary>Converts to <see cref="WorldOptions"/>.</summary>
    public WorldOptions ToWorldOptions() => new()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "New World" : Title.Trim(),
        TileCount = new GridSize(Math.Max(1, TileColumns), Math.Max(1, TileRows)),
        TileSizePx = new GridSize(Math.Max(1, TileWidthPx), Math.Max(1, TileHeightPx)),
        ExactSensing = ExactSensing
    };

    /// <summary>Captures options and seed from the live UI state.</summary>
    public static WorldSettings From(WorldOptions options, int seed) => new()
    {
        Title = options.Title,
        TileColumns = options.TileCount.Width,
        TileRows = options.TileCount.Height,
        TileWidthPx = options.TileSizePx.Width,
        TileHeightPx = options.TileSizePx.Height,
        Seed = seed,
        ExactSensing = options.ExactSensing
    };
}

/// <summary>Serializable environment directive.</summary>
public sealed class DirectiveSettings
{
    /// <summary>Tick at which the directive runs.</summary>
    public long AtTick { get; set; }

    /// <summary>Deployer name (may be a legacy alias such as Random).</summary>
    public string DeployerName { get; set; } = "Random";

    /// <summary>Captured deployer and deployable parameter bag.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Reads and writes <see cref="AppSettingsDocument"/> under LocalAppData.</summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Path to the persisted settings file.</summary>
    public static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SensorAnalysis");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    /// <summary>Loads settings when the file exists and parses successfully.</summary>
    public static bool TryLoad(out AppSettingsDocument document)
    {
        document = new AppSettingsDocument();
        try
        {
            if (!File.Exists(SettingsPath))
                return false;

            var json = File.ReadAllText(SettingsPath);
            document = JsonSerializer.Deserialize<AppSettingsDocument>(json, JsonOptions)
                ?? new AppSettingsDocument();
            return true;
        }
        catch
        {
            document = new AppSettingsDocument();
            return false;
        }
    }

    /// <summary>Persists settings to disk.</summary>
    public static void Save(AppSettingsDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
