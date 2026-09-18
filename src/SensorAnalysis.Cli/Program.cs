using System.Diagnostics;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Watchers;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;

// Recipe-driven batch runner. The JSON is the same document the GUI opens/saves.
//
//   SensorAnalysis.Cli                  create/open recipe.json in Notepad, then run
//   SensorAnalysis.Cli recipe.json      run that file
//   SensorAnalysis.Cli recipe.json --out experiments\foo
//   SensorAnalysis.Cli --edit [file]    Notepad only (default recipe.json)

var help = args.Any(a => a is "-h" or "--help" or "/?");
if (help)
{
    PrintUsage();
    return 0;
}

if (LooksLikeLegacyFlagSoup(args))
{
    Console.Error.WriteLine(
        "This CLI no longer takes deployer flags (--regime, --agents, …).");
    Console.Error.WriteLine(
        "Put those knobs in a recipe JSON (GUI File → Save Recipe, or run with no args to edit recipe.json).");
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}

var editOnly = HasFlag(args, "--edit");
var recipePath = ResolveRecipePath(args);
var outOverride = GetString(args, "--out");

if (editOnly || args.Length == 0)
{
    RecipeSerializer.EnsureDefault(recipePath);
    Console.WriteLine($"Editing {Path.GetFullPath(recipePath)}");
    LaunchNotepad(recipePath);
    if (editOnly)
        return 0;
}

if (!File.Exists(recipePath))
{
    Console.Error.WriteLine($"Recipe not found: {recipePath}");
    return 2;
}

var recipe = RecipeSerializer.Load(recipePath);
var outDir = string.IsNullOrWhiteSpace(outOverride) ? recipe.Out : outOverride;
if (string.IsNullOrWhiteSpace(outDir))
    outDir = Path.Combine("experiments", $"cli-{DateTime.Now:yyyyMMdd-HHmmss}");

var agents = BuiltinCatalog.CreateAgents();
var deployers = BuiltinCatalog.CreateDeployers();
var steps = RecipeCompiler.CompileSteps(recipe, deployers);
var worldOptions = recipe.ToWorldOptions();

Console.WriteLine("SensorAnalysis CLI");
Console.WriteLine($"  recipe={Path.GetFullPath(recipePath)}");
Console.WriteLine($"  experiment={recipe.ExperimentName} trials={recipe.Trials} ticks={recipe.Ticks} seed={recipe.Seed}");
Console.WriteLine($"  grid={worldOptions.TileCount.Width}x{worldOptions.TileCount.Height} tilePx={worldOptions.TileSizePx.Width} exactSensing={worldOptions.ExactSensing}");
Console.WriteLine($"  directives={recipe.Directives.Count}: {string.Join(" | ", recipe.Directives.Select(d => $"t{d.AtTick}:{d.Deployer}"))}");
Console.WriteLine($"  out={outDir}");

var coverage = new CoverageWatcher(sampleEveryTicks: 1);
var connectivity = new ConnectivityWatcher(sampleEveryTicks: 5);
var clearance = new IncidentClearanceWatcher(sampleEveryTicks: 1);
var trustTel = new TrustTelemetryWatcher(sampleEveryTicks: 5);
var crowdIntrusion = new CrowdIntrusionWatcher(sampleEveryTicks: 5);
var progressStall = new ProgressStallWatcher(sampleEveryTicks: 5);
var lloydOccupancy = new LloydOccupancyWatcher(sampleEveryTicks: 5);
var pheromoneField = new PheromoneFieldWatcher(sampleEveryTicks: 5);
var boidsPeel = new BoidsPeelWatcher(sampleEveryTicks: 5);
var runner = new SimulationRunner();
runner.AddWatcher(coverage);
runner.AddWatcher(connectivity);
runner.AddWatcher(clearance);
runner.AddWatcher(trustTel);
runner.AddWatcher(crowdIntrusion);
runner.AddWatcher(progressStall);
runner.AddWatcher(lloydOccupancy);
runner.AddWatcher(pheromoneField);
runner.AddWatcher(boidsPeel);
runner.TrialCompleted += trial =>
    Console.WriteLine($"  trial {trial + 1}/{recipe.Trials} done");

var deployables = BuiltinCatalog.CreateDeployables();
var context = new DeployContext
{
    Deployables = deployables
};

var manifest = runner.RunExperiment(new ExperimentRequest
{
    ExperimentName = string.IsNullOrWhiteSpace(recipe.ExperimentName) ? "recipe" : recipe.ExperimentName,
    BaseSeed = recipe.Seed,
    Trials = Math.Max(1, recipe.Trials),
    TicksPerTrial = Math.Max(1, recipe.Ticks),
    WorldOptions = worldOptions,
    Steps = steps,
    Context = context,
    Notes = string.IsNullOrWhiteSpace(recipe.Notes)
        ? $"recipe={Path.GetFullPath(recipePath)}"
        : recipe.Notes
});

ExperimentExporter.Export(outDir, manifest, new IWatcher[] { coverage, connectivity, clearance, trustTel, crowdIntrusion, progressStall, lloydOccupancy, pheromoneField, boidsPeel });
Console.WriteLine($"Exported → {Path.GetFullPath(outDir)}");
return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        SensorAnalysis.Cli — run a recipe JSON (same file the GUI saves).

          SensorAnalysis.Cli                       edit recipe.json in Notepad, then run
          SensorAnalysis.Cli <file.json>           run that recipe
          SensorAnalysis.Cli <file.json> --out DIR override export folder
          SensorAnalysis.Cli --edit [file.json]    edit only; do not run

        Directives in the JSON are deployer name + parameter bags (Gaussian, Random,
        Periodic, Uniform, …). Periodic has an optional Near type for host-relative
        placement. There is no --regime flag.

        The GUI still launches with form defaults when given no file. Pass a recipe JSON
        to SensorAnalysis.App.exe (or File → Open Recipe) only when you want that document.
        """);
}

static bool LooksLikeLegacyFlagSoup(string[] args) =>
    args.Any(a => a is "--regime" or "--agents" or "--adversaries" or "--agent-type"
        or "--incident-period" or "--incidents-near-adversaries" or "--near-share"
        or "--adversary-type" or "--exact-sensing" or "--cols" or "--sigma");

static bool HasFlag(string[] args, string name) =>
    Array.IndexOf(args, name) >= 0;

static string? GetString(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string ResolveRecipePath(string[] args)
{
    var named = GetString(args, "--recipe");
    if (!string.IsNullOrWhiteSpace(named))
        return named;

    var positional = FirstPositional(args);
    if (!string.IsNullOrWhiteSpace(positional))
        return positional;

    return ExperimentRecipe.DefaultFileName;
}

static string? FirstPositional(string[] args)
{
    var skipValue = false;
    foreach (var a in args)
    {
        if (skipValue)
        {
            skipValue = false;
            continue;
        }

        if (a is "--out" or "--recipe")
        {
            skipValue = true;
            continue;
        }

        if (a is "--edit" or "--help" or "-h" or "/?")
            continue;

        if (a.StartsWith('-'))
            continue;

        return a;
    }

    return null;
}

static void LaunchNotepad(string path)
{
    var psi = new ProcessStartInfo
    {
        FileName = "notepad.exe",
        Arguments = $"\"{path}\"",
        UseShellExecute = true
    };
    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("Could not start notepad.exe.");
    proc.WaitForExit();
}
