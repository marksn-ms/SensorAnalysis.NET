using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Metrics;

namespace SensorAnalysis.Core.World;

/// <summary>
/// Runs one or more trials of a scenario: deploy → tick loop → collect metrics.
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
