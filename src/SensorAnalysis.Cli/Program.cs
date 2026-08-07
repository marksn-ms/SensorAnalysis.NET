using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Agents.Watchers;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;

// Headless batch runner. Each deploy step places one kind of thing (matches UI directives).
//
//   --regime honest --agents 8 --adversaries 0 --incident-period 25 --incident-wave 1 ...
// Steps: Gaussian(honest) [+ Random(adversaries)] [+ UniformIncidents(init)]
//        [+ PeriodicIncidents]
//   --incidents-near-adversaries (adversary regimes only; explicit — never auto from advCount):
//        PeriodicIncidents + PlaceNear with rates summing to full load (see --near-share)
//   --near-share f  (default 0.5): PlaceNear gets fraction f of total incident rate;
//        uniform gets (1-f). Use f=1 for PlaceNear-only (no uniform stream).
//        Periods ≈ baseline/share and baseline/(1-share) when 0<f<1.

var trials = GetInt(args, "--trials", 5);
var ticks = GetInt(args, "--ticks", 200);
var agents = GetInt(args, "--agents", 10);
var incidents = GetInt(args, "--incidents", 0);
var incidentPeriod = GetInt(args, "--incident-period", 25);
var incidentWave = GetInt(args, "--incident-wave", 1);
var adversaries = GetInt(args, "--adversaries", 0);
var seed = GetInt(args, "--seed", 42);
var sigma = GetDouble(args, "--sigma", 80);
var sensorRange = GetDouble(args, "--sensor-range", 80);
var rangeFactor = GetDouble(args, "--near-range-factor", 0.5);
var nearShare = GetDouble(args, "--near-share", 0.5);
var cols = GetInt(args, "--cols", 10);
var rows = GetInt(args, "--rows", 10);
var tilePx = GetInt(args, "--tile-px", 40);
var regime = GetString(args, "--regime", "honest");
var agentType = GetString(args, "--agent-type", "");
var outDir = GetString(args, "--out", Path.Combine("experiments", $"cli-{DateTime.Now:yyyyMMdd-HHmmss}"));

var (honestType, adversaryType, advCount) = ResolveRegime(regime, agentType, adversaries);
var honestCount = Math.Max(0, agents - advCount);

var worldW = cols * tilePx;
var worldH = rows * tilePx;
var area = (double)worldW * worldH;
var r2 = sensorRange * sensorRange;
var nStar = 4.0 * area / (3.0 * Math.Sqrt(3.0) * r2);
var density = agents / (double)(cols * rows);

// Explicit flag only — do NOT auto-enable PlaceNear merely because adversaries > 0.
var placeNearAdv = GetFlag(args, "--incidents-near-adversaries");
if (nearShare <= 0 || nearShare > 1)
    throw new ArgumentException("--near-share must be in (0, 1] (e.g. 0.25, 0.5, 1.0 for PlaceNear-only).");

Console.WriteLine($"SensorAnalysis CLI");
Console.WriteLine($"  regime={regime} trials={trials} ticks={ticks}");
Console.WriteLine($"  grid={cols}x{rows} ({cols * rows} cells) tilePx={tilePx} world={worldW}x{worldH}");
Console.WriteLine($"  agents={agents} honest={honestCount} adversaries={advCount} ({density:P1} of cells)");
Console.WriteLine($"  incidents: initial={incidents} period={incidentPeriod} wave={incidentWave}");
Console.WriteLine($"  sensorRange={sensorRange} n*≈{nStar:F1} nearRangeFactor={rangeFactor}");
Console.WriteLine($"  placeNear={placeNearAdv} nearShare={nearShare:F2} honest={honestType} adversary={adversaryType ?? "-"} seed={seed}");
Console.WriteLine($"  out={outDir}");

var registry = CreateRegistry();

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

var context = new DeployContext
{
    ResolveAgent = name => registry.Get(name).Create()
};

var steps = new List<DeployStep>
{
    new()
    {
        CreateDeployer = () => new GaussianDeployer(),
        Options = new GaussianDeployOptions
        {
            Count = honestCount,
            TypeName = honestType,
            SensorRange = sensorRange,
            DeploySigma = sigma
        }
    }
};

if (advCount > 0 && adversaryType is not null)
{
    steps.Add(new DeployStep
    {
        CreateDeployer = () => new RandomDeployer(),
        Options = new RandomDeployOptions
        {
            Count = advCount,
            TypeName = adversaryType,
            SensorRange = sensorRange
        }
    });
}

