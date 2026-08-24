using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Sensing;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Serialization;

/// <summary>
/// Shared experiment recipe used by the CLI and GUI. Directives are the same
/// deployer + parameter bags the UI already edits; world/batch knobs live here
/// instead of a one-off CLI flag list.
/// </summary>
public sealed class ExperimentRecipe
{
    /// <summary>Default filename opened when the CLI is invoked with no arguments.</summary>
    public const string DefaultFileName = "recipe.json";

    /// <summary>Experiment / run label.</summary>
    public string ExperimentName { get; set; } = "interactive";

    /// <summary>Optional notes copied into the export manifest.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Number of independent trials (seed, seed+1, …).</summary>
    public int Trials { get; set; } = 5;

    /// <summary>Ticks per trial.</summary>
    public int Ticks { get; set; } = 200;

    /// <summary>Base RNG seed.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Export directory. CLI <c>--out</c> overrides this when set.</summary>
    public string Out { get; set; } = "experiments/cli-last";

    /// <summary>World construction options.</summary>
    public RecipeWorld World { get; set; } = new();

    /// <summary>Ordered deployer schedule (same objects the GUI directive log uses).</summary>
    public List<RecipeDirective> Directives { get; set; } = new();

    /// <summary>Maps this recipe onto <see cref="WorldOptions"/>.</summary>
    public WorldOptions ToWorldOptions() => World.ToWorldOptions();

    /// <summary>
    /// Commented starter file: 10×10 world, Gaussian OrbitingCoop, periodic incidents.
    /// Same shape the GUI saves/loads.
    /// </summary>
    public static string TemplateText() => """
{
  // Shared CLI + GUI recipe. Add any registered deployer; parameters are that
  // deployer's public options (same fields as the GUI directive form).
  "experimentName": "interactive",
  "notes": "",
  "trials": 5,
  "ticks": 200,
  "seed": 42,
  "out": "experiments/cli-last",
  "world": {
    "title": "New World",
    "tileColumns": 10,
    "tileRows": 10,
    "tileWidthPx": 40,
    "tileHeightPx": 40,
    "exactSensing": false,
    "sensing": {
      "detectionExponent": 2,
      "errorExponent": 2,
      "positionErrorAtRangeFactor": 0.25
    }
  },
  "directives": [
    {
      "atTick": 0,
      "deployer": "Gaussian",
      "salt": 0,
      "parameters": {
        "Count": 10,
        "TypeName": "OrbitingCoop",
        "SensorRange": 80,
        "DeploySigma": 80
      }
    },
    {
      "atTick": 0,
      "deployer": "PeriodicIncidents",
      "salt": 0,
      "parameters": {
        "PeriodTicks": 25,
        "Count": 1,
        "IncidentType": "Incident"
      }
    }
  ]
}
""";
}

/// <summary>World size, seed-independent geometry, and sensing mode.</summary>
public sealed class RecipeWorld
{
    /// <summary>Window / manifest title.</summary>
    public string Title { get; set; } = "New World";

    /// <summary>Tile columns.</summary>
    public int TileColumns { get; set; } = 10;

    /// <summary>Tile rows.</summary>
    public int TileRows { get; set; } = 10;

    /// <summary>Tile width in pixels.</summary>
    public int TileWidthPx { get; set; } = 40;

    /// <summary>Tile height in pixels.</summary>
    public int TileHeightPx { get; set; } = 40;

    /// <summary>Legacy binary disc when true; cue sensing when false.</summary>
    public bool ExactSensing { get; set; }

    /// <summary>Cue-sensing profile (ignored when <see cref="ExactSensing"/> is true).</summary>
    public RecipeSensing Sensing { get; set; } = new();

    /// <summary>Build runtime world options.</summary>
    public WorldOptions ToWorldOptions() => new()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "World" : Title.Trim(),
        TileCount = new GridSize(TileColumns, TileRows),
        TileSizePx = new GridSize(TileWidthPx, TileHeightPx),
        ExactSensing = ExactSensing,
        Sensing = Sensing.ToProfile()
    };

    /// <summary>Capture world options into a recipe world block.</summary>
    public static RecipeWorld FromWorldOptions(WorldOptions options) => new()
    {
        Title = options.Title,
        TileColumns = options.TileCount.Width,
        TileRows = options.TileCount.Height,
        TileWidthPx = options.TileSizePx.Width,
        TileHeightPx = options.TileSizePx.Height,
        ExactSensing = options.ExactSensing,
        Sensing = RecipeSensing.FromProfile(options.Sensing)
    };
}

