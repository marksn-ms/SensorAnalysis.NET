using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Agents.Watchers;
using SensorAnalysis.App.Controls;
using SensorAnalysis.App.Models;
using SensorAnalysis.App.Services;
using SensorAnalysis.App.Views;
using SensorAnalysis.App.Visualization;
using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.App.ViewModels;

/// <summary>
/// View model for the main application window. Encapsulates world creation, directive-driven
/// deployment, stepping, live metrics, video recording, and experiment exporting.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Core.World.SimulationRunner _runner = new();
    private readonly AgentFactoryRegistry _factories;
    private readonly Dictionary<string, Func<IDeployer>> _deployerFactories;
    private readonly DispatcherTimer _runTimer;
    private readonly CoverageWatcher _coverageWatcher = new(sampleEveryTicks: 1);
    private readonly ConnectivityWatcher _connectivityWatcher = new(sampleEveryTicks: 1);
    private readonly IncidentClearanceWatcher _clearanceWatcher = new(sampleEveryTicks: 1);
    private readonly TrustTelemetryWatcher _trustTelWatcher = new(sampleEveryTicks: 5);
    private readonly CanvasVideoRecorder _videoRecorder = new();
    private RadioTrafficLog? _boundRadio;

    private WorldOptions _lastWorldOptions = new()
    {
        Title = "New World",
        TileCount = new GridSize(12, 12),
        TileSizePx = new GridSize(40, 40)
    };
    private int _lastSeed = 42;

    /// <summary>Set by the view so we can capture frames of the live canvas.</summary>
    public WorldCanvas? Canvas { get; set; }

    /// <summary>Trials/ticks from the last loaded recipe; used by Run Experiment when set.</summary>
    private int? _recipeTrials;
    private int? _recipeTicks;

    /// <summary>Non-dialog AVI path when set by --record-video PATH.</summary>
    private string? _autoVideoPath;

    /// <summary>Shut down the process when Stop completes (unattended capture).</summary>
    private bool _exitOnStop;

    private GuiStartupOptions _startup = new();

    /// <summary>Creates a new instance of <see cref="MainViewModel"/> and initializes application state.</summary>
    public MainViewModel()
    {
        _factories = BuiltinCatalog.CreateAgents();
        _deployerFactories = BuiltinCatalog.CreateDeployers();

        AgentTypeNames = new ObservableCollection<string>(_factories.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        IncidentTypeNames = new ObservableCollection<string>(new[] { "Incident", "Hotspot", "Distress" });
        DeployerNames = new ObservableCollection<string>(_deployerFactories.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        _runner.AddWatcher(_coverageWatcher);
        _runner.AddWatcher(_connectivityWatcher);
        _runner.AddWatcher(_clearanceWatcher);
        _runner.AddWatcher(_trustTelWatcher);
        _runner.TickCompleted += OnTickCompleted;

        RebuildMetricCatalog();
        SeedDefaultMetrics();
        DraftDeployerName = "Gaussian";
        RebuildDraftFields();
        SeedDefaultDirectives();

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _runTimer.Tick += (_, _) => AdvanceOneTick();

        NewWorld();
        _startup = GuiStartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    [ObservableProperty] private SimWorld? _world;
    [ObservableProperty] private WorldObject? _selectedObject;
    [ObservableProperty] private Tile? _selectedTile;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _tickText = "Tick 0";
    [ObservableProperty] private string _selectionSummary = "Nothing selected";
    [ObservableProperty] private bool _recordFrames;
    [ObservableProperty] private bool _recordVideo;
    [ObservableProperty] private int _frameEveryNTicks = 10;
    [ObservableProperty] private int _tickDelayMs = 50;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private EnvironmentDirective? _selectedDirective;
    [ObservableProperty] private MetricCatalogEntry? _selectedCatalogEntry;

    /// <summary>Draft: at this tick, run the selected deployer.</summary>
    [ObservableProperty] private long _draftAtTick;
    [ObservableProperty] private string _draftDeployerName = "Gaussian";
    [ObservableProperty] private string _draftValidationMessage = "";

    /// <summary>Available agent factory names.</summary>
    public ObservableCollection<string> AgentTypeNames { get; }

    /// <summary>Known incident type names for directive combos.</summary>
    public ObservableCollection<string> IncidentTypeNames { get; }

    /// <summary>Available deployer names.</summary>
    public ObservableCollection<string> DeployerNames { get; }

    /// <summary>Property-driven draft fields for the selected deployer.</summary>
    public ObservableCollection<DirectiveParameterField> DraftFields { get; } = new();

    /// <summary>Scheduled / applied environment directives.</summary>
    public ObservableCollection<EnvironmentDirective> Directives { get; } = new();

    /// <summary>Pinned live metric series.</summary>
    public ObservableCollection<LiveMetricSeries> MetricSeries { get; } = new();

    /// <summary>Catalog of inherent + watcher metrics available to pin.</summary>
    public ObservableCollection<MetricCatalogEntry> MetricCatalog { get; } = new();

    /// <summary>Legend of agent/incident types currently present in the world.</summary>
    public ObservableCollection<LegendEntry> LegendEntries { get; } = new();

    /// <summary>Narrated radio traffic (color-coded by sender type).</summary>
    public ObservableCollection<RadioLogLine> RadioLog { get; } = new();

    private long _autoStopAt = long.MaxValue;
    private string? _frameDirectory;

    partial void OnTickDelayMsChanged(int value)
    {
        _runTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, value));
    }

    partial void OnSelectedObjectChanged(WorldObject? value)
    {
        if (value is not null)
            RefreshSelectionSummary();
        else if (SelectedTile is null)
            SelectionSummary = "Nothing selected";
    }

    partial void OnSelectedTileChanged(Tile? value)
    {
        if (value is not null)
            RefreshSelectionSummary();
        else if (SelectedObject is null)
            SelectionSummary = "Nothing selected";
    }

    /// <summary>Create a new simulation world using the last-used options and seed.</summary>
    [RelayCommand]
    private void NewWorld()
    {
        ResetWorld(_lastWorldOptions, _lastSeed);
        StatusText = "World created. Tick-0 directives apply on Step/Run.";
    }

    /// <summary>Opens the world-options dialog and creates a world with the chosen settings.</summary>
    [RelayCommand]
    private void NewWorldWithOptions()
    {
        var dlgVm = new WorldOptionsDialogViewModel();
        dlgVm.Prefill(_lastWorldOptions, _lastSeed);
        var dlg = new WorldOptionsDialog
        {
            Owner = Application.Current.MainWindow,
            ViewModel = dlgVm
        };
        if (dlg.ShowDialog() != true) return;

        _lastWorldOptions = dlgVm.ToWorldOptions();
        _lastSeed = dlgVm.Seed;
        ResetWorld(_lastWorldOptions, _lastSeed);
        StatusText = $"World created ({_lastWorldOptions.TileCount.Width}×{_lastWorldOptions.TileCount.Height}, seed {_lastSeed}).";
    }

    private void ResetWorld(WorldOptions options, int seed)
    {
        Stop(finalizeVideo: false);
        _videoRecorder.Clear();

        _runner.Reset(CloneOptions(options), seed);
        SelectedObject = null;
        SelectedTile = null;

        foreach (var d in Directives)
        {
            d.IsApplied = false;
            d.Status = "";
        }
        foreach (var s in MetricSeries)
            s.Reset();

        BindRadioLog(_runner.World);
        RefreshWorldBinding();
        UpdateTickText();
        RefreshMetrics();
        RefreshLegend();
        RefreshSelectionSummary();
    }

    private static WorldOptions CloneOptions(WorldOptions o) => new()
    {
        Title = o.Title,
        TileCount = o.TileCount,
        TileSizePx = o.TileSizePx,
        TileShape = o.TileShape,
        ExactSensing = o.ExactSensing,
        Sensing = o.Sensing.Clone()
    };

    /// <summary>Clear the radio traffic panel.</summary>
    [RelayCommand]
    private void ClearRadioLog()
    {
        RadioLog.Clear();
        _runner.World?.Radio.Clear();
    }

    partial void OnDraftDeployerNameChanged(string value)
    {
        RebuildDraftFields();
        AddDirectiveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Append the draft as a new directive once all required fields are valid.</summary>
    [RelayCommand(CanExecute = nameof(CanAddDirective))]
    private void AddDirective()
    {
        if (!TryBuildDraftOptions(out var options, out var error))
        {
            DraftValidationMessage = error ?? "Invalid parameters.";
            return;
        }

        var d = new EnvironmentDirective
        {
            AtTick = DraftAtTick,
            DeployerName = DraftDeployerName,
            Parameters = DeployerParameterCatalog.Capture(options)
        };
        Directives.Add(d);
        SelectedDirective = d;
        DraftValidationMessage = "";
        StatusText = $"Added: {d.Summary}";
    }

    private bool CanAddDirective() =>
        _deployerFactories.ContainsKey(DraftDeployerName) &&
        DraftFields.All(f => f.IsValid) &&
        TryBuildDraftOptions(out var options, out _) &&
        DeployerParameterCatalog.TryValidate(options, out _);

    /// <summary>Remove the selected directive from the log.</summary>
    [RelayCommand]
    private void RemoveDirective()
    {
        if (SelectedDirective is null) return;
        Directives.Remove(SelectedDirective);
        SelectedDirective = null;
    }

    /// <summary>
    /// Stamp an unapplied selected directive (or a new draft) at the current tick and apply due items.
    /// Prefer scheduling via Add + Step/Run; this is for mid-run injection while paused.
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

        if (SelectedDirective is { IsApplied: false } sel)
        {
            sel.AtTick = _runner.World.CurrentTick;
        }
        else if (CanAddDirective())
        {
            DraftAtTick = _runner.World.CurrentTick;
            AddDirective();
        }
        else
        {
            StatusText = "Select an unapplied directive or complete the draft, then Apply now.";
            return;
        }

        ApplyDueDirectives();
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
        RefreshSelectionSummary();
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
            _frameDirectory = _startup.FramesDirectory
                ?? Path.Combine(Environment.CurrentDirectory, "experiments",
                    $"frames-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(_frameDirectory);
            StatusText = $"Running (recording frames → {_frameDirectory})…";
        }
        else
        {
            _frameDirectory = null;
            StatusText = "Running…";
        }

        if (RecordVideo)
        {
            _videoRecorder.Begin();
            StatusText = RecordFrames
                ? $"Running (PNG + video)…"
                : "Running (recording video)…";
        }

        _autoStopAt = _startup.AutoStopTicks is int stopAt ? stopAt : long.MaxValue;
        IsRunning = true;
        _runTimer.Start();
    }

    /// <summary>Stop the automatic runner timer and finalize video if recording.</summary>
    [RelayCommand]
    private void Stop() => Stop(finalizeVideo: true);

    private void Stop(bool finalizeVideo)
    {
        _runTimer.Stop();
        IsRunning = false;

        if (finalizeVideo && RecordVideo && _videoRecorder.IsRecording)
            FinalizeVideoRecording();
        else if (!finalizeVideo)
            _videoRecorder.Clear();
        else if (_frameDirectory is not null)
            StatusText = $"Stopped. Frames in {_frameDirectory}";
        else
            StatusText = "Stopped.";

        if (_exitOnStop && finalizeVideo)
            Application.Current.Shutdown();
    }

    private void FinalizeVideoRecording()
    {
        if (_videoRecorder.FrameCount == 0)
        {
            _videoRecorder.Clear();
            StatusText = "Stopped (no video frames captured).";
            return;
        }

        string? path = _autoVideoPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "AVI Video|*.avi",
                FileName = $"sim-{DateTime.Now:yyyyMMdd-HHmmss}.avi"
            };
            if (dlg.ShowDialog() != true)
            {
                _videoRecorder.Clear();
                return;
            }

            path = dlg.FileName;
        }
        else
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var fps = Math.Clamp(1000.0 / Math.Max(1, TickDelayMs), 1, 60);
        try
        {
            if (_videoRecorder.SaveAvi(path, fps))
                StatusText = $"Stopped. Video saved: {path} ({fps:0.#} fps)";
            else
                StatusText = "Stopped (no video frames).";
        }
        catch (Exception ex)
        {
            _videoRecorder.Clear();
            if (_autoVideoPath is null)
                MessageBox.Show($"Failed to write AVI:\n{ex.Message}", "Record video");
            StatusText = $"Stopped (video save failed): {ex.Message}";
        }
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

        Stop(finalizeVideo: false);
        _videoRecorder.Clear();
        var doc = ScenarioSerializer.Load(dlg.FileName);
        _lastWorldOptions = doc.ToWorldOptions();
        _lastSeed = doc.Seed;
        _runner.Reset(_lastWorldOptions, _lastSeed);
        var world = _runner.World!;
        SelectedObject = null;
        SelectedTile = null;

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
        RefreshSelectionSummary();
    }

    /// <summary>
    /// Apply CLI startup options: optional recipe load, recording flags, then optional auto-Run.
    /// Call after the canvas is bound so the first captured frames are valid.
    /// </summary>
    public void TryLoadStartupRecipe()
    {
        _startup = GuiStartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());

        if (_startup.TickDelayMs is int delay)
            TickDelayMs = Math.Max(1, delay);
        if (_startup.FrameEveryNTicks is int every)
            FrameEveryNTicks = Math.Max(1, every);
        if (_startup.RecordVideo)
            RecordVideo = true;
        if (_startup.RecordFrames)
            RecordFrames = true;
        _autoVideoPath = _startup.VideoPath;
        _exitOnStop = _startup.ExitOnStop;

        if (_startup.RecipePath is string json)
        {
            try
            {
                ApplyRecipe(RecipeSerializer.Load(json), json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load recipe:\n{json}\n\n{ex.Message}", "Open Recipe");
                if (_exitOnStop)
                    Application.Current.Shutdown();
                return;
            }
        }

        if (!_startup.AutoRun)
            return;

        // Defer one dispatcher pump so layout/canvas render size is ready.
        Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                if (_startup.AutoStopTicks is int ticks && _recipeTicks is null)
                    _recipeTicks = ticks;
                Run();
            });
    }

    /// <summary>Open a recipe JSON (world + directive log). Does not replace form-based editing.</summary>
    [RelayCommand]
    private void LoadRecipe()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Recipe JSON|*.json",
            FileName = ExperimentRecipe.DefaultFileName
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ApplyRecipe(RecipeSerializer.Load(dlg.FileName), dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open Recipe");
        }
    }

    /// <summary>Save the current world options and directive log as a CLI/GUI recipe JSON.</summary>
    [RelayCommand]
    private void SaveRecipe()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Recipe JSON|*.json",
            FileName = ExperimentRecipe.DefaultFileName
        };
        if (dlg.ShowDialog() != true) return;
        RecipeSerializer.Save(dlg.FileName, BuildRecipeFromUi());
        StatusText = $"Recipe saved: {dlg.FileName}";
    }

    private void ApplyRecipe(ExperimentRecipe recipe, string path)
    {
        Stop(finalizeVideo: false);
        _videoRecorder.Clear();
        _lastWorldOptions = recipe.ToWorldOptions();
        _lastSeed = recipe.Seed;
        _recipeTrials = recipe.Trials;
        _recipeTicks = recipe.Ticks;

        Directives.Clear();
        foreach (var d in recipe.Directives)
        {
            Directives.Add(new EnvironmentDirective
            {
                AtTick = d.AtTick,
                DeployerName = d.Deployer,
                Salt = d.Salt,
                Parameters = new Dictionary<string, object?>(d.Parameters, StringComparer.Ordinal)
            });
        }

        SelectedDirective = Directives.FirstOrDefault();
        ResetWorld(_lastWorldOptions, _lastSeed);
        StatusText = $"Loaded recipe: {path} ({Directives.Count} directive(s)). Edit with the form or Step/Run.";
    }

    private ExperimentRecipe BuildRecipeFromUi() => new()
    {
        ExperimentName = string.IsNullOrWhiteSpace(_lastWorldOptions.Title) ? "gui" : _lastWorldOptions.Title,
        Trials = _recipeTrials ?? 5,
        Ticks = _recipeTicks ?? 200,
        Seed = _lastSeed,
        World = RecipeWorld.FromWorldOptions(_lastWorldOptions),
        Directives = Directives.Select(d => new RecipeDirective
        {
            AtTick = d.AtTick,
            Deployer = d.DeployerName,
            Salt = d.Salt,
            Parameters = d.Parameters
        }).ToList()
    };

    /// <summary>
    /// Run a small batch experiment from the directive log,
    /// collect metrics and export results to the experiments folder.
    /// </summary>
    [RelayCommand]
    private void RunExperiment()
    {
        Stop();
        if (_runner.World is null) return;

        var trials = Math.Max(1, _recipeTrials ?? 5);
        var ticks = Math.Max(1, _recipeTicks ?? 200);
        var result = MessageBox.Show(
            $"Run {trials} trials × {ticks} ticks and export metrics to ./experiments/ ?",
            "Run Experiment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        if (Directives.Count == 0)
        {
            MessageBox.Show("Add at least one environment directive first.", "Run Experiment");
            return;
        }

        var coverage = new CoverageWatcher(1);
        var connectivity = new ConnectivityWatcher(1);
        var clearance = new IncidentClearanceWatcher(1);
        var trustTel = new TrustTelemetryWatcher(5);
        var runner = new Core.World.SimulationRunner();
        runner.AddWatcher(coverage);
        runner.AddWatcher(connectivity);
        runner.AddWatcher(clearance);
        runner.AddWatcher(trustTel);

        var steps = Directives.Select(ToDeployStep).ToList();
        var manifest = runner.RunExperiment(new Core.World.ExperimentRequest
        {
            ExperimentName = "gui-batch",
            BaseSeed = _lastSeed,
            Trials = trials,
            TicksPerTrial = ticks,
            WorldOptions = CloneOptions(_lastWorldOptions),
            Steps = steps,
            Context = CreateDeployContext(0),
            Notes = "Launched from GUI"
        });

        var dir = Path.Combine(Environment.CurrentDirectory, "experiments",
            $"{manifest.ExperimentName}-{DateTime.Now:yyyyMMdd-HHmmss}");
        ExperimentExporter.Export(dir, manifest, new IWatcher[] { coverage, connectivity, clearance, trustTel });
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
            if (!_deployerFactories.ContainsKey(d.DeployerName))
                throw new InvalidOperationException($"Unknown deployer '{d.DeployerName}'.");

            var salt = d.Salt;
            var step = ToDeployStep(d);
            _runner.AttachDeployer(step, CreateDeployContext(salt));
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

    private DeployStep ToDeployStep(EnvironmentDirective d)
    {
        if (!_deployerFactories.TryGetValue(d.DeployerName, out var factory))
            throw new InvalidOperationException($"Unknown deployer '{d.DeployerName}'.");
        var prototype = factory();
        var options = DeployerParameterCatalog.Materialize(prototype.OptionsType, d.Parameters);
        return new DeployStep
        {
            CreateDeployer = factory,
            Options = options,
            AtTick = d.AtTick,
            RandomSalt = d.Salt
        };
    }

    private DeployContext CreateDeployContext(int salt) => new()
    {
        RandomSalt = salt,
        ResolveAgent = name => _factories.Get(name).Create()
    };

    private void RebuildDraftFields()
    {
        foreach (var f in DraftFields)
            f.PropertyChanged -= OnDraftFieldPropertyChanged;
        DraftFields.Clear();

        if (!_deployerFactories.TryGetValue(DraftDeployerName, out var factory))
            return;

        var options = factory().CreateOptions();
        foreach (var info in DeployerParameterCatalog.Describe(options.GetType()))
        {
            var field = new DirectiveParameterField(info, info.Property.GetValue(options), AgentTypeNames, IncidentTypeNames);
            field.PropertyChanged += OnDraftFieldPropertyChanged;
            DraftFields.Add(field);
        }

        RefreshDraftValidation();
    }

    private void OnDraftFieldPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DirectiveParameterField.IsValid)
            or nameof(DirectiveParameterField.TextValue)
            or nameof(DirectiveParameterField.BoolValue)
            or nameof(DirectiveParameterField.ChoiceValue))
        {
            RefreshDraftValidation();
            AddDirectiveCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshDraftValidation()
    {
        if (!TryBuildDraftOptions(out var options, out var error))
        {
            DraftValidationMessage = error ?? "";
            return;
        }

        DraftValidationMessage = DeployerParameterCatalog.TryValidate(options, out var err)
            ? ""
            : err ?? "";
    }

    private bool TryBuildDraftOptions(out object options, out string? error)
    {
        options = null!;
        error = null;
        if (!_deployerFactories.TryGetValue(DraftDeployerName, out var factory))
        {
            error = $"Unknown deployer '{DraftDeployerName}'.";
            return false;
        }

        try
        {
            foreach (var f in DraftFields)
            {
                if (!f.IsValid)
                {
                    error = $"Invalid value for {f.Label}.";
                    return false;
                }
            }

            var map = DraftFields.ToDictionary(f => f.Name, f => f.GetBoxedValue(), StringComparer.Ordinal);
            options = DeployerParameterCatalog.Materialize(factory().OptionsType, map);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void OnTickCompleted()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateTickText();
            RefreshMetrics();
            RefreshLegend();
            RefreshWorldBinding();
            if (SelectedTile is not null)
                RefreshSelectionSummary();

            var every = Math.Max(1, FrameEveryNTicks);
            if (World is not null && World.CurrentTick % every == 0)
            {
                if (RecordFrames && Canvas is not null && _frameDirectory is not null)
                    FrameCapture.CaptureTick(Canvas, _frameDirectory, World.CurrentTick);

                if (RecordVideo && _videoRecorder.IsRecording && Canvas is not null)
                    _videoRecorder.CaptureFrame(Canvas);
            }
        });
    }

    private void RefreshWorldBinding()
    {
        var w = _runner.World;
        BindRadioLog(w);
        World = null;
        World = w;
        Canvas?.Refresh();
    }

    private void BindRadioLog(SimWorld? world)
    {
        if (_boundRadio is not null)
            _boundRadio.EntryAdded -= OnRadioEntryAdded;
        _boundRadio = world?.Radio;
        RadioLog.Clear();
        if (_boundRadio is null) return;

        foreach (var e in _boundRadio.Entries)
            RadioLog.Add(new RadioLogLine(e));
        _boundRadio.EntryAdded += OnRadioEntryAdded;
    }

    private void OnRadioEntryAdded(RadioLogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            AppendRadioLine(entry);
        else
            dispatcher.BeginInvoke(() => AppendRadioLine(entry));
    }

    private void AppendRadioLine(RadioLogEntry entry)
    {
        RadioLog.Add(new RadioLogLine(entry));
        while (RadioLog.Count > 500)
            RadioLog.RemoveAt(0);
    }

    private void UpdateTickText() =>
        TickText = $"Tick {World?.CurrentTick ?? 0}  |  Agents {World?.Agents.Count ?? 0}  |  Incidents {World?.Incidents.Count(i => !i.IsDepleted) ?? 0}";

    private void RefreshSelectionSummary()
    {
        if (SelectedObject is not null)
        {
            SelectionSummary = SelectedObject switch
            {
                Agent a => $"Agent #{a.Id} ({a.AgentType})\nPos: ({a.Position.X:F1}, {a.Position.Y:F1})\nRange: {a.SensorRange}\nReward: {a.TotalReward:F3}",
                Incident i => $"Incident #{i.Id} ({i.IncidentType})\nPos: ({i.Position.X:F1}, {i.Position.Y:F1})\nIntensity: {i.Intensity:F3}",
                Message m => $"Message #{m.Id}\nFrom: {m.FromAgentType} (#{m.FromAgentId})\nPos: ({m.Position.X:F1}, {m.Position.Y:F1})\nTTL: {m.Ttl}\n{m.Payload}",
                _ => "Nothing selected"
            };
            return;
        }

        if (SelectedTile is not null && World is not null)
        {
            var tile = SelectedTile;
            // Re-resolve from current world grid in case world was replaced.
            if (tile.Col < World.Options.TileCount.Width && tile.Row < World.Options.TileCount.Height)
                tile = World.Tiles[tile.Col, tile.Row];

            var tw = World.Options.TileSizePx.Width;
            var th = World.Options.TileSizePx.Height;
            var objects = World.ObjectsInTile(tile).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Tile ({tile.Col}, {tile.Row})");
            sb.AppendLine($"Size: {tw}×{th} px");
            sb.AppendLine($"Center: ({tile.Center.X:F1}, {tile.Center.Y:F1})");
            sb.AppendLine(tile.LastCoveredTick < 0
                ? "Last covered: never"
                : $"Last covered: tick {tile.LastCoveredTick}");
            sb.AppendLine($"Objects ({objects.Count}):");
            if (objects.Count == 0)
                sb.Append("  (empty)");
            else
            {
                foreach (var o in objects)
                {
                    sb.AppendLine(o switch
                    {
                        Agent a => $"  Agent #{a.Id} {a.AgentType}",
                        Incident i => $"  Incident #{i.Id} {i.IncidentType}",
                        Message m => $"  Message #{m.Id} {m.FromAgentType}",
                        _ => $"  {o.GetType().Name} #{o.Id}"
                    });
                }
            }
            SelectionSummary = sb.ToString().TrimEnd();
            return;
        }

        SelectionSummary = "Nothing selected";
    }

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

    private void SeedDefaultDirectives()
    {
        Directives.Add(new EnvironmentDirective
        {
            AtTick = 0,
            DeployerName = "Gaussian",
            Parameters = DeployerParameterCatalog.Capture(new GaussianDeployOptions
            {
                Count = 10,
                TypeName = "OrbitingCoop"
            })
        });
        Directives.Add(new EnvironmentDirective
        {
            AtTick = 0,
            DeployerName = "PeriodicIncidents",
            Parameters = DeployerParameterCatalog.Capture(new PeriodicIncidentOptions
            {
                PeriodTicks = 25,
                Count = 1
            })
        });
        SelectedDirective = Directives[0];
    }
}
