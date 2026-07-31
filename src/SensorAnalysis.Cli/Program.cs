using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Agents.Watchers;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

// Headless batch runner for publication-quality experiment exports.
//
// Usage:
//   dotnet run --project src/SensorAnalysis.Cli -- \
//     --trials 10 --ticks 500 --agents 20 --incidents 8 \
//     --agent-type GreedyIncident --seed 42 --out ./experiments/run1

var trials = GetInt(args, "--trials", 5);
var ticks = GetInt(args, "--ticks", 200);
var agents = GetInt(args, "--agents", 10);
var incidents = GetInt(args, "--incidents", 5);
var seed = GetInt(args, "--seed", 42);
var agentType = GetString(args, "--agent-type", "GreedyIncident");
var outDir = GetString(args, "--out", Path.Combine("experiments", $"cli-{DateTime.Now:yyyyMMdd-HHmmss}"));

Console.WriteLine($"SensorAnalysis CLI");
Console.WriteLine($"  trials={trials} ticks={ticks} agents={agents} incidents={incidents}");
Console.WriteLine($"  agent={agentType} seed={seed}");
Console.WriteLine($"  out={outDir}");

var registry = new AgentFactoryRegistry();
registry.Register(new RandomWalkAgentFactory());
registry.Register(new GreedyIncidentAgentFactory());
registry.Register(new StigmergyAgentFactory());
var factory = registry.Get(agentType);

var coverage = new CoverageWatcher(sampleEveryTicks: 1);
var connectivity = new ConnectivityWatcher(sampleEveryTicks: 1);
var runner = new SimulationRunner();
runner.AddWatcher(coverage);
runner.AddWatcher(connectivity);

runner.TrialCompleted += trial =>
    Console.WriteLine($"  trial {trial + 1}/{trials} done");

var manifest = runner.RunExperiment(new ExperimentRequest
{
    ExperimentName = "cli-batch",
    BaseSeed = seed,
    Trials = trials,
    TicksPerTrial = ticks,
    WorldOptions = new WorldOptions
    {
        Title = "CLI Experiment",
        TileCount = new GridSize(12, 12),
        TileSizePx = new GridSize(40, 40)
    },
    Deployer = new RandomDeployer(),
    DeployRequest = new DeployRequest
    {
        AgentCount = agents,
        IncidentCount = incidents,
        AgentTypeName = agentType,
        SensorRange = 80,
        AgentFactory = _ => factory.Create()
    },
    Notes = $"CLI: {string.Join(' ', args)}"
});

ExperimentExporter.Export(outDir, manifest, new IWatcher[] { coverage, connectivity });
Console.WriteLine($"Exported → {Path.GetFullPath(outDir)}");
Console.WriteLine("  run-meta.json");
Console.WriteLine("  metrics.csv");
return 0;

static int GetInt(string[] args, string name, int defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : defaultValue;
}

static string GetString(string[] args, string name, string defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : defaultValue;
}