/// <summary>Serializable <see cref="IncidentSensingProfile"/>.</summary>
public sealed class RecipeSensing
{
    /// <summary>See <see cref="IncidentSensingProfile.DetectionExponent"/>.</summary>
    public double DetectionExponent { get; set; } = 2;

    /// <summary>See <see cref="IncidentSensingProfile.ErrorExponent"/>.</summary>
    public double ErrorExponent { get; set; } = 2;

    /// <summary>See <see cref="IncidentSensingProfile.PositionErrorAtRangeFactor"/>.</summary>
    public double PositionErrorAtRangeFactor { get; set; } = 0.25;

    /// <summary>Runtime profile.</summary>
    public IncidentSensingProfile ToProfile() => new()
    {
        DetectionExponent = DetectionExponent,
        ErrorExponent = ErrorExponent,
        PositionErrorAtRangeFactor = PositionErrorAtRangeFactor
    };

    /// <summary>Capture a runtime profile.</summary>
    public static RecipeSensing FromProfile(IncidentSensingProfile profile) => new()
    {
        DetectionExponent = profile.DetectionExponent,
        ErrorExponent = profile.ErrorExponent,
        PositionErrorAtRangeFactor = profile.PositionErrorAtRangeFactor
    };
}

/// <summary>One GUI-style directive: at tick T, run deployer D with a property bag.</summary>
public sealed class RecipeDirective
{
    /// <summary>World tick at which to attach (0 = before the first agent step).</summary>
    public long AtTick { get; set; }

    /// <summary>Registered deployer name (Gaussian, Random, PeriodicIncidents, PlaceNear, …).</summary>
    public string Deployer { get; set; } = "Gaussian";

    /// <summary>RNG salt mixed into that deployer (0 matches CLI/GUI default).</summary>
    public int Salt { get; set; }

    /// <summary>Deployer options, keyed by CLR property name.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Load/save <see cref="ExperimentRecipe"/> JSON (comments and trailing commas allowed).</summary>
public static class RecipeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serialize <paramref name="recipe"/> to <paramref name="path"/>.</summary>
    public static void Save(string path, ExperimentRecipe recipe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(recipe, Options));
    }

    /// <summary>Deserialize a recipe file.</summary>
    public static ExperimentRecipe Load(string path)
    {
        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ExperimentRecipe>(text, Options)
               ?? throw new InvalidOperationException($"Failed to parse recipe: {path}");
    }

    /// <summary>Write the commented template if <paramref name="path"/> does not exist.</summary>
    public static string EnsureDefault(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            File.WriteAllText(path, ExperimentRecipe.TemplateText());
        }

        return path;
    }
}

/// <summary>Turns a recipe + deployer name map into <see cref="DeployStep"/>s.</summary>
public static class RecipeCompiler
{
    /// <summary>Materialize deploy steps in list order.</summary>
    public static List<DeployStep> CompileSteps(
        ExperimentRecipe recipe,
        IReadOnlyDictionary<string, Func<IDeployer>> deployers)
    {
        var steps = new List<DeployStep>();
        foreach (var d in recipe.Directives)
        {
            if (string.IsNullOrWhiteSpace(d.Deployer))
                throw new InvalidOperationException("Recipe directive is missing a deployer name.");
            if (!deployers.TryGetValue(d.Deployer, out var factory))
            {
                throw new InvalidOperationException(
                    $"Unknown deployer '{d.Deployer}'. Available: {string.Join(", ", deployers.Keys)}");
            }

            var sample = factory();
            var options = DeployerParameterCatalog.Materialize(sample.OptionsType, d.Parameters);
            steps.Add(new DeployStep
            {
                CreateDeployer = factory,
                Options = options,
                AtTick = d.AtTick,
                RandomSalt = d.Salt
            });
        }

        return steps;
    }
}
