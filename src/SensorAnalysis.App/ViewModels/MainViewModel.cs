using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Agents.Watchers;
using SensorAnalysis.App.Controls;
using SensorAnalysis.App.Models;
using SensorAnalysis.App.Services;
using SensorAnalysis.App.Visualization;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.ViewModels;

/// <summary>
/// View model for the main application window. Encapsulates world creation, directive-driven
/// deployment, stepping, live metrics, and experiment exporting.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Core.World.SimulationRunner _runner = new();
    private readonly AgentFactoryRegistry _factories = new();
    private readonly Dictionary<string, IDeployer> _deployers = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _runTimer;
    private readonly CoverageWatcher _coverageWatcher = new(sampleEveryTicks: 1);
    private readonly ConnectivityWatcher _connectivityWatcher = new(sampleEveryTicks: 1);

    /// <summary>Set by the view so we can capture frames of the live canvas.</summary>
    public WorldCanvas? Canvas { get; set; }

    /// <summary>Creates a new instance of <see cref="MainViewModel"/> and initializes application state.</summary>
    public MainViewModel()
    {
        RegisterFactories();
        RegisterDeployers();

        AgentTypeNames = new ObservableCollection<string>(_factories.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        DeployerNames = new ObservableCollection<string>(_deployers.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        _runner.AddWatcher(_coverageWatcher);
        _runner.AddWatcher(_connectivityWatcher);
        _runner.TickCompleted += OnTickCompleted;

        RebuildMetricCatalog();
        SeedDefaultMetrics();
        SeedDefaultDirective();

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _runTimer.Tick += (_, _) => AdvanceOneTick();

        NewWorld();
    }

    [ObservableProperty] private SimWorld? _world;
    [ObservableProperty] private WorldObject? _selectedObject;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _tickText = "Tick 0";
    [ObservableProperty] private string _selectionSummary = "Nothing selected";
    [ObservableProperty] private bool _recordFrames;
    [ObservableProperty] private int _frameEveryNTicks = 10;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private EnvironmentDirective? _selectedDirective;
    [ObservableProperty] private MetricCatalogEntry? _selectedCatalogEntry;

    /// <summary>Draft fields for adding a new directive.</summary>
    [ObservableProperty] private long _draftAtTick;
    [ObservableProperty] private string _draftDeployerName = "Gaussian";
    [ObservableProperty] private int _draftAgentCount = 10;
    [ObservableProperty] private string _draftAgentTypeName = "OrbitingCoop";
    [ObservableProperty] private int _draftAdversaryCount;
    [ObservableProperty] private string _draftAdversaryTypeName = "HerdingDeceptive";
    [ObservableProperty] private int _draftIncidentCount = 5;

    /// <summary>Available agent factory names.</summary>
    public ObservableCollection<string> AgentTypeNames { get; }

    /// <summary>Available deployer names.</summary>
    public ObservableCollection<string> DeployerNames { get; }

    /// <summary>Scheduled / applied environment directives.</summary>
    public ObservableCollection<EnvironmentDirective> Directives { get; } = new();

    /// <summary>Pinned live metric series.</summary>
    public ObservableCollection<LiveMetricSeries> MetricSeries { get; } = new();

    /// <summary>Catalog of inherent + watcher metrics available to pin.</summary>
    public ObservableCollection<MetricCatalogEntry> MetricCatalog { get; } = new();

    /// <summary>Legend of agent/incident types currently present in the world.</summary>
    public ObservableCollection<LegendEntry> LegendEntries { get; } = new();

    private long _autoStopAt = long.MaxValue;
    private string? _frameDirectory;

    partial void OnSelectedObjectChanged(WorldObject? value)
    {
        SelectionSummary = value switch
        {
            Agent a => $"Agent #{a.Id} ({a.AgentType})\nPos: ({a.Position.X:F1}, {a.Position.Y:F1})\nRange: {a.SensorRange}\nReward: {a.TotalReward:F3}",
            Incident i => $"Incident #{i.Id}\nPos: ({i.Position.X:F1}, {i.Position.Y:F1})\nIntensity: {i.Intensity:F3}",
            _ => "Nothing selected"
        };
    }

    /// <summary>Create a new simulation world with default options and reset the runner.</summary>
    [RelayCommand]
    private void NewWorld()
    {
        Stop();
        var options = new WorldOptions
        {
            Title = "New World",
            TileCount = new GridSize(12, 12),
            TileSizePx = new GridSize(40, 40)
        };
        _runner.Reset(options, seed: 42);
        foreach (var d in Directives)
        {
            d.IsApplied = false;
            d.Status = "";
        }
        foreach (var s in MetricSeries)
            s.Reset();

        RefreshWorldBinding();
        StatusText = "World created. Apply tick-0 directives or press Run.";
        UpdateTickText();
        RefreshMetrics();
        RefreshLegend();
    }

    /// <summary>Append the draft fields as a new directive (not yet applied).</summary>
    [RelayCommand]
    private void AddDirective()
    {
        var d = new EnvironmentDirective
        {
            AtTick = DraftAtTick,
            DeployerName = DraftDeployerName,
            AgentCount = DraftAgentCount,
            AgentTypeName = DraftAgentTypeName,
            AdversaryCount = DraftAdversaryCount,
            AdversaryTypeName = DraftAdversaryTypeName,
            IncidentCount = DraftIncidentCount
        };
        Directives.Add(d);
        SelectedDirective = d;
        StatusText = $"Added directive: {d.Summary}";
    }

    /// <summary>Remove the selected directive from the log.</summary>
    [RelayCommand]
    private void RemoveDirective()
    {
        if (SelectedDirective is null) return;
        Directives.Remove(SelectedDirective);
        SelectedDirective = null;
    }

    /// <summary>
    /// Stamp the draft (or selected) directive at the current tick and apply it immediately.
    /// Simulation should be paused for mid-run injections.
    /// </summary>
    [RelayCommand]
    private void ApplyDirectiveNow()
    {
        if (_runner.World is null) return;
        if (IsRunning)
        {
            MessageBox.Show("Pause (Stop) before applying a directive mid-run.", "Apply directive");
            return;
        }

        EnvironmentDirective d;
        if (SelectedDirective is { IsApplied: false } sel)
        {
            d = sel;
            d.AtTick = _runner.World.CurrentTick;
        }
        else
        {
            d = new EnvironmentDirective
            {
                AtTick = _runner.World.CurrentTick,
                DeployerName = DraftDeployerName,
                AgentCount = DraftAgentCount,
                AgentTypeName = DraftAgentTypeName,
                AdversaryCount = DraftAdversaryCount,
                AdversaryTypeName = DraftAdversaryTypeName,
                IncidentCount = DraftIncidentCount
            };
            Directives.Add(d);
            SelectedDirective = d;
        }

        if (TryApplyDirective(d))
        {
            RefreshWorldBinding();
            UpdateTickText();
            RefreshMetrics();
            RefreshLegend();
            StatusText = $"Applied: {d.Summary}";
        }
    }

    /// <summary>Apply all due unapplied directives for the current tick without advancing time.</summary>
    [RelayCommand]
    private void ApplyDueDirectives()
    {
        if (_runner.World is null) return;
        var n = ApplyDueDirectivesCore();
        RefreshWorldBinding();
        UpdateTickText();
        RefreshMetrics();
        RefreshLegend();
        StatusText = n == 0 ? "No due directives." : $"Applied {n} directive(s) at tick {_runner.World.CurrentTick}.";
    }

    /// <summary>Perform a single interactive simulation step (applies due directives first).</summary>
    [RelayCommand]
    private void Step() => AdvanceOneTick();

    /// <summary>Start the automatic runner timer and begin stepping the simulation.</summary>
    [RelayCommand]
    private void Run()
    {
        if (RecordFrames)
        {
            _frameDirectory = Path.Combine(Environment.CurrentDirectory, "experiments",
                $"frames-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(_frameDirectory);
            StatusText = $"Running (recording frames → {_frameDirectory})…";
        }
        else
        {
            _frameDirectory = null;
            StatusText = "Running…";
        }
        _autoStopAt = long.MaxValue;
        IsRunning = true;
        _runTimer.Start();
    }

    /// <summary>Stop the automatic runner timer.</summary>
    [RelayCommand]
    private void Stop()
    {
        _runTimer.Stop();
        IsRunning = false;
        StatusText = _frameDirectory is not null
            ? $"Stopped. Frames in {_frameDirectory}"
            : "Stopped.";
    }

    /// <summary>Pin the selected catalog metric into the live metrics list.</summary>
    [RelayCommand]
    private void AddMetricSeries()
    {
        if (SelectedCatalogEntry is null) return;
        if (MetricSeries.Any(s => s.Key == SelectedCatalogEntry.Key))
        {
            StatusText = $"Already showing {SelectedCatalogEntry.DisplayName}.";
            return;
        }
        MetricSeries.Add(new LiveMetricSeries(SelectedCatalogEntry));
        RefreshMetrics();
    }

    /// <summary>Remove a pinned metric series.</summary>
    [RelayCommand]
    private void RemoveMetricSeries(LiveMetricSeries? series)
    {
        if (series is null) return;
        MetricSeries.Remove(series);
    }

    /// <summary>Capture a single frame image of the live world canvas and save it to disk.</summary>
    [RelayCommand]
    private void CaptureFrame()
    {
        if (Canvas is null || World is null)
        {
            MessageBox.Show("Canvas not ready.");
            return;
        }
        var dir = Path.Combine(Environment.CurrentDirectory, "experiments", "frames");
        var path = FrameCapture.CaptureTick(Canvas, dir, World.CurrentTick);
        StatusText = $"Saved {path}";
    }

    /// <summary>Show a save dialog and persist the current world as a scenario JSON file.</summary>
    [RelayCommand]
    private void SaveScenario()
    {
        if (World is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Scenario JSON|*.json",
            FileName = "scenario.json"
        };
        if (dlg.ShowDialog() != true) return;
        ScenarioSerializer.Save(dlg.FileName, ScenarioDocument.FromWorld(World));
        StatusText = $"Scenario saved: {dlg.FileName}";
    }

    /// <summary>Show an open dialog and load a scenario JSON into the world.</summary>
    [RelayCommand]
    private void LoadScenario()
    {
        var dlg = new OpenFileDialog { Filter = "Scenario JSON|*.json" };
        if (dlg.ShowDialog() != true) return;

        Stop();
        var doc = ScenarioSerializer.Load(dlg.FileName);
        _runner.Reset(doc.ToWorldOptions(), doc.Seed);
        var world = _runner.World!;

        foreach (var d in Directives)
        {
            d.IsApplied = false;
            d.Status = "superseded by scenario load";
        }

        foreach (var a in doc.Agents)
        {
            Agent agent;
            try { agent = _factories.Get(a.Type).Create(a.Label); }
            catch { agent = new RandomWalkAgent { Label = a.Label }; }
            agent.Position = new Vec2(a.X, a.Y);
            agent.SensorRange = a.SensorRange;
            world.Add(agent);
        }
        foreach (var i in doc.Incidents)
        {
            world.Add(new Incident
            {
                Position = new Vec2(i.X, i.Y),
                Intensity = i.Intensity,
                IncidentType = string.IsNullOrWhiteSpace(i.IncidentType) ? "Incident" : i.IncidentType,
                Label = i.Label
            });
        }

        foreach (var s in MetricSeries)
            s.Reset();

        RefreshWorldBinding();
        StatusText = $"Loaded scenario: {doc.Title} ({doc.Agents.Count} agents, {doc.Incidents.Count} incidents)";
        UpdateTickText();
        RefreshMetrics();
        RefreshLegend();
    }

    /// <summary>
    /// Run a small batch experiment from the first tick-0 directive (or draft defaults),
    /// collect metrics and export results to the experiments folder.
    /// </summary>
    [RelayCommand]
    private void RunExperiment()
    {
        Stop();
        if (_runner.World is null) return;

        var result = MessageBox.Show(
            "Run 5 trials × 200 ticks and export metrics to ./experiments/ ?",
            "Run Experiment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var template = Directives.FirstOrDefault(d => d.AtTick == 0) ?? new EnvironmentDirective
        {
            AtTick = 0,
            DeployerName = DraftDeployerName,
            AgentCount = DraftAgentCount,
            AgentTypeName = DraftAgentTypeName,
            AdversaryCount = DraftAdversaryCount,
            AdversaryTypeName = DraftAdversaryTypeName,
            IncidentCount = DraftIncidentCount
        };

        var coverage = new CoverageWatcher(1);
        var connectivity = new ConnectivityWatcher(1);
        var runner = new Core.World.SimulationRunner();
        runner.AddWatcher(coverage);
        runner.AddWatcher(connectivity);

        if (!_deployers.TryGetValue(template.DeployerName, out var deployer))
            deployer = new RandomDeployer();

        var manifest = runner.RunExperiment(new Core.World.ExperimentRequest
        {
            ExperimentName = "gui-batch",
            BaseSeed = 100,
            Trials = 5,
            TicksPerTrial = 200,
            WorldOptions = new WorldOptions
            {
                Title = "Batch",
                TileCount = new GridSize(12, 12),
                TileSizePx = new GridSize(40, 40)
            },
            Deployer = deployer,
            DeployRequest = BuildDeployRequest(template, salt: 0),
            Notes = "Launched from GUI"
        });

        var dir = Path.Combine(Environment.CurrentDirectory, "experiments",
            $"{manifest.ExperimentName}-{DateTime.Now:yyyyMMdd-HHmmss}");
        ExperimentExporter.Export(dir, manifest, new IWatcher[] { coverage, connectivity });
        StatusText = $"Experiment exported to {dir}";
        MessageBox.Show($"Exported to:\n{dir}", "Done");
    }

    /// <summary>Export the current run metrics and manifest (manual export) to disk.</summary>
    [RelayCommand]
    private void Export()
    {
        var dir = Path.Combine(Environment.CurrentDirectory, "experiments",
            $"manual-{DateTime.Now:yyyyMMdd-HHmmss}");
        var manifest = new ExperimentManifest
        {
            ExperimentName = "manual",
            Seed = World?.Seed ?? 0,
            Trials = 1,
            TicksPerTrial = (int)(World?.CurrentTick ?? 0),
            WorldTitle = World?.Title ?? "",
            TileColumns = World?.Options.TileCount.Width ?? 0,
            TileRows = World?.Options.TileCount.Height ?? 0,
            AgentCount = World?.Agents.Count ?? 0,
            IncidentCount = World?.Incidents.Count ?? 0,
            AgentTypes = World?.Agents.Select(a => a.AgentType).Distinct().ToArray() ?? Array.Empty<string>(),
            FinishedAt = DateTimeOffset.UtcNow
        };
        ExperimentExporter.Export(dir, manifest, _runner.Watchers);
        StatusText = $"Exported to {dir}";
        MessageBox.Show($"Exported to:\n{dir}", "Export");
    }

    /// <summary>Exit the application.</summary>
    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    private void AdvanceOneTick()
    {
        if (_runner.World is null) return;
        ApplyDueDirectivesCore();
        _runner.Step();
        if (_runner.World.CurrentTick >= _autoStopAt)
            Stop();
    }

    private int ApplyDueDirectivesCore()
    {
        if (_runner.World is null) return 0;
        var tick = _runner.World.CurrentTick;
        var due = Directives
            .Where(d => !d.IsApplied && d.AtTick <= tick)
            .OrderBy(d => d.AtTick)
            .ThenBy(d => d.Id)
            .ToList();
        var n = 0;
        foreach (var d in due)
        {
            if (TryApplyDirective(d))
                n++;
        }
        return n;
    }

    private bool TryApplyDirective(EnvironmentDirective d)
    {
        if (_runner.World is null) return false;
        try
        {
            if (!_deployers.TryGetValue(d.DeployerName, out var deployer))
                throw new InvalidOperationException($"Unknown deployer '{d.DeployerName}'.");

            var salt = unchecked((int)d.AtTick * 1009 ^ d.Id);
            var request = BuildDeployRequest(d, salt);
            // Additive: do not ClearEntities (supports mid-run injections and multiple tick-0 rows).
            deployer.Deploy(_runner.World, request);
            d.IsApplied = true;
            d.Status = "applied";
            return true;
        }
        catch (Exception ex)
        {
            d.Status = ex.Message;
            StatusText = $"Directive failed: {ex.Message}";
            return false;
        }
    }

    private DeployRequest BuildDeployRequest(EnvironmentDirective d, int salt)
    {
        var honestFactory = _factories.Get(d.AgentTypeName);
        Func<string, Agent>? advFactory = null;
        if (d.AdversaryCount > 0)
            advFactory = _ => _factories.Get(d.AdversaryTypeName).Create();

        return new DeployRequest
        {
            AgentCount = d.AgentCount,
            IncidentCount = d.IncidentCount,
            AgentTypeName = d.AgentTypeName,
            SensorRange = d.SensorRange,
            AgentFactory = _ => honestFactory.Create(),
            AdversaryCount = d.AdversaryCount,
            AdversaryFactory = advFactory,
            DeploySigma = d.DeploySigma,
            RandomSalt = salt
        };
    }

    private void OnTickCompleted()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateTickText();
            RefreshMetrics();
            RefreshLegend();
            RefreshWorldBinding();

            if (RecordFrames && Canvas is not null && _frameDirectory is not null && World is not null)
            {
                if (World.CurrentTick % Math.Max(1, FrameEveryNTicks) == 0)
                    FrameCapture.CaptureTick(Canvas, _frameDirectory, World.CurrentTick);
            }
        });
    }

    private void RefreshWorldBinding()
    {
        var w = _runner.World;
        World = null;
        World = w;
        Canvas?.Refresh();
    }

    private void UpdateTickText() =>
        TickText = $"Tick {World?.CurrentTick ?? 0}  |  Agents {World?.Agents.Count ?? 0}  |  Incidents {World?.Incidents.Count(i => !i.IsDepleted) ?? 0}";

    private void RefreshLegend()
    {
        LegendEntries.Clear();
        var catalog = EntityVisualCatalog.Default;
        if (_runner.World is null) return;

        foreach (var type in _runner.World.Agents.Select(a => a.AgentType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t))
            LegendEntries.Add(new LegendEntry(catalog.ResolveAgent(type), "agent"));

        foreach (var type in _runner.World.Incidents.Where(i => !i.IsDepleted)
                     .Select(i => i.IncidentType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t))
            LegendEntries.Add(new LegendEntry(catalog.ResolveIncident(type), "incident"));
    }

    private void RefreshMetrics()
    {
        foreach (var series in MetricSeries)
        {
            if (TryReadMetric(series.Key, out var value, out var format))
                series.Push(value, format);
        }
    }

    private bool TryReadMetric(string key, out double value, out string format)
    {
        value = 0;
        format = "G4";
        var world = _runner.World;

        if (key.StartsWith("inherent.", StringComparison.Ordinal))
        {
            switch (key)
            {
                case "inherent.tick":
                    value = world?.CurrentTick ?? 0;
                    format = "F0";
                    return true;
                case "inherent.agents":
                    value = world?.Agents.Count ?? 0;
                    format = "F0";
                    return true;
                case "inherent.active_incidents":
                    value = world?.Incidents.Count(i => !i.IsDepleted) ?? 0;
                    format = "F0";
                    return true;
                case "inherent.total_incidents":
                    value = world?.Incidents.Count ?? 0;
                    format = "F0";
                    return true;
                case "inherent.messages":
                    value = world?.Messages.Count ?? 0;
                    format = "F0";
                    return true;
                case "inherent.avg_reward":
                    value = world is null || world.Agents.Count == 0
                        ? 0
                        : world.Agents.Average(a => a.TotalReward);
                    format = "F3";
                    return true;
            }
            return false;
        }

        var dot = key.IndexOf('.');
        if (dot <= 0) return false;
        var watcherName = key[..dot];
        var column = key[(dot + 1)..];
        var watcher = _runner.Watchers.FirstOrDefault(w =>
            string.Equals(w.Name, watcherName, StringComparison.OrdinalIgnoreCase));
        if (watcher is null || watcher.Rows.Count == 0) return false;
        var colIndex = -1;
        for (int i = 0; i < watcher.ColumnNames.Count; i++)
        {
            if (string.Equals(watcher.ColumnNames[i], column, StringComparison.OrdinalIgnoreCase))
            {
                colIndex = i;
                break;
            }
        }
        if (colIndex < 0) return false;
        value = watcher.Rows[^1][colIndex];
        format = column is "coverage" ? "P1" : "G4";
        return true;
    }

    private void RebuildMetricCatalog()
    {
        MetricCatalog.Clear();
        MetricCatalog.Add(new MetricCatalogEntry("inherent.tick", "Tick", "F0"));
        MetricCatalog.Add(new MetricCatalogEntry("inherent.agents", "Agents", "F0"));
        MetricCatalog.Add(new MetricCatalogEntry("inherent.active_incidents", "Active incidents", "F0"));
        MetricCatalog.Add(new MetricCatalogEntry("inherent.total_incidents", "Total incidents", "F0"));
        MetricCatalog.Add(new MetricCatalogEntry("inherent.messages", "Messages (live echoes)", "F0"));
        MetricCatalog.Add(new MetricCatalogEntry("inherent.avg_reward", "Avg reward", "F3"));

        foreach (var w in _runner.Watchers)
        {
            foreach (var col in w.ColumnNames)
            {
                if (col is "tick" or "trial") continue;
                MetricCatalog.Add(new MetricCatalogEntry($"{w.Name}.{col}", $"{w.Name}.{col}"));
            }
        }

        SelectedCatalogEntry = MetricCatalog.FirstOrDefault(e => e.Key == "inherent.agents")
            ?? MetricCatalog.FirstOrDefault();
    }

    private void SeedDefaultMetrics()
    {
        foreach (var key in new[]
                 {
                     "inherent.agents",
                     "inherent.active_incidents",
                     "Coverage.coverage",
                     "Coverage.messages",
                     "Connectivity.avg_degree"
                 })
        {
            var entry = MetricCatalog.FirstOrDefault(e => e.Key == key);
            if (entry is not null)
                MetricSeries.Add(new LiveMetricSeries(entry));
        }
    }

    private void SeedDefaultDirective()
    {
        Directives.Add(new EnvironmentDirective
        {
            AtTick = 0,
            DeployerName = "Gaussian",
            AgentCount = 10,
            AgentTypeName = "OrbitingCoop",
            AdversaryCount = 0,
            IncidentCount = 5
        });
        SelectedDirective = Directives[0];
        DraftDeployerName = "Gaussian";
        DraftAgentTypeName = "OrbitingCoop";
    }

    private void RegisterFactories()
    {
        _factories.Register(new RandomWalkAgentFactory());
        _factories.Register(new GreedyIncidentAgentFactory());
        _factories.Register(new StigmergyAgentFactory());
        _factories.Register(new CooperativeSearchAgentFactory());
        _factories.Register(new DeceptiveStigmergyAgentFactory());
        _factories.Register(new TrustAwareSearchAgentFactory());
        _factories.Register(new OrbitingCoopAgentFactory());
        _factories.Register(new HerdingDeceptiveAgentFactory());
        _factories.Register(new MitigatedOrbitAgentFactory());
    }

    private void RegisterDeployers()
    {
        void Add(IDeployer d) => _deployers[d.Name] = d;
        Add(new RandomDeployer());
        Add(new GaussianDeployer());
    }
}
