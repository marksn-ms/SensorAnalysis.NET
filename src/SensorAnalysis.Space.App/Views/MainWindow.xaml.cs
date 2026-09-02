using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using SensorAnalysis.Space.App.Rendering;
using SensorAnalysis.Space.Core.Domain;
using SensorAnalysis.Space.Core.World;

namespace SensorAnalysis.Space.App.Views;

/// <summary>Volumetric world shell: orbit/fly camera, back-wall grid, and inhabitant selection.</summary>
public partial class MainWindow : Window
{
    private const double DragThresholdPx = 3;

    private readonly PerspectiveCamera _camera = new() { FieldOfView = 48 };
    private readonly OrbitCameraController _view = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly Dictionary<GeometryModel3D, WorldObject> _modelEntities = [];
    private readonly List<(WorldObject Object, GeometryModel3D Model, MatrixTransform3D Transform)> _entityTransforms = [];
    private readonly HashSet<Key> _keys = [];
    private readonly MeshGeometry3D _sphereMesh = MeshFactory.Sphere();
    private readonly MeshGeometry3D _cubeMesh = MeshFactory.Cube();
    private readonly MeshGeometry3D _coneMesh = MeshFactory.Cone();
    private readonly MeshGeometry3D _tetraMesh = MeshFactory.Tetrahedron();
    private readonly MeshGeometry3D _quadMesh = MeshFactory.UnitQuad();

    private SimWorld? _world;
    private WorldOptions _options = new();
    private int _seed = 42;
    private Model3DGroup? _scene;
    private Model3DGroup? _gridGroup;
    private Model3DGroup? _axisGroup;
    private Model3DGroup? _overlayGroup;
    private WorldObject? _selected;
    private readonly List<string> _metricHistory = [];
    private bool _viewCentered = true;
    private bool _hasTrack;
    private Vec3 _trackWrapped;
    private double _gridSpacing = -1;
    private (int X, int Y, int Z) _backFaces;
    private Point? _dragStart;
    private double _dragYaw;
    private double _dragPitch;
    private Vec3 _dragFocus;
    private Point3D _dragFlyPos;
    private Vector3D _dragFlyLook;
    private bool _didDrag;
    private bool _panning;

    /// <summary>Initializes the space world scene.</summary>
    public MainWindow()
    {
        InitializeComponent();
        WorldViewport.Camera = _camera;
        _timer.Tick += (_, _) => AdvanceSimulation();
        RenderWorld();
    }

    private void AdvanceSimulation()
    {
        SampleFlyKeys();
        _world?.Tick();
        SyncNewEntities();
        SyncTransforms();
        UpdateFollow();
        RebuildGridIfNeeded();
        UpdateAxes();
        UpdateOverlays();
        ApplyCamera();
        UpdatePanels();
    }

