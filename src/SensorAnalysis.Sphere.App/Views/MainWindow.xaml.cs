using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;
using SensorAnalysis.Sphere.Core.Geometry;
using SensorAnalysis.Sphere.Core.Serialization;
using SensorAnalysis.Sphere.Core.Simulation;

namespace SensorAnalysis.Sphere.App.Views;

/// <summary>App-style shell for the geodesic spherical simulation platform.</summary>
public partial class MainWindow : Window
{
    private const double MinimumMarkerRadius = 0.022;
    private const int HeatmapWidth = 256;
    private const int HeatmapHeight = 128;

    private readonly PerspectiveCamera _camera = new() { FieldOfView = 48 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly Dictionary<GeometryModel3D, object> _modelEntities = [];
    private readonly List<(GeometryModel3D Model, RotateTransform3D Rotation, TranslateTransform3D Translation)> _agentTransforms = [];
    private readonly List<(GeometryModel3D Model, RotateTransform3D Rotation, TranslateTransform3D Translation)> _incidentTransforms = [];
    private readonly WriteableBitmap _heatmap = new(HeatmapWidth, HeatmapHeight, 96, 96, PixelFormats.Bgra32, null);
    private GeometryModel3D? _surfaceModel;

    private double _yaw = -38;
    private double _pitch = -22;
    private double _distance = 4.1;
    private Point? _dragStart;
    private double _dragYaw;
    private double _dragPitch;
    private bool _didDrag;
    private SphericalWorld? _world;
    private SphericalWorldOptions _options = new()
    {
        Radius = 1_000,
        Resolution = 4,
        SensorRange = 100,
        CommunicationRange = 200,
        StepSize = 1
    };
    private int _seed = 42;
    private string _worldTitle = "Spherical world";
    private List<SphericalDirective>? _scenarioDirectives;
    private Model3DGroup? _scene;
    private object? _selected;
    private readonly List<string> _metricHistory = [];

    /// <summary>Initializes the spherical world scene.</summary>
    public MainWindow()
    {
        InitializeComponent();
        WorldViewport.Camera = _camera;
        _timer.Tick += (_, _) => AdvanceSimulation();
        RenderWorld();
    }

    private void AdvanceSimulation()
    {
        _world?.Tick();
        SyncAgentModels();
        SyncIncidentModels();
        UpdateAgentPositions();
        UpdateIncidentPositions();
        UpdateHeatmap();
        UpdatePanels();
    }

    private void RenderWorld()
    {
        _timer.IsEnabled = false;
        RunButton.Content = "Run";
        _selected = null;
        _metricHistory.Clear();
        _modelEntities.Clear();

        _world = new SphericalWorld(_options, _seed);
        foreach (var directive in _scenarioDirectives ?? CreateDefaultDirectives(_world))
            _world.AddDirective(directive);

        _scene = new Model3DGroup();
        _scene.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 80)));
        _scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-2, -3, -4)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(100, 130, 180), new Vector3D(3, 2, -1)));
        _surfaceModel = CreateSurfaceModel();
        _scene.Children.Add(_surfaceModel);
        foreach (var axis in CreateReferenceAxes()) _scene.Children.Add(axis);

        _agentTransforms.Clear();
        _incidentTransforms.Clear();
        UpdateHeatmap();

        WorldViewport.Children.Clear();
        WorldViewport.Children.Add(new ModelVisual3D { Content = _scene });
        UpdatePanels();
        UpdateCamera();
        StatusText.Text = $"{_worldTitle} · resolution n={_options.Resolution} · {_world.Mesh.Faces.Count:N0} faces";
    }

    private static IReadOnlyList<SphericalDirective> CreateDefaultDirectives(SphericalWorld world) =>
    [
        new SphericalDirective(
            Tick: 0,
            Kind: SphericalDirectiveKind.DeployUniformAgents,
            Count: 5,
            AgentKind: SphericalAgentKind.CooperativeSearch,
            SensorRange: world.Options.SensorRange,
            CommunicationRange: world.Options.CommunicationRange,
            StepSize: world.Options.StepSize),
        new SphericalDirective(
            Tick: 0,
            Kind: SphericalDirectiveKind.EnableCoverageWatcher),
        new SphericalDirective(
            Tick: 20,
            Kind: SphericalDirectiveKind.DeployPeriodicIncidents,
            Count: 1,
            PeriodTicks: 20,
            IncidentType: "StationaryIncident")
    ];

    private GeometryModel3D CreateSurfaceModel()
    {
        var geometry = new MeshGeometry3D();
        const int rings = 48;
        const int segments = 72;
        for (var ring = 0; ring <= rings; ring++)
        {
            var v = (double)ring / rings;
            var theta = Math.PI * v;
            for (var segment = 0; segment <= segments; segment++)
            {
                var u = (double)segment / segments;
                var phi = Math.Tau * u;
                geometry.Positions.Add(new Point3D(
                    Math.Sin(theta) * Math.Cos(phi),
                    Math.Cos(theta),
                    Math.Sin(theta) * Math.Sin(phi)));
                geometry.TextureCoordinates.Add(new System.Windows.Point(u, v));
            }
        }
        for (var ring = 0; ring < rings; ring++)
            for (var segment = 0; segment < segments; segment++)
            {
                var a = ring * (segments + 1) + segment;
                var b = a + segments + 1;
                geometry.TriangleIndices.Add(a);
                geometry.TriangleIndices.Add(b);
                geometry.TriangleIndices.Add(a + 1);
                geometry.TriangleIndices.Add(a + 1);
                geometry.TriangleIndices.Add(b);
                geometry.TriangleIndices.Add(b + 1);
            }

        var brush = new ImageBrush(_heatmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        var material = new DiffuseMaterial(brush);
        return new GeometryModel3D(geometry, material) { BackMaterial = material };
    }

    private void UpdateHeatmap()
    {
        if (_world is null) return;
        var pixels = new byte[HeatmapWidth * HeatmapHeight * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 77;
            pixels[i + 1] = 87;
            pixels[i + 2] = 60;
            pixels[i + 3] = 255;
        }

        foreach (var (faceId, lastTick) in _world.FaceLastCoveredTick)
        {
            var age = Math.Max(0, _world.CurrentTick - lastTick);
            var intensity = Math.Clamp(1.0 - age / 80.0, 0.08, 1.0);
            var center = _world.Mesh.Faces[faceId].Center;
            var lon = Math.Atan2(center.Z, center.X);
            var lat = Math.Asin(Math.Clamp(center.Y, -1, 1));
            var u = (lon + Math.PI) / Math.Tau;
            var v = 0.5 - lat / Math.PI;
            StampKernel(pixels, u, v, intensity, radius: 4);
        }

        _heatmap.WritePixels(new Int32Rect(0, 0, HeatmapWidth, HeatmapHeight), pixels, HeatmapWidth * 4, 0);
    }

    private static void StampKernel(byte[] pixels, double u, double v, double intensity, int radius)
    {
        var cx = (int)Math.Round(u * (HeatmapWidth - 1));
        var cy = (int)Math.Round(v * (HeatmapHeight - 1));
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var x = (cx + dx + HeatmapWidth) % HeatmapWidth;
            var y = Math.Clamp(cy + dy, 0, HeatmapHeight - 1);
            var weight = 1.0 - Math.Sqrt(dx * dx + dy * dy) / (radius + 0.01);
            if (weight <= 0) continue;
            var idx = (y * HeatmapWidth + x) * 4;
            var heat = Math.Clamp(intensity * weight, 0, 1);
            pixels[idx] = (byte)(60 * (1 - heat) + 40 * heat);
            pixels[idx + 1] = (byte)(87 * (1 - heat) + 180 * heat);
            pixels[idx + 2] = (byte)(77 * (1 - heat) + 255 * heat);
            pixels[idx + 3] = 255;
        }
    }

    private static IEnumerable<GeometryModel3D> CreateReferenceAxes()
    {
        yield return CreateAxis(new Vector3D(0, 1, 0), Color.FromRgb(232, 108, 108));
        yield return CreateAxis(new Vector3D(1, 0, 0), Color.FromRgb(120, 168, 240));
        yield return CreateAxis(new Vector3D(0, 0, 1), Color.FromRgb(132, 216, 148));
    }

    private static GeometryModel3D CreateAxis(Vector3D direction, Color color)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        return new GeometryModel3D(Cylinder(radius: 0.006, halfLength: 1.22, segments: 12), material)
        {
            BackMaterial = material,
            Transform = new RotateTransform3D(RotationFromZAxis(direction))
        };
    }

    private GeometryModel3D CreateAgentModel(double radius, SphericalAgentKind kind)
    {
        var color = kind switch
        {
            SphericalAgentKind.RandomWalk => Color.FromRgb(100, 190, 255),
            SphericalAgentKind.Stationary => Color.FromRgb(190, 150, 255),
            _ => Color.FromRgb(255, 220, 60)
        };
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        var rotation = new RotateTransform3D();
        var translation = new TranslateTransform3D();
        var transform = new Transform3DGroup();
        transform.Children.Add(rotation);
        transform.Children.Add(translation);
        var model = new GeometryModel3D(Disc(radius, 18), material) { BackMaterial = material, Transform = transform };
        _agentTransforms.Add((model, rotation, translation));
        return model;
    }

    private GeometryModel3D CreateIncidentModel(bool depleted)
    {
        var color = depleted ? Color.FromRgb(90, 90, 90) : Color.FromRgb(250, 105, 80);
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        var rotation = new RotateTransform3D();
        var translation = new TranslateTransform3D();
        var transform = new Transform3DGroup();
        transform.Children.Add(rotation);
        transform.Children.Add(translation);
        var model = new GeometryModel3D(Disc(0.026, 3), material) { BackMaterial = material, Transform = transform };
        _incidentTransforms.Add((model, rotation, translation));
        return model;
    }

    private void UpdateAgentPositions()
    {
        if (_world is null) return;
        for (var index = 0; index < _world.Agents.Count; index++)
        {
            var agent = _world.Agents[index];
            var transform = _agentTransforms[index];
            transform.Rotation.Rotation = RotationFromZAxis(new Vector3D(agent.Position.X, agent.Position.Y, agent.Position.Z));
            transform.Translation.OffsetX = agent.Position.X * 1.012;
            transform.Translation.OffsetY = agent.Position.Y * 1.012;
            transform.Translation.OffsetZ = agent.Position.Z * 1.012;
            _modelEntities[transform.Model] = agent;
        }
    }

    private void SyncAgentModels()
    {
        if (_world is null || _scene is null) return;
        var markerRadius = Math.Max(_world.Options.SensorRange / _world.Options.Radius / 100, MinimumMarkerRadius);
        while (_agentTransforms.Count < _world.Agents.Count)
            _scene.Children.Add(CreateAgentModel(markerRadius, _world.Agents[_agentTransforms.Count].Kind));
    }

    private void SyncIncidentModels()
    {
        if (_world is null || _scene is null) return;
        while (_incidentTransforms.Count < _world.Incidents.Count)
            _scene.Children.Add(CreateIncidentModel(_world.Incidents[_incidentTransforms.Count].IsDepleted));
    }

    private void UpdateIncidentPositions()
    {
        if (_world is null) return;
        for (var index = 0; index < _world.Incidents.Count; index++)
        {
            var incident = _world.Incidents[index];
            var entry = _incidentTransforms[index];
            entry.Rotation.Rotation = RotationFromZAxis(new Vector3D(incident.Position.X, incident.Position.Y, incident.Position.Z));
            entry.Translation.OffsetX = incident.Position.X * 1.014;
            entry.Translation.OffsetY = incident.Position.Y * 1.014;
            entry.Translation.OffsetZ = incident.Position.Z * 1.014;
            var color = incident.IsDepleted ? Color.FromRgb(90, 90, 90) : Color.FromRgb(250, 105, 80);
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            material.Freeze();
            entry.Model.Material = material;
            entry.Model.BackMaterial = material;
            _modelEntities[entry.Model] = incident;
        }
    }

    private void UpdatePanels()
    {
        if (_world is null) return;
        var metrics = _world.GetMetrics();
        TickText.Text = $"tick {metrics.Tick}";
        MetricsSummary.Text =
            $"coverage {metrics.Coverage:P1}\n" +
            $"agents {metrics.Agents}\n" +
            $"active incidents {metrics.ActiveIncidents}\n" +
            $"depleted {metrics.DepletedIncidents}\n" +
            $"last drain {metrics.LastDrain:F2}\n" +
            $"messages {metrics.Messages}\n" +
            $"R={_world.Options.Radius:N0}  r={_world.Options.SensorRange:N0}  c={_world.Options.CommunicationRange:N0}\n" +
            $"n={_world.Options.Resolution}  faces={_world.Mesh.Faces.Count:N0}  step={_world.Options.StepSize}\n" +
            $"ℓ≈{_world.CharacteristicEdgeLength:F1}  " +
            $"r hops {_world.RequiredHops(_world.Options.SensorRange)}  " +
            $"c hops {_world.RequiredHops(_world.Options.CommunicationRange)}";

        foreach (var sample in _world.CoverageSamples.TakeLast(1))
        {
            var row = $"t{sample.Tick,5}  cov {sample.Coverage,6:P1}  active {sample.ActiveIncidents,2}  drain {sample.LastDrain,4:F2}";
            if (_metricHistory.Count == 0 || _metricHistory[^1] != row)
            {
                _metricHistory.Add(row);
                if (_metricHistory.Count > 200) _metricHistory.RemoveAt(0);
            }
        }
        MetricHistoryLog.ItemsSource = null;
        MetricHistoryLog.ItemsSource = _metricHistory;

        SelectionSummary.Text = _selected switch
        {
            SphericalAgent agent =>
                $"Agent #{agent.Id}\nKind: {agent.Kind}\nr={agent.SensorRange:0.###}  c={agent.CommunicationRange:0.###}\n" +
                $"step={agent.StepSize:0.###}\nKnown incidents: {agent.KnownIncidentPositions.Count}\n" +
                $"pos=({agent.Position.X:F3}, {agent.Position.Y:F3}, {agent.Position.Z:F3})",
            SphericalIncident incident =>
                $"Incident #{incident.Id}\nType: {incident.IncidentType}\nIntensity: {incident.Intensity:F2}\n" +
                (incident.IsDepleted ? "Status: resolved" : "Status: active") +
                $"\npos=({incident.Position.X:F3}, {incident.Position.Y:F3}, {incident.Position.Z:F3})",
            _ => "Click an agent or incident marker to inspect it."
        };

        var filter = MessageFilterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _world.Messages
            : _world.Messages.Where(message => message.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
        var lines = filtered.TakeLast(100).Select(message => message.ToString()).ToArray();
        MessageLog.ItemsSource = lines;
        if (lines.Length > 0) MessageLog.ScrollIntoView(lines[^1]);
        MessageSummary.Text = string.IsNullOrEmpty(filter)
            ? $"{metrics.Messages:N0} total messages"
            : $"{lines.Length:N0} shown of {metrics.Messages:N0} · filter “{filter}”";

        DirectiveList.ItemsSource = null;
        DirectiveList.ItemsSource = _world.Directives.OrderBy(d => d.Tick).ToArray();
    }

    private void MessageFilterBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePanels();

    private void NewWorld_Click(object sender, RoutedEventArgs e)
    {
        _scenarioDirectives = null;
        RenderWorld();
    }

    private void OpenScenario_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Sphere scenario (*.sphere.json)|*.sphere.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open spherical scenario"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var scenario = SphericalScenarioSerializer.Load(dialog.FileName);
            _worldTitle = scenario.Title;
            _seed = scenario.Seed;
            _options = scenario.World;
            _scenarioDirectives = scenario.Directives.ToList();
            RenderWorld();
            StatusText.Text = $"Opened {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            MessageBox.Show(this, exception.Message, "Open scenario", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveScenario_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Sphere scenario (*.sphere.json)|*.sphere.json|JSON files (*.json)|*.json",
            DefaultExt = ".sphere.json",
            Title = "Save spherical scenario"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SphericalScenarioSerializer.Save(dialog.FileName, new SphericalScenarioDocument
            {
                Title = _worldTitle,
                Seed = _seed,
                World = _options,
                Directives = _world.Directives.ToList()
            });
            StatusText.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (IOException exception)
        {
            MessageBox.Show(this, exception.Message, "Save scenario", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportMetrics_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            Title = "Export spherical metrics"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SphericalMetricsExporter.ExportCsv(dialog.FileName, _world.CoverageSamples);
            StatusText.Text = $"Exported {Path.GetFileName(dialog.FileName)}";
        }
        catch (IOException exception)
        {
            MessageBox.Show(this, exception.Message, "Export metrics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewWorldOptions_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SphereWorldOptionsDialog { Owner = this };
        dialog.ViewModel.Title = _worldTitle;
        dialog.ViewModel.Seed = _seed.ToString();
        dialog.ViewModel.Radius = _options.Radius.ToString("0.###");
        dialog.ViewModel.SensorRange = _options.SensorRange.ToString("0.###");
        dialog.ViewModel.CommunicationRange = _options.CommunicationRange.ToString("0.###");
        dialog.ViewModel.Resolution = _options.Resolution.ToString();
        dialog.ViewModel.StepSize = _options.StepSize.ToString("0.###");
        if (dialog.ShowDialog() != true) return;
        if (!dialog.ViewModel.TryBuild(out var options, out var seed, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _options = options;
        _seed = seed;
        _worldTitle = dialog.ViewModel.Title;
        DirectiveSensorBox.Text = options.SensorRange.ToString("0.###");
        DirectiveCommBox.Text = options.CommunicationRange.ToString("0.###");
        try
        {
            RenderWorld();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            MessageBox.Show(this, exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        _timer.IsEnabled = false;
        RunButton.Content = "Run";
        AdvanceSimulation();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        _timer.IsEnabled = true;
        RunButton.Content = "Running";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.IsEnabled = false;
        RunButton.Content = "Run";
    }

    private void ScheduleDirective_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null || !TryBuildDirective(out var directive, applyNow: false)) return;
        _world.AddDirective(directive);
        UpdatePanels();
    }

    private void ApplyDirectiveNow_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null || !TryBuildDirective(out var directive, applyNow: true)) return;
        _world.ApplyDirectiveNow(directive with { Tick = _world.CurrentTick });
        SyncAgentModels();
        SyncIncidentModels();
        UpdateAgentPositions();
        UpdateIncidentPositions();
        UpdateHeatmap();
        UpdatePanels();
    }

    private void RemoveDirective_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null) return;
        if (DirectiveList.SelectedItem is not SphericalDirective directive) return;
        _world.RemoveDirective(directive);
        UpdatePanels();
    }

    private bool TryBuildDirective(out SphericalDirective directive, bool applyNow)
    {
        directive = default!;
        if (_world is null) return false;
        if (!long.TryParse(DirectiveTickBox.Text, out var tick))
        {
            MessageBox.Show(this, "Enter a valid tick.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!applyNow && tick < _world.CurrentTick)
        {
            MessageBox.Show(this, $"Enter a tick at or after {_world.CurrentTick}.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!int.TryParse(AgentCountBox.Text, out var count) || count < 1) count = 1;
        if (!double.TryParse(DirectiveSensorBox.Text, out var sensor)) sensor = _world.Options.SensorRange;
        if (!double.TryParse(DirectiveCommBox.Text, out var comm)) comm = _world.Options.CommunicationRange;
        if (!int.TryParse(DirectivePeriodBox.Text, out var period) || period < 1) period = 20;
        var kindName = (DirectiveKindBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var agentName = (AgentKindBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!Enum.TryParse(kindName, out SphericalDirectiveKind kind)
            || !Enum.TryParse(agentName, out SphericalAgentKind agentKind))
        {
            MessageBox.Show(this, "Select a valid directive and agent kind.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        directive = new SphericalDirective(
            Tick: applyNow ? _world.CurrentTick : tick,
            Kind: kind,
            Count: count,
            AgentKind: agentKind,
            SensorRange: sensor,
            CommunicationRange: comm,
            StepSize: _world.Options.StepSize,
            PeriodTicks: kind == SphericalDirectiveKind.DeployPeriodicIncidents ? period : null,
            IncidentType: DirectiveIncidentTypeBox.Text);
        return true;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _didDrag = false;
        _dragStart = e.GetPosition(WorldViewport);
        _dragYaw = _yaw;
        _dragPitch = _pitch;
        WorldViewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var click = e.GetPosition(WorldViewport);
        WorldViewport.ReleaseMouseCapture();
        if (!_didDrag) TrySelectByHitTest(click);
        _dragStart = null;
        UpdatePanels();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || !WorldViewport.IsMouseCaptured) return;
        var now = e.GetPosition(WorldViewport);
        if ((now - start).Length > 3) _didDrag = true;
        _yaw = _dragYaw + (now.X - start.X) * 0.5;
        _pitch = Math.Clamp(_dragPitch + (now.Y - start.Y) * 0.5, -85, 85);
        UpdateCamera();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.88 : 1.14), 2.1, 12);
        UpdateCamera();
    }

    private void TrySelectByHitTest(Point click)
    {
        var result = VisualTreeHelper.HitTest(WorldViewport, click);
        if (result is not RayMeshGeometry3DHitTestResult meshHit) return;
        if (meshHit.ModelHit is not GeometryModel3D model) return;
        if (_modelEntities.TryGetValue(model, out var entity))
            _selected = entity;
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var position = new Point3D(
            _distance * Math.Cos(pitch) * Math.Cos(yaw),
            _distance * Math.Sin(pitch),
            _distance * Math.Cos(pitch) * Math.Sin(yaw));
        _camera.Position = position;
        _camera.LookDirection = new Vector3D(-position.X, -position.Y, -position.Z);
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private static MeshGeometry3D Disc(double radius, int segments)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D());
        for (var i = 0; i < segments; i++)
        {
            var angle = Math.Tau * i / segments;
            mesh.Positions.Add(new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), 0));
        }
        for (var i = 0; i < segments; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add((i + 1) % segments + 1);
        }
        return mesh;
    }

    private static MeshGeometry3D Cylinder(double radius, double halfLength, int segments)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i <= segments; i++)
        {
            var angle = Math.Tau * i / segments;
            mesh.Positions.Add(new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), -halfLength));
            mesh.Positions.Add(new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), halfLength));
        }
        for (var i = 0; i < segments; i++)
        {
            var a = i * 2;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(a + 2);
            mesh.TriangleIndices.Add(a + 2); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(a + 3);
        }
        return mesh;
    }

    private static Rotation3D RotationFromZAxis(Vector3D normal)
    {
        var axis = Vector3D.CrossProduct(new Vector3D(0, 0, 1), normal);
        if (axis.LengthSquared < 1e-12)
            return new AxisAngleRotation3D(new Vector3D(1, 0, 0), normal.Z < 0 ? 180 : 0);
        axis.Normalize();
        return new AxisAngleRotation3D(axis,
            Math.Acos(Math.Clamp(Vector3D.DotProduct(new Vector3D(0, 0, 1), normal), -1, 1)) * 180 / Math.PI);
    }
}
