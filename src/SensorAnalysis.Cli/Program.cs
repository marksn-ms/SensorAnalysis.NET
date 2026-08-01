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
//     --regime honest --trials 30 --ticks 400 --agents 16 --incidents 8 \
//     --adversaries 0 --seed 42 --out ./experiments/honest
//
// Regimes: honest | deceptive | mitigated | greedy

var trials = GetInt(args, "--trials", 5);
var ticks = GetInt(args, "--ticks", 200);
var agents = GetInt(args, "--agents", 10);
var incidents = GetInt(args, "--incidents", 5);
var adversaries = GetInt(args, "--adversaries", 0);
var seed = GetInt(args, "--seed", 42);
var sigma = GetDouble(args, "--sigma", 80);
var regime = GetString(args, "--regime", "honest");
var agentType = GetString(args, "--agent-type", "");
var outDir = GetString(args, "--out", Path.Combine("experiments", $"cli-{DateTime.Now:yyyyMMdd-HHmmss}"));

var (honestType, adversaryType, advCount) = ResolveRegime(regime, agentType, adversaries);

Console.WriteLine($"SensorAnalysis CLI");
Console.WriteLine($"  regime={regime} trials={trials} ticks={ticks}");
Console.WriteLine($"  agents={agents} adversaries={advCount} incidents={incidents}");
Console.WriteLine($"  honest={honestType} adversary={adversaryType ?? "-"} seed={seed}");
Console.WriteLine($"  out={outDir}");

var registry = CreateRegistry();
var honestFactory = registry.Get(honestType);
var adversaryFactory = adversaryType is null ? null : registry.Get(adversaryType);

var coverage = new CoverageWatcher(sampleEveryTicks: 1);
var connectivity = new ConnectivityWatcher(sampleEveryTicks: 5);
var clearance = new IncidentClearanceWatcher(sampleEveryTicks: 1);
var runner = new SimulationRunner();
runner.AddWatcher(coverage);
runner.AddWatcher(connectivity);
runner.AddWatcher(clearance);

runner.TrialCompleted += trial =>
    Console.WriteLine($"  trial {trial + 1}/{trials} done");

var manifest = runner.RunExperiment(new ExperimentRequest
{
    ExperimentName = $"trust-{regime}",
    BaseSeed = seed,
    Trials = trials,
    TicksPerTrial = ticks,
    WorldOptions = new WorldOptions
    {
        Title = $"Trust Experiment ({regime})",
        TileCount = new GridSize(12, 12),
        TileSizePx = new GridSize(40, 40)
    },
    Deployer = new GaussianDeployer(),
    DeployRequest = new DeployRequest
    {
        AgentCount = agents,
        IncidentCount = incidents,
        AgentTypeName = honestType,
        SensorRange = 80,
        DeploySigma = sigma,
        AdversaryCount = advCount,
        AgentFactory = _ => honestFactory.Create(),
        AdversaryFactory = adversaryFactory is null ? null : _ => adversaryFactory.Create()
    },
    Notes = $"CLI: {string.Join(' ', args)}"
});

ExperimentExporter.Export(outDir, manifest, new IWatcher[] { coverage, connectivity, clearance });
Console.WriteLine($"Exported → {Path.GetFullPath(outDir)}");
Console.WriteLine("  run-meta.json");
Console.WriteLine("  metrics.csv");
return 0;

static (string Honest, string? Adversary, int AdvCount) ResolveRegime(
    string regime, string agentTypeOverride, int adversaries)
{
    return regime.ToLowerInvariant() switch
    {
        "honest" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "CooperativeSearch" : agentTypeOverride,
            null,
            0),
        "deceptive" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "CooperativeSearch" : agentTypeOverride,
            "DeceptiveStigmergy",
            Math.Max(1, adversaries)),
        "mitigated" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "TrustAwareSearch" : agentTypeOverride,
            "DeceptiveStigmergy",
            Math.Max(1, adversaries)),
        "greedy" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "GreedyIncident" : agentTypeOverride,
            null,
            0),
        _ => throw new ArgumentException(
            $"Unknown --regime '{regime}'. Use honest|deceptive|mitigated|greedy.")
    };
}

static AgentFactoryRegistry CreateRegistry()
{
    var registry = new AgentFactoryRegistry();
    registry.Register(new RandomWalkAgentFactory());
    registry.Register(new GreedyIncidentAgentFactory());
    registry.Register(new StigmergyAgentFactory());
    registry.Register(new CooperativeSearchAgentFactory());
    registry.Register(new DeceptiveStigmergyAgentFactory());
    registry.Register(new TrustAwareSearchAgentFactory());
    return registry;
}

static int GetInt(string[] args, string name, int defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : defaultValue;
}

static double GetDouble(string[] args, string name, double defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v) ? v : defaultValue;
}

static string GetString(string[] args, string name, string defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : defaultValue;
}
