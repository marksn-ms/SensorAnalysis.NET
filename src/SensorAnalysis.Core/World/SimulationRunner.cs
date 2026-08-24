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
    /// </summary>
    /// <param name="options">World construction options.</param>
    /// <param name="seed">Random seed used to initialize the world deterministically.</param>
    public void Reset(WorldOptions options, int seed)
    {
        World = new SimWorld(options, seed);
        IsRunning = false;
    }

    /// <summary>
    /// Clears entities and deployers, then attaches each step in order.
    /// CLI and UI both use this path so schedules stay aligned.
    /// </summary>
    /// <param name="steps">Ordered deploy steps for the trial.</param>
    /// <param name="context">Agent resolution and base RNG salt.</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Reset"/> has not been called first.</exception>
    public void Deploy(IReadOnlyList<DeployStep> steps, DeployContext context)
    {
        if (World is null) throw new InvalidOperationException("Call Reset first.");
        World.ClearEntities();
        World.ClearDeployers();
        foreach (var step in steps)
            AttachDeployer(step, context);
    }

    /// <summary>
    /// Additive attach used by the GUI directive log (does not clear the world).
    /// Always constructs a fresh deployer instance so ticking state is not shared.
    /// </summary>
    /// <param name="step">Deployer factory + options.</param>
    /// <param name="context">Agent resolution and RNG salt (directive id mixed in by caller).</param>
    public void AttachDeployer(DeployStep step, DeployContext context)
    {
        if (World is null) throw new InvalidOperationException("Call Reset first.");
        var deployer = step.CreateDeployer();
        deployer.Attach(World, step.Options, context);
        World.AddDeployer(deployer);
    }

    private void ApplyDueSteps(
        IReadOnlyList<DeployStep> steps,
        HashSet<int> applied,
        long tick,
        DeployContext baseContext)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            if (applied.Contains(i)) continue;
            var step = steps[i];
            if (step.AtTick > tick) continue;
            applied.Add(i);
            AttachDeployer(step, new DeployContext
            {
                ResolveAgent = baseContext.ResolveAgent,
                RandomSalt = step.RandomSalt
            });
        }
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
    /// <param name="request">Experiment configuration.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>An <see cref="ExperimentManifest"/> describing the completed experiment run.</returns>
    public ExperimentManifest RunExperiment(ExperimentRequest request, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        string[] agentTypes = Array.Empty<string>();
        var agentCount = 0;
        var incidentHint = 0;

        for (int trial = 0; trial < request.Trials; trial++)
        {
            ct.ThrowIfCancellationRequested();
            var seed = request.BaseSeed + trial;
            Reset(request.WorldOptions, seed);
            World!.CurrentTrial = trial;

            var applied = new HashSet<int>();
            ApplyDueSteps(request.Steps, applied, tick: 0, request.Context);

            agentTypes = World.Agents.Select(a => a.AgentType).Distinct().ToArray();
            agentCount = World.Agents.Count;
            incidentHint = World.Incidents.Count;

            foreach (var w in _watchers)
                w.OnTrialStart(World, trial);

            IsRunning = true;
            try
            {
                for (int i = 0; i < request.TicksPerTrial; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    ApplyDueSteps(request.Steps, applied, World.CurrentTick, request.Context);
                    Step();
                }
            }
            finally
            {
                IsRunning = false;
            }

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
            AgentCount = agentCount,
            IncidentCount = incidentHint,
            AgentTypes = agentTypes,
            Notes = request.Notes
        };
    }
}

/// <summary>
/// One deployer attachment: factory + options snapshot.
/// </summary>
public sealed class DeployStep
{
    /// <summary>Creates a fresh deployer instance (required so ticking state is not shared).</summary>
    public required Func<IDeployer> CreateDeployer { get; init; }

    /// <summary>Options object matching the deployer's <see cref="IDeployer.OptionsType"/>.</summary>
    public required object Options { get; init; }

    /// <summary>Attach when the world tick reaches this value (0 = before the first step).</summary>
    public long AtTick { get; init; }

    /// <summary>Per-step RNG salt (0 is the shared CLI/GUI default).</summary>
    public int RandomSalt { get; init; }
}

/// <summary>
/// Configuration for running a multi-trial experiment.
/// </summary>
public sealed class ExperimentRequest
{
    /// <summary>Experiment identifier or name.</summary>
    public string ExperimentName { get; set; } = "run";

    /// <summary>Base RNG seed; each trial uses base + trialIndex.</summary>
    public int BaseSeed { get; set; } = 42;

    /// <summary>Number of trials to execute.</summary>
    public int Trials { get; set; } = 1;

    /// <summary>Number of simulation ticks to run per trial.</summary>
    public int TicksPerTrial { get; set; } = 500;

    /// <summary>World options used to construct the simulation world for each trial.</summary>
    public required WorldOptions WorldOptions { get; init; }

    /// <summary>Ordered deploy steps applied at the start of each trial.</summary>
    public required IReadOnlyList<DeployStep> Steps { get; init; }

    /// <summary>Shared agent resolution for all steps.</summary>
    public required DeployContext Context { get; init; }

    /// <summary>Optional notes attached to the experiment manifest.</summary>
    public string Notes { get; set; } = "";
}
