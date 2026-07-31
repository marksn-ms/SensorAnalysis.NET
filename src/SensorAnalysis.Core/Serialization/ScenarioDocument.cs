using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Metrics;

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

/// <summary>
/// Runs one or more trials of a scenario: deploy ? tick loop ? collect metrics.
/// Used by both the GUI (interactive) and the CLI (batch export).
/// </summary>
public sealed class SimulationRunner
{
    private readonly List<IWatcher> _watchers = new();

    /// <summary>
    /// Gets the currently active simulation world, or <c>null</c> if the runner has not been initialized.
    /// </summary>
    public SimWorld? World { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the runner is actively executing ticks.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Raised after each interactive tick completes.
    /// </summary>
    public event Action? TickCompleted;

    /// <summary>
    /// Raised when a trial completes. The event parameter is the zero-based trial index.
    /// </summary>
    public event Action<int>? TrialCompleted;

    /// <summary>
    /// Add a watcher that will observe world state and record metrics.
    /// </summary>
    /// <param name="watcher">Watcher to receive lifecycle callbacks.</param>
    public void AddWatcher(IWatcher watcher) => _watchers.Add(watcher);

    /// <summary>
    /// Gets the collection of registered watchers (read-only).
    /// </summary>
    public IReadOnlyList<IWatcher> Watchers => _watchers;

    /// <summary>
    /// Initialize or replace the internal <see cref="SimWorld"/> instance with the provided options and seed.
    /// After calling <see cref="Reset"/>, the <see cref="World"/> property will be non-null.
    /// </summary>
    /// <param name="options">World construction options.</param>
    /// <param name="seed">Random seed used to initialize the world deterministically.</param>
    public void Reset(WorldOptions options, int seed)
    {
        World = new SimWorld(options, seed);
        IsRunning = false;
    }

    /// <summary>
    /// Clear existing entities in the world and run the provided deployer to populate agents/incidents.
    /// </summary>
    /// <param name="deployer">The deployer implementation that places agents and incidents into the world.</param>
    /// <param name="request">Deployment parameters (counts, types, factory callbacks).</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Reset"/> has not been called first.</exception>
    public void Deploy(IDeployer deployer, DeployRequest request)
    {
        if (World is null) throw new InvalidOperationException("Call Reset first.");
        World.ClearEntities();
        deployer.Deploy(World, request);
    }

    /// <summary>Single interactive step.</summary>
    public void Step()
    {
        if (World is null) return;
        World.Tick();
        foreach (var w in _watchers)
            w.OnTick(World, World.CurrentTick - 1, World.CurrentTrial);
        TickCompleted?.Invoke();
    }

    /// <summary>
    /// Run <paramref name="ticks"/> ticks synchronously.
    /// Cancellation is cooperative via <paramref name="ct"/>.
    /// </summary>
    /// <param name="ticks">Number of ticks to execute.</param>
    /// <param name="ct">Cancellation token to stop execution early.</param>
    public void RunTicks(int ticks, CancellationToken ct = default)
    {
        if (World is null) return;
        IsRunning = true;
        try
        {
            for (int i = 0; i < ticks; i++)
            {
                ct.ThrowIfCancellationRequested();
                Step();
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Full multi-trial experiment. Returns a manifest ready for <see cref="ExperimentExporter"/>.
    /// </summary>
    /// <param name="request">Experiment configuration, including number of trials, ticks per trial and deployment details.</param>
    /// <param name="ct">Optional cancellation token to abort the experiment run.</param>
    /// <returns>An <see cref="ExperimentManifest"/> describing the completed experiment run.</returns>
    public ExperimentManifest RunExperiment(ExperimentRequest request, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        string[] agentTypes = Array.Empty<string>();

        for (int trial = 0; trial < request.Trials; trial++)
        {
            ct.ThrowIfCancellationRequested();
            var seed = request.BaseSeed + trial;
            Reset(request.WorldOptions, seed);
            World!.CurrentTrial = trial;

            foreach (var w in _watchers)
                w.OnTrialStart(World, trial);

            Deploy(request.Deployer, request.DeployRequest);
            agentTypes = World.Agents.Select(a => a.AgentType).Distinct().ToArray();

            RunTicks(request.TicksPerTrial, ct);

            foreach (var w in _watchers)
                w.OnTrialEnd(World, trial);

            TrialCompleted?.Invoke(trial);
        }

        return new ExperimentManifest
        {
            ExperimentName = request.ExperimentName,
            StartedAt = started,
            FinishedAt = DateTimeOffset.UtcNow,
            Seed = request.BaseSeed,
            Trials = request.Trials,
            TicksPerTrial = request.TicksPerTrial,
            WorldTitle = request.WorldOptions.Title,
            TileColumns = request.WorldOptions.TileCount.Width,
            TileRows = request.WorldOptions.TileCount.Height,
            AgentCount = request.DeployRequest.AgentCount,
            IncidentCount = request.DeployRequest.IncidentCount,
            AgentTypes = agentTypes,
            Notes = request.Notes
        };
    }
}

/// <summary>
/// Configuration for running a multi-trial experiment.
/// </summary>
public sealed class ExperimentRequest
{
    /// <summary>Experiment identifier or name.</summary>
    public string ExperimentName { get; set; } = "run";

    /// <summary>Base RNG seed used for the experiment; each trial will use base + trialIndex.</summary>
    public int BaseSeed { get; set; } = 42;

    /// <summary>Number of trials to execute.</summary>
    public int Trials { get; set; } = 1;

    /// <summary>Number of simulation ticks to run per trial.</summary>
    public int TicksPerTrial { get; set; } = 500;

    /// <summary>World options used to construct the simulation world for each trial.</summary>
    public required WorldOptions WorldOptions { get; init; }

    /// <summary>Deployer responsible for placing agents and incidents into the world.</summary>
    public required IDeployer Deployer { get; init; }

    /// <summary>Parameters controlling how to deploy agents and incidents.</summary>
    public required DeployRequest DeployRequest { get; init; }

    /// <summary>Optional notes attached to the experiment manifest.</summary>
    public string Notes { get; set; } = "";
}