    private void RenderWorld()
    {
        _timer.IsEnabled = false;
        RunButton.Content = "Run";
        _selected = null;
        _hasTrack = false;
        _modelEntities.Clear();
        _entityTransforms.Clear();
        _gridSpacing = -1;
        _backFaces = default;
        _metricHistory.Clear();

        _world = new SimWorld(_options, _seed);
        _view.FieldOfView = _options.FieldOfView;
        FovBox.Text = ((int)_options.FieldOfView).ToString();

        _scene = new Model3DGroup();
        _scene.Children.Add(new AmbientLight(Color.FromRgb(48, 52, 64)));
        _scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.8, -0.6)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(70, 90, 130), new Vector3D(0.6, 0.2, 0.5)));

        _gridGroup = new Model3DGroup();
        _axisGroup = new Model3DGroup();
        _overlayGroup = new Model3DGroup();
        _scene.Children.Add(_gridGroup);
        _scene.Children.Add(_overlayGroup);
        _scene.Children.Add(_axisGroup);

        foreach (var obj in _world.AllObjects)
            AddEntityModel(obj);

        WorldViewport.Children.Clear();
        WorldViewport.Children.Add(new ModelVisual3D { Content = _scene });

        var distance = Math.Max(_options.Extent * 0.62, _options.BeltOuterRadius * 3.2);
        _view.Reset(Vec3.Zero, distance);
        _view.MinDistance = Math.Max(8, _options.PlanetoidRadius * 0.4);
        _view.MaxDistance = Math.Max(distance * 8, _options.Extent * 4);
        _view.FlySpeed = Math.Max(2, _options.Extent * 0.012);
        RebuildGridIfNeeded(force: true);
        UpdateAxes();
        ApplyCamera();
        UpdatePanels();
        StatusText.Text = $"{_world.Title} · {_options.CubesPerAxis}³ · extent {_options.Extent:0}";
        SyncFlyUi();
    }

    private void AddEntityModel(WorldObject obj)
    {
        var mesh = MeshFor(obj);
        var material = MeshFactory.Material(ColorFor(obj), emissive: obj is Agent);
        var transform = new MatrixTransform3D(PoseTransform.From(obj));
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
            Transform = transform
        };
        _scene!.Children.Add(model);
        _modelEntities[model] = obj;
        _entityTransforms.Add((obj, model, transform));
    }

    private void SyncNewEntities()
    {
        if (_world is null || _scene is null)
            return;
        var known = _entityTransforms.Select(t => t.Object.Id).ToHashSet();
        foreach (var obj in _world.AllObjects)
        {
            if (known.Add(obj.Id))
                AddEntityModel(obj);
        }
    }

    private MeshGeometry3D MeshFor(WorldObject obj) => obj.Visual switch
    {
        VisualKind.Cube => _cubeMesh,
        VisualKind.Cone => _coneMesh,
        VisualKind.Tetrahedron => _tetraMesh,
        VisualKind.Icosphere => MeshFactory.Icosphere(1, 0.2, obj.Id),
        _ => _sphereMesh
    };

    private static Color ColorFor(WorldObject obj)
    {
        if (obj is Agent)
            return Color.FromRgb(90, 210, 230);
        return obj.ObjectType switch
        {
            "Planetoid" => Color.FromRgb(168, 156, 146),
            "Asteroid" => AsteroidColor(obj.Id),
            _ when obj.Visual == VisualKind.Cube => Color.FromRgb(48, 196, 96),
            _ => Color.FromRgb(210, 150, 70)
        };
    }

    private static Color AsteroidColor(int id)
    {
        var h = (uint)id * 2654435761u;
        var g = (byte)(90 + (h & 40));
        var r = (byte)(110 + ((h >> 8) & 35));
        var b = (byte)(100 + ((h >> 16) & 25));
        return Color.FromRgb(r, g, b);
    }

    private void AddBar(Model3DGroup group, Point3D from, Point3D to, double thickness, Material material)
    {
        var matrix = MeshFactory.BarTransform(from, to, thickness);
        if (matrix is null)
            return;
        group.Children.Add(new GeometryModel3D(_cubeMesh, material)
        {
            BackMaterial = material,
            Transform = new MatrixTransform3D(matrix.Value)
        });
    }

    private void SyncTransforms()
    {
        foreach (var (obj, model, transform) in _entityTransforms)
        {
            transform.Matrix = PoseTransform.From(obj);
            var selected = ReferenceEquals(obj, _selected);
            var color = ColorFor(obj);
            if (selected)
            {
                color = Color.FromRgb(
                    (byte)Math.Min(255, color.R + 36),
                    (byte)Math.Min(255, color.G + 36),
                    (byte)Math.Min(255, color.B + 28));
            }

            var material = MeshFactory.Material(color, emissive: selected || obj is Agent);
            model.Material = material;
            model.BackMaterial = material;
        }
    }

    private void UpdateFollow()
    {
        if (_world is null || _view.Fly || !_viewCentered)
            return;

        Vec3 target = Vec3.Zero;
        if (_selected is not null)
            target = _selected.Position;

        if (!_hasTrack)
        {
            _trackWrapped = target;
            _view.Focus = target;
            _hasTrack = true;
            return;
        }

        _view.Focus += _world.Displacement(_trackWrapped, target);
        _trackWrapped = target;
    }

    private void RebuildGridIfNeeded(bool force = false)
    {
        if (_world is null || _gridGroup is null)
            return;

        var h = _world.Options.HalfExtent;
        var spacing = SnapLog(_world.Options.Extent / 10.0);
        spacing = Math.Clamp(spacing, _world.Options.CubeSize, h);
        var faces = BackFacesFromCamera();
        if (!force
            && Math.Abs(spacing - _gridSpacing) < spacing * 0.01
            && faces == _backFaces)
            return;

        _gridSpacing = spacing;
        _backFaces = faces;
        _gridGroup.Children.Clear();
        _gridGroup.Transform = Transform3D.Identity;

        var wallMat = MeshFactory.Material(Color.FromRgb(16, 20, 28));
        var gridMat = MeshFactory.Material(Color.FromRgb(52, 62, 78), emissive: true);
        var edgeMat = MeshFactory.Material(Color.FromRgb(86, 98, 116), emissive: true);
        var gridThick = Math.Max(_world.Options.Extent * 0.0012, 0.22);
        var inset = Math.Max(_world.Options.Extent * 0.0008, 0.15);

        AddWall(faces.X < 0 ? -h : h, 0, 0, new Vector3D(faces.X < 0 ? 1 : -1, 0, 0), h, wallMat);
        AddWall(0, faces.Y < 0 ? -h : h, 0, new Vector3D(0, faces.Y < 0 ? 1 : -1, 0), h, wallMat);
        AddWall(0, 0, faces.Z < 0 ? -h : h, new Vector3D(0, 0, faces.Z < 0 ? 1 : -1), h, wallMat);

        AddFaceGrid(faces, h, spacing, inset, gridThick, gridMat, edgeMat);
    }

    private (int X, int Y, int Z) BackFacesFromCamera()
    {
        var position = _view.Fly ? _view.FlyPosition : _view.OrbitPose().Position;
        return (FarSign(position.X, _backFaces.X), FarSign(position.Y, _backFaces.Y), FarSign(position.Z, _backFaces.Z));
    }

    private static int FarSign(double cameraCoord, int previous)
    {
        if (Math.Abs(cameraCoord) < 1e-6)
            return previous == 0 ? -1 : previous;
        return cameraCoord >= 0 ? -1 : 1;
    }

    private void AddWall(double x, double y, double z, Vector3D inward, double halfExtent, Material material)
    {
        var matrix = new Matrix3D();
        matrix.Scale(new Vector3D(halfExtent, halfExtent, 1));
        matrix.Rotate(MeshFactory.RotationAligningZ(inward));
        matrix.Translate(new Vector3D(x, y, z));
        _gridGroup!.Children.Add(new GeometryModel3D(_quadMesh, material)
        {
            BackMaterial = material,
            Transform = new MatrixTransform3D(matrix)
        });
    }

    private void AddFaceGrid(
        (int X, int Y, int Z) faces,
        double h,
        double spacing,
        double inset,
        double thickness,
        Material gridMat,
        Material edgeMat)
    {
        var x = FaceCoord(faces.X, h);
        var y = FaceCoord(faces.Y, h);
        var z = FaceCoord(faces.Z, h);
        var xi = x - faces.X * inset;
        var yi = y - faces.Y * inset;
        var zi = z - faces.Z * inset;

        AddBar(_gridGroup!, new Point3D(xi, -h, -h), new Point3D(xi, h, -h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(xi, h, -h), new Point3D(xi, h, h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(xi, h, h), new Point3D(xi, -h, h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(xi, -h, h), new Point3D(xi, -h, -h), thickness, edgeMat);

        AddBar(_gridGroup!, new Point3D(-h, yi, -h), new Point3D(h, yi, -h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(h, yi, -h), new Point3D(h, yi, h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(h, yi, h), new Point3D(-h, yi, h), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(-h, yi, h), new Point3D(-h, yi, -h), thickness, edgeMat);

        AddBar(_gridGroup!, new Point3D(-h, -h, zi), new Point3D(h, -h, zi), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(h, -h, zi), new Point3D(h, h, zi), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(h, h, zi), new Point3D(-h, h, zi), thickness, edgeMat);
        AddBar(_gridGroup!, new Point3D(-h, h, zi), new Point3D(-h, -h, zi), thickness, edgeMat);

        for (var s = spacing; s < h - spacing * 0.4; s += spacing)
        {
            AddBar(_gridGroup!, new Point3D(xi, -s, -h), new Point3D(xi, -s, h), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(xi, s, -h), new Point3D(xi, s, h), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(xi, -h, -s), new Point3D(xi, h, -s), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(xi, -h, s), new Point3D(xi, h, s), thickness, gridMat);

            AddBar(_gridGroup!, new Point3D(-s, yi, -h), new Point3D(-s, yi, h), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(s, yi, -h), new Point3D(s, yi, h), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(-h, yi, -s), new Point3D(h, yi, -s), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(-h, yi, s), new Point3D(h, yi, s), thickness, gridMat);

            AddBar(_gridGroup!, new Point3D(-s, -h, zi), new Point3D(-s, h, zi), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(s, -h, zi), new Point3D(s, h, zi), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(-h, -s, zi), new Point3D(h, -s, zi), thickness, gridMat);
            AddBar(_gridGroup!, new Point3D(-h, s, zi), new Point3D(h, s, zi), thickness, gridMat);
        }
    }

    private static double FaceCoord(int farSign, double halfExtent) => farSign < 0 ? -halfExtent : halfExtent;

    private static double SnapLog(double value)
    {
        if (value <= 1e-6)
            return 1;
        var exp = Math.Floor(Math.Log10(value));
        var mant = value / Math.Pow(10, exp);
        var nice = mant < 2 ? 1 : mant < 5 ? 2 : 5;
        return nice * Math.Pow(10, exp);
    }

    private void UpdateAxes()
    {
        if (_axisGroup is null)
            return;

        _axisGroup.Children.Clear();
        var len = Math.Max(_view.Distance * 0.05, 6);
        var thick = Math.Max(_view.Distance * 0.0016, 0.1);
        AddBar(_axisGroup, new Point3D(-len, 0, 0), new Point3D(len, 0, 0), thick,
            MeshFactory.Material(Color.FromRgb(150, 72, 72), emissive: true));
        AddBar(_axisGroup, new Point3D(0, -len, 0), new Point3D(0, len, 0), thick,
            MeshFactory.Material(Color.FromRgb(72, 140, 88), emissive: true));
        AddBar(_axisGroup, new Point3D(0, 0, -len), new Point3D(0, 0, len), thick,
            MeshFactory.Material(Color.FromRgb(72, 108, 168), emissive: true));
    }

    private void UpdateOverlays()
    {
        if (_overlayGroup is null)
            return;

        _overlayGroup.Children.Clear();
        if (_selected is not { } obj)
            return;

        var r = Math.Max(obj.VisualRadius * 1.22, 1.2);
        var p = obj.Position;
        var t = Math.Max(r * 0.035, 0.12);
        var mat = MeshFactory.Material(Color.FromRgb(255, 214, 96), emissive: true);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y - r, p.Z - r), new Point3D(p.X + r, p.Y - r, p.Z - r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y - r, p.Z - r), new Point3D(p.X + r, p.Y + r, p.Z - r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y + r, p.Z - r), new Point3D(p.X - r, p.Y + r, p.Z - r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y + r, p.Z - r), new Point3D(p.X - r, p.Y - r, p.Z - r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y - r, p.Z + r), new Point3D(p.X + r, p.Y - r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y - r, p.Z + r), new Point3D(p.X + r, p.Y + r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y + r, p.Z + r), new Point3D(p.X - r, p.Y + r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y + r, p.Z + r), new Point3D(p.X - r, p.Y - r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y - r, p.Z - r), new Point3D(p.X - r, p.Y - r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y - r, p.Z - r), new Point3D(p.X + r, p.Y - r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X + r, p.Y + r, p.Z - r), new Point3D(p.X + r, p.Y + r, p.Z + r), t, mat);
        AddBar(_overlayGroup, new Point3D(p.X - r, p.Y + r, p.Z - r), new Point3D(p.X - r, p.Y + r, p.Z + r), t, mat);
    }

    private void ApplyCamera()
    {
        var diag = _world is null ? 1000 : _world.Extent * Math.Sqrt(3);
        _view.Apply(_camera, diag);
        UpdateAxes();
    }

    private void UpdatePanels()
    {
        TickText.Text = _world is null ? "tick 0" : $"tick {_world.CurrentTick}";
        if (_world is null)
            return;

        WorldSummary.Text =
            $"kind {_options.Kind}" +
            (_options.Kind == WorldKind.ToroidalStream ? $"  drift {_options.DriftAxis}" : "") + "\n" +
            $"extent {_options.Extent:0}  cubes {_options.CubesPerAxis}³ × {_options.CubeSize}\n" +
            $"bodies {_world.Bodies.Count}  agents {_world.Agents.Count}  incidents {_world.Incidents.Count}\n" +
            $"coverage {_world.CoverageFraction:P1}";

        SelectionSummary.Text = _selected is { } obj
            ? FormatObject(obj)
            : "Nothing selected. Click an inhabitant.";

        RefreshMessageLog();
        RefreshDirectiveList();
        RefreshMetrics();
    }

    private void RefreshMessageLog()
    {
        if (_world is null)
            return;
        var filter = MessageFilterBox.Text?.Trim() ?? "";
        var lines = _world.Messages
            .Where(m => filter.Length == 0
                        || m.Tick.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || m.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(m => $"t{m.Tick}  {m.Text}")
            .ToList();
        MessageLog.ItemsSource = lines;
        MessageSummary.Text = lines.Count == 0
            ? (filter.Length == 0 ? "No messages yet." : "No messages match the filter.")
            : $"{lines.Count} line(s)";
    }

    private void RefreshDirectiveList()
    {
        if (_world is null)
            return;
        DirectiveList.ItemsSource = _world.Directives.ToList();
    }

    private void RefreshMetrics()
    {
        if (_world is null)
            return;
        var meanV = _world.AllObjects.Count == 0
            ? 0
            : _world.AllObjects.Average(o => o.Velocity.Length);
        MetricsSummary.Text =
            $"tick {_world.CurrentTick}  coverage {_world.CoverageFraction:P2}\n" +
            $"agents {_world.Agents.Count}  bodies {_world.Bodies.Count}  mean |v| {meanV:0.###}\n" +
            $"watcher {( _world.CoverageWatcherEnabled ? "on" : "off")}";

        if (_world.CoverageWatcherEnabled || _world.CurrentTick == 0)
        {
            var line = $"t{_world.CurrentTick}  cov {_world.CoverageFraction:P2}  n={_world.Agents.Count}";
            if (_metricHistory.Count == 0 || _metricHistory[^1] != line)
                _metricHistory.Add(line);
            if (_metricHistory.Count > 200)
                _metricHistory.RemoveAt(0);
        }

        MetricHistoryLog.ItemsSource = null;
        MetricHistoryLog.ItemsSource = _metricHistory.AsEnumerable().Reverse().ToList();
    }

    private void MessageFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshMessageLog();

    private bool TryReadDirectiveDraft(out Directive directive, out string error)
    {
        directive = null!;
        error = "";
        if (!long.TryParse(DirectiveTickBox.Text, out var tick) || tick < 0)
        {
            error = "At tick must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(DirectiveCountBox.Text, out var count) || count < 1)
            count = 1;

        var kind = DirectiveKindBox.SelectedIndex switch
        {
            1 => DirectiveKind.DeployAsteroids,
            2 => DirectiveKind.EnableCoverageWatcher,
            _ => DirectiveKind.DeployUniformAgents
        };
        directive = new Directive { AtTick = tick, Kind = kind, Count = count };
        return true;
    }

    private void ScheduleDirective_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null)
            return;
        if (!TryReadDirectiveDraft(out var directive, out var error))
        {
            StatusText.Text = error;
            return;
        }

        _world.AddDirective(directive);
        SyncNewEntities();
        RefreshDirectiveList();
        RefreshMessageLog();
        StatusText.Text = $"Scheduled {directive}.";
    }

    private void ApplyDirectiveNow_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null)
            return;
        if (!TryReadDirectiveDraft(out var directive, out var error))
        {
            StatusText.Text = error;
            return;
        }

        _world.AddDirective(new Directive
        {
            AtTick = _world.CurrentTick,
            Kind = directive.Kind,
            Count = directive.Count
        });
        SyncNewEntities();
        SyncTransforms();
        RefreshDirectiveList();
        RefreshMessageLog();
        UpdatePanels();
        StatusText.Text = "Applied directive at current tick.";
    }

    private void RemoveDirective_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null || DirectiveList.SelectedItem is not Directive directive)
            return;
        _world.RemoveDirective(directive);
        RefreshDirectiveList();
    }

    private string FormatObject(WorldObject obj)
    {
        var extra = obj is Agent agent
            ? $"\nsensor {agent.SensorRange:0.#}  fuel {(double.IsPositiveInfinity(agent.Fuel) ? "∞" : agent.Fuel.ToString("0.###"))}  Δv/tick {agent.MaxDeltaVPerTick:0.###}"
            : obj is Body body
                ? $"\nμ {body.Mu:0.###}  anchored {body.IsAnchored}"
                : "";
        return
            $"{obj.ObjectType} #{obj.Id}  {obj.Label}\n" +
            $"pos ({obj.Position.X:0.##}, {obj.Position.Y:0.##}, {obj.Position.Z:0.##})\n" +
            $"vel ({obj.Velocity.X:0.###}, {obj.Velocity.Y:0.###}, {obj.Velocity.Z:0.###})  |v| {obj.Velocity.Length:0.###}\n" +
            $"mass {obj.Mass:0.##}  inertia {obj.HasInertia}  shield {obj.ShieldRadius:0.##}\n" +
            $"visual {obj.Visual}" + extra;
    }

    private void NewWorld_Click(object sender, RoutedEventArgs e) => RenderWorld();

    private void NewWorldOptions_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WorldOptionsDialog { Owner = this };
        dialog.ViewModel.LoadFrom(_options, _seed);
        if (dialog.ShowDialog() != true)
            return;
        if (!dialog.ViewModel.TryBuild(out var options, out var seed, out _))
            return;
        _options = options;
        _seed = seed;
        Title = $"SensorAnalysis — {options.Title}";
        RenderWorld();
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
        ApplyDelay();
        _timer.IsEnabled = true;
        RunButton.Content = "Pause";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.IsEnabled = false;
        RunButton.Content = "Run";
    }

    private void DelayBox_LostFocus(object sender, RoutedEventArgs e) => ApplyDelay();

    private void ApplyDelay()
    {
        if (int.TryParse(DelayBox.Text, out var ms) && ms >= 1)
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(ms, 1, 2000));
    }

    private void FovBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(FovBox.Text, out var fov))
            return;
        _view.FieldOfView = Math.Clamp(fov, 35, 75);
        FovBox.Text = ((int)_view.FieldOfView).ToString();
        ApplyCamera();
        RebuildGridIfNeeded(force: true);
    }

    private void ViewCentered_Changed(object sender, RoutedEventArgs e)
    {
        _viewCentered = sender switch
        {
            MenuItem => ViewCenteredMenuItem.IsChecked == true,
            _ => ViewCenteredCheckBox.IsChecked == true
        };
        ViewCenteredMenuItem.IsChecked = _viewCentered;
        ViewCenteredCheckBox.IsChecked = _viewCentered;
        _hasTrack = false;
        if (_viewCentered && _view.Fly)
            SetFly(false);
        UpdateFollow();
        ApplyCamera();
    }

    private void Fly_Changed(object sender, RoutedEventArgs e)
    {
        var fly = sender switch
        {
            MenuItem => FlyMenuItem.IsChecked == true,
            _ => FlyCheckBox.IsChecked == true
        };
        SetFly(fly);
    }

    private void SetFly(bool fly)
    {
        if (fly && !_view.Fly)
        {
            _view.EnterFly();
            _viewCentered = false;
            ViewCenteredCheckBox.IsChecked = false;
            ViewCenteredMenuItem.IsChecked = false;
        }
        else if (!fly && _view.Fly)
        {
            _view.ExitFly();
        }

        FlyCheckBox.IsChecked = _view.Fly;
        FlyMenuItem.IsChecked = _view.Fly;
        ApplyCamera();
    }

    private void SyncFlyUi()
    {
        FlyCheckBox.IsChecked = _view.Fly;
        FlyMenuItem.IsChecked = _view.Fly;
        ViewCenteredCheckBox.IsChecked = _viewCentered;
        ViewCenteredMenuItem.IsChecked = _viewCentered;
    }

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null)
            return;
        SetFly(false);
        var distance = Math.Max(_options.Extent * 0.62, _options.BeltOuterRadius * 3.2);
        _view.Reset(Vec3.Zero, distance);
        _hasTrack = false;
        ApplyCamera();
        RebuildGridIfNeeded(force: true);
    }

    private void FrameSelection_Click(object sender, RoutedEventArgs e) => FrameSelection();

    private void FrameSelection()
    {
        Vec3 target = _selected?.Position ?? Vec3.Zero;
        var radius = _selected is { } wo ? Math.Max(wo.VisualRadius * 8, 40) : Math.Max(_options.BeltOuterRadius * 2.4, 80);
        _view.Focus = target;
        _view.Distance = Math.Clamp(radius, _view.MinDistance, _view.MaxDistance);
        _hasTrack = false;
        ApplyCamera();
        RebuildGridIfNeeded(force: true);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        WorldViewport.Focus();
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _didDrag = false;
            _dragStart = e.GetPosition(WorldViewport);
            _dragFocus = _view.Focus;
            _dragFlyPos = _view.FlyPosition;
            WorldViewport.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WorldViewport.Focus();
        _didDrag = false;
        _panning = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        _dragStart = e.GetPosition(WorldViewport);
        _dragYaw = _view.Yaw;
        _dragPitch = _view.Pitch;
        _dragFocus = _view.Focus;
        _dragFlyPos = _view.FlyPosition;
        _dragFlyLook = _view.FlyLook;
        WorldViewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var click = e.GetPosition(WorldViewport);
        if (WorldViewport.IsMouseCaptured)
            WorldViewport.ReleaseMouseCapture();

        if (!_didDrag && !_panning)
            TrySelect(click);

        _dragStart = null;
        _didDrag = false;
        _panning = false;
        UpdateFollow();
        UpdateOverlays();
        UpdatePanels();
        ApplyCamera();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || !WorldViewport.IsMouseCaptured)
            return;

        var now = e.GetPosition(WorldViewport);
        if ((now - start).Length > DragThresholdPx)
            _didDrag = true;

        var dx = now.X - start.X;
        var dy = now.Y - start.Y;
        var pan = _panning || e.MiddleButton == MouseButtonState.Pressed;
        if (pan)
        {
            if (_view.Fly)
            {
                _view.FlyPosition = _dragFlyPos;
                _view.FlyMove(0, -dx * 0.04, dy * 0.04);
            }
            else
            {
                _view.Focus = _dragFocus;
                _view.Pan(dx, dy);
            }
        }
        else if (_view.Fly)
        {
            _view.FlyLook = _dragFlyLook;
            _view.FlyLookDelta(dx * 0.35, -dy * 0.35);
        }
        else
        {
            _view.Yaw = _dragYaw;
            _view.Pitch = _dragPitch;
            _view.Orbit(dx * 0.35, dy * 0.35);
        }

        ApplyCamera();
        RebuildGridIfNeeded();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _view.Zoom(e.Delta > 0 ? 0.88 : 1.14);
        ApplyCamera();
        RebuildGridIfNeeded();
        e.Handled = true;
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;
        if (WorldViewport.IsMouseCaptured)
            WorldViewport.ReleaseMouseCapture();
        _dragStart = null;
        _didDrag = false;
        _panning = false;
    }

    private void TrySelect(Point click)
    {
        if (_world is null)
            return;

        foreach (var obj in _world.AllObjects)
            obj.IsSelected = false;

        WorldObject? picked = null;
        VisualTreeHelper.HitTest(
            WorldViewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult meshHit
                    && meshHit.ModelHit is GeometryModel3D model
                    && _modelEntities.TryGetValue(model, out var entity))
                {
                    picked = entity;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(click));

        if (picked is not null)
        {
            picked.IsSelected = true;
            _selected = picked;
            _hasTrack = false;
            return;
        }

        _selected = null;
        _hasTrack = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        _keys.Add(e.Key);
        switch (e.Key)
        {
            case Key.F5:
                Run_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F10:
                Step_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Home:
                ResetCamera_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F:
                FrameSelection();
                e.Handled = true;
                break;
            case Key.Left:
                if (!_view.Fly) { _view.Orbit(-6, 0); ApplyCamera(); RebuildGridIfNeeded(); }
                e.Handled = true;
                break;
            case Key.Right:
                if (!_view.Fly) { _view.Orbit(6, 0); ApplyCamera(); RebuildGridIfNeeded(); }
                e.Handled = true;
                break;
            case Key.Up:
                if (!_view.Fly) { _view.Orbit(0, -5); ApplyCamera(); RebuildGridIfNeeded(); }
                e.Handled = true;
                break;
            case Key.Down:
                if (!_view.Fly) { _view.Orbit(0, 5); ApplyCamera(); RebuildGridIfNeeded(); }
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e) => _keys.Remove(e.Key);

    private void SampleFlyKeys()
    {
        if (!_view.Fly || Keyboard.FocusedElement is TextBox)
            return;

        var f = (_keys.Contains(Key.W) ? 1 : 0) + (_keys.Contains(Key.S) ? -1 : 0);
        var r = (_keys.Contains(Key.D) ? 1 : 0) + (_keys.Contains(Key.A) ? -1 : 0);
        var u = (_keys.Contains(Key.E) ? 1 : 0) + (_keys.Contains(Key.Q) ? -1 : 0);
        if (f == 0 && r == 0 && u == 0)
            return;
        _view.FlyMove(f, r, u);
    }
}