if (incidents > 0)
{
    if (placeNearAdv && adversaryType is not null)
    {
        // One-shot near adversaries when explicitly requested.
        steps.Add(new DeployStep
        {
            CreateDeployer = () => new PlaceNearDeployer(),
            Options = new PlaceNearOptions
            {
                Count = incidents,
                NearTypeName = adversaryType,
                RangeFactor = rangeFactor,
                PeriodTicks = 0
            }
        });
    }
    else
    {
        steps.Add(new DeployStep
        {
            CreateDeployer = () => new UniformIncidentDeployer(),
            Options = new UniformIncidentOptions { Count = incidents }
        });
    }
}

if (incidentPeriod > 0)
{
    if (placeNearAdv && adversaryType is not null)
    {
        if (nearShare >= 1.0)
        {
            // PlaceNear-only: all incidents near infiltrators at the baseline period/wave.
            Console.WriteLine(
                $"  PlaceNear-only incidents: period={incidentPeriod} wave={incidentWave} near={adversaryType}");
            steps.Add(new DeployStep
            {
                CreateDeployer = () => new PlaceNearDeployer(),
                Options = new PlaceNearOptions
                {
                    Count = incidentWave,
                    NearTypeName = adversaryType,
                    RangeFactor = rangeFactor,
                    PeriodTicks = incidentPeriod
                }
            });
        }
        else
        {
            // Hybrid fair load: PlaceNear gets nearShare of total rate; uniform gets the rest.
            // Periods ≈ baseline/share so rates sum ≈ wave/incidentPeriod (same as uniform-only).
            var uniformPeriod = Math.Max(1, (int)Math.Round(incidentPeriod / (1.0 - nearShare)));
            var placeNearPeriod = Math.Max(1, (int)Math.Round(incidentPeriod / nearShare));
            var rateSum = incidentWave * (1.0 / uniformPeriod + 1.0 / placeNearPeriod);
            var targetRate = incidentWave / (double)incidentPeriod;
            Console.WriteLine(
                $"  hybrid incidents: PeriodicIncidents period={uniformPeriod} + PlaceNear period={placeNearPeriod} " +
                $"(nearShare={nearShare:F2}; rate≈{rateSum:F4}/tick vs target {targetRate:F4}/tick)");

            steps.Add(new DeployStep
            {
                CreateDeployer = () => new PeriodicIncidentDeployer(),
                Options = new PeriodicIncidentOptions
                {
                    PeriodTicks = uniformPeriod,
                    Count = incidentWave
                }
            });
            steps.Add(new DeployStep
            {
                CreateDeployer = () => new PlaceNearDeployer(),
                Options = new PlaceNearOptions
                {
                    Count = incidentWave,
                    NearTypeName = adversaryType,
                    RangeFactor = rangeFactor,
                    PeriodTicks = placeNearPeriod
                }
            });
        }
    }
    else
    {
        Console.WriteLine($"  uniform incidents: PeriodicIncidents period={incidentPeriod} wave={incidentWave}");
        steps.Add(new DeployStep
        {
            CreateDeployer = () => new PeriodicIncidentDeployer(),
            Options = new PeriodicIncidentOptions
            {
                PeriodTicks = incidentPeriod,
                Count = incidentWave
            }
        });
    }
}

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
    Steps = steps,
    Context = context,
    Notes = $"CLI: {string.Join(' ', args)}; n*≈{nStar:F2}; placeNear={placeNearAdv}; nearShare={nearShare}; rangeFactor={rangeFactor}"
});

ExperimentExporter.Export(outDir, manifest, new IWatcher[] { coverage, connectivity, clearance, trustTel });
Console.WriteLine($"Exported → {Path.GetFullPath(outDir)}");
return 0;

static (string Honest, string? Adversary, int AdvCount) ResolveRegime(
    string regime, string agentTypeOverride, int adversaries)
{
    return regime.ToLowerInvariant() switch
    {
        "random" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "RandomWalk" : agentTypeOverride,
            null, 0),
        "honest" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "OrbitingCoop" : agentTypeOverride,
            null, 0),
        "deceptive" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "OrbitingCoop" : agentTypeOverride,
            "HerdingDeceptive", Math.Max(1, adversaries)),
        "mitigated" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "MitigatedOrbit" : agentTypeOverride,
            "HerdingDeceptive", Math.Max(1, adversaries)),
        "greedy" => (
            string.IsNullOrWhiteSpace(agentTypeOverride) ? "GreedyIncident" : agentTypeOverride,
            null, 0),
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
