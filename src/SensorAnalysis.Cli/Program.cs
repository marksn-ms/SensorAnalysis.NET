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
//   SensorAnalysis.Cli.exe --regime honest --trials 30 --ticks 400 --agents 8 --incidents 6 \
//     --cols 10 --rows 10 --adversaries 0 --seed 42 --out ./experiments/honest
//
// Regimes: random | greedy | honest | deceptive | mitigated

var trials = GetInt(args, "--trials", 5);
var ticks = GetInt(args, "--ticks", 200);
var agents = GetInt(args, "--agents", 10);
var incidents = GetInt(args, "--incidents", 5);
var adversaries = GetInt(args, "--adversaries", 0);
var seed = GetInt(args, "--seed", 42);
var sigma = GetDouble(args, "--sigma", 80);
var sensorRange = GetDouble(args, "--sensor-range", 80);
var cols = GetInt(args, "--cols", 10);
var rows = GetInt(args, "--rows", 10);
var tilePx = GetInt(args, "--tile-px", 40);
var regime = GetString(args, "--regime", "honest");
var agentType = GetString(args, "--agent-type", "");
var outDir = GetString(args, "--out", Path.Combine("experiments", $"cli-{DateTime.Now:yyyyMMdd-HHmmss}"));

var (honestType, adversaryType, advCount) = ResolveRegime(regime, agentType, adversaries);

var worldW = cols * tilePx;
var worldH = rows * tilePx;
var area = (double)worldW * worldH;
var r2 = sensorRange * sensorRange;
// Kershner-style order-of-magnitude 1-cover count for disk sensors on area A
var nStar = 4.0 * area / (3.0 * Math.Sqrt(3.0) * r2);
var density = agents / (double)(cols * rows);

var placeNearAdv = GetFlag(args, "--incidents-near-adversaries");
// Default false: keep incident spatial process identical across regimes.
// Omniscient adversaries + uniform random infiltrator placement carry the stress test.

Console.WriteLine($"SensorAnalysis CLI");
Console.WriteLine($"  regime={regime} trials={trials} ticks={ticks}");
Console.WriteLine($"  grid={cols}x{rows} ({cols * rows} cells) tilePx={tilePx} world={worldW}x{worldH}");
Console.WriteLine($"  agents={agents} ({density:P1} of cells) adversaries={advCount} incidents={incidents}");
Console.WriteLine($"  sensorRange={sensorRange} n*≈{nStar:F1} (sparse if agents << n*)");
Console.WriteLine($"  incidentsNearAdversaries={placeNearAdv}");
Console.WriteLine($"  honest={honestType} adversary={adversaryType ?? "-"} seed={seed}");
Console.WriteLine($"  out={outDir}");

var registry = CreateRegistry();
var honestFactory = registry.Get(honestType);
var adversaryFactory = adversaryType is null ? null : registry.Get(adversaryType);

var coverage = new CoverageWatcher(sampleEveryTicks: 1);
var connectivity = new ConnectivityWatcher(sampleEveryTicks: 5);
var clearance = new IncidentClearanceWatcher(sampleEveryTicks: 1);
var trustTel = new TrustTelemetryWatcher(sampleEveryTicks: 5);
var runner = new SimulationRunner();
runner.AddWatcher(coverage);
runner.AddWatcher(connectivity);
runner.AddWatcher(clearance);
runner.AddWatcher(trustTel);

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
        TileCount = new GridSize(cols, rows),
        TileSizePx = new GridSize(tilePx, tilePx)
    },
    Deployer = new GaussianDeployer(),
    DeployRequest = new DeployRequest
    {
        AgentCount = agents,
        IncidentCount = incidents,
        AgentTypeName = honestType,
        SensorRange = sensorRange,
        DeploySigma = sigma,
        AdversaryCount = advCount,
        PlaceIncidentsNearAdversaries = placeNearAdv,
        AgentFactory = _ => honestFactory.Create(),
        AdversaryFactory = adversaryFactory is null ? null : _ => adversaryFactory.Create()
    },
    Notes = $"CLI: {string.Join(' ', args)}; n*≈{nStar:F2}; agents/cells={density:F4}; incidentsNearAdv={placeNearAdv}"
});

ExperimentExporter.Export(outDir, manifest, new IWatcher[] { coverage, connectivity, clearance, trustTel });
Console.WriteLine($"Exported → {Path.GetFullPath(outDir)}");
Console.WriteLine("  run-meta.json");
Console.WriteLine("  metrics.csv");
return 0;

static (string Honest, string? Adversary, int AdvCount) ResolveRegime(
    string regime, string agentTypeOverride, int adversaries)
{
    return regime.ToLowerInvariant() switch
    {
        "random" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "RandomWalk" : agentTypeOverride,
            null,
            0),
        "honest" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "OrbitingCoop" : agentTypeOverride,
            null,
            0),
        "deceptive" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "OrbitingCoop" : agentTypeOverride,
            "HerdingDeceptive",
            Math.Max(1, adversaries)),
        "mitigated" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "MitigatedOrbit" : agentTypeOverride,
            "HerdingDeceptive",
            Math.Max(1, adversaries)),
        "greedy" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "GreedyIncident" : agentTypeOverride,
            null,
            0),
        // Legacy reference agents retained:
        "legacy-honest" => ("CooperativeSearch", null, 0),
        "legacy-deceptive" => ("CooperativeSearch", "DeceptiveStigmergy", Math.Max(1, adversaries)),
        "legacy-mitigated" => ("TrustAwareSearch", "DeceptiveStigmergy", Math.Max(1, adversaries)),
        _ => throw new ArgumentException(
            $"Unknown --regime '{regime}'. Use random|greedy|honest|deceptive|mitigated (or legacy-*).")
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
    registry.Register(new OrbitingCoopAgentFactory());
    registry.Register(new HerdingDeceptiveAgentFactory());
    registry.Register(new MitigatedOrbitAgentFactory());
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

static bool GetFlag(string[] args, string name) =>
    Array.IndexOf(args, name) >= 0;
