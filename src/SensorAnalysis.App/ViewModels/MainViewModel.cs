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
using SensorAnalysis.App.Services;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.ViewModels;

/// <summary>
/// View model for the main application window. Encapsulates world creation, deployment,
/// stepping and experiment exporting logic and exposes data used by the view.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Core.World.SimulationRunner _runner = new();
    private readonly AgentFactoryRegistry _factories = new();
    private readonly DispatcherTimer _runTimer;
    private CoverageWatcher _coverageWatcher = new(sampleEveryTicks: 1);
    private ConnectivityWatcher _connectivityWatcher = new(sampleEveryTicks: 1);

    /// <summary>Set by the view so we can capture frames of the live canvas.</summary>
    public WorldCanvas? Canvas { get; set; }

    /// <summary>Creates a new instance of <see cref="MainViewModel"/> and initializes application state.</summary>
    public MainViewModel()
    {
        _factories.Register(new RandomWalkAgentFactory());
        _factories.Register(new GreedyIncidentAgentFactory());
        _factories.Register(new StigmergyAgentFactory());
        AgentTypeNames = new ObservableCollection<string>(_factories.Names);
        SelectedAgentType = "GreedyIncident";

        _runner.AddWatcher(_coverageWatcher);
        _runner.AddWatcher(_connectivityWatcher);
        _runner.TickCompleted += OnTickCompleted;

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _runTimer.Tick += (_, _) =>
        {
            _runner.Step();
            if (_runner.World is { CurrentTick: var t } && t >= _autoStopAt)
                Stop();
        };

        NewWorld();
    }

    [ObservableProperty] private SimWorld? _world;
    [ObservableProperty] private WorldObject? _selectedObject;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _tickText = "Tick 0";
    [ObservableProperty] private string _selectionSummary = "Nothing selected";
    [ObservableProperty] private string _liveMetricsText = "";
    [ObservableProperty] private int _agentCount = 10;
    [ObservableProperty] private int _incidentCount = 5;
    [ObservableProperty] private string _selectedAgentType = "GreedyIncident";
    [ObservableProperty] private bool _recordFrames;
    [ObservableProperty] private int _frameEveryNTicks = 10;

    /// <summary>Collection of available agent type names for the UI picker.</summary>
    public ObservableCollection<string> AgentTypeNames { get; }

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
        World = _runner.World;
        StatusText = "World created. Deploy agents to begin.";
        UpdateTickText();
        RefreshMetrics();
    }

    /// <summary>Deploy agents and incidents into the currently loaded world using the selected factory.</summary>
    [RelayCommand]
    private void Deploy()
    {
        if (_runner.World is null) return;
        var deployer = new RandomDeployer();
        var factory = _factories.Get(SelectedAgentType);
        _runner.Deploy(deployer, new DeployRequest
        {
            AgentCount = AgentCount,
            IncidentCount = IncidentCount,
            AgentTypeName = SelectedAgentType,
            SensorRange = 80,
            AgentFactory = _ => factory.Create()
        });
        World = _runner.World;
        StatusText = $"Deployed {AgentCount} × {SelectedAgentType}, {IncidentCount} incidents.";
        RefreshMetrics();
    }

    /// <summary>Perform a single interactive simulation step.</summary>
    [RelayCommand]
    private void Step() => _runner.Step();

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
        _runTimer.Start();
    }

    /// <summary>Stop the automatic runner timer.</summary>
    [RelayCommand]
    private void Stop()
    {
        _runTimer.Stop();
        StatusText = _frameDirectory is not null
            ? $"Stopped. Frames in {_frameDirectory}"
            : "Stopped.";
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
                Label = i.Label
            });
        }

        World = world;
        StatusText = $"Loaded scenario: {doc.Title} ({doc.Agents.Count} agents, {doc.Incidents.Count} incidents)";
        UpdateTickText();
        RefreshMetrics();
    }

    /// <summary>
    /// Run a small batch experiment (multiple trials) using the current UI selections,
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

        var coverage = new CoverageWatcher(1);
        var connectivity = new ConnectivityWatcher(1);
        var runner = new Core.World.SimulationRunner();
        runner.AddWatcher(coverage);
        runner.AddWatcher(connectivity);

        var factory = _factories.Get(SelectedAgentType);
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
            Deployer = new RandomDeployer(),
            DeployRequest = new DeployRequest
            {
                AgentCount = AgentCount,
                IncidentCount = IncidentCount,
                AgentTypeName = SelectedAgentType,
                SensorRange = 80,
                AgentFactory = _ => factory.Create()
            },
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

    private void OnTickCompleted()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateTickText();
            RefreshMetrics();
            var w = World;
            World = null;
            World = w;

            if (RecordFrames && Canvas is not null && _frameDirectory is not null && World is not null)
            {
                if (World.CurrentTick % Math.Max(1, FrameEveryNTicks) == 0)
                    FrameCapture.CaptureTick(Canvas, _frameDirectory, World.CurrentTick);
            }
        });
    }

    private void UpdateTickText() =>
        TickText = $"Tick {World?.CurrentTick ?? 0}  |  Agents {World?.Agents.Count ?? 0}  |  Incidents {World?.Incidents.Count(i => !i.IsDepleted) ?? 0}";

    private void RefreshMetrics()
    {
        if (_coverageWatcher.Rows.Count == 0)
        {
            LiveMetricsText = "(no samples yet)";
            return;
        }
        var last = _coverageWatcher.Rows[^1];
        LiveMetricsText =
            $"coverage:     {last[2]:P1}\n" +
            $"incidents:    {last[3]:F0}\n" +
            $"avg reward:   {last[5]:F3}\n" +
            $"messages:     {last[6]:F0}";
    }
}
