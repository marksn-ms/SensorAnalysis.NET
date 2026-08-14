using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using SensorAnalysis.Viewport3DPrototype.Geometry;

namespace SensorAnalysis.Viewport3DPrototype.Views;

/// <summary>Hosts and navigates the standalone continuous spherical-world prototype.</summary>
public partial class MainWindow : Window
{
    // A true r/100 footprint is well under one pixel at usable zoom levels, so the marker is
    // never drawn smaller than this even though the simulated footprint stays exact.
    private const double MinimumVisibleRadius = 0.02;

    private readonly PerspectiveCamera _camera = new();
    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly Random _random = new(42);
    private int _level = 100;
    private double _yaw = -38;
    private double _pitch = -22;
    private double _distance = 4.1;
    private Point? _dragStart;
    private double _dragYaw;
    private double _dragPitch;
    private PrototypeWorldShape? _world;
    private Point3D _inhabitantPosition;
    private Vector3D _inhabitantDirection;
    private TranslateTransform3D? _inhabitantTranslation;
    private RotateTransform3D? _inhabitantRotation;

    /// <summary>Initializes the 3D scene and displays a 100-center spherical world.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ConfigureCamera();
        _walkTimer.Tick += (_, _) => AdvanceInhabitant();
        RenderWorld();
    }

    private void ConfigureCamera()
    {
        _camera.FieldOfView = 48;
        WorldViewport.Camera = _camera;
    }

    private void RenderWorld()
    {
        _world = PlatonicWorldCatalog.Create(_level);
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(88, 88, 88)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-2, -3, -4)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(100, 130, 180), new Vector3D(3, 2, -1)));
        group.Children.Add(CreateWorldModel());
        foreach (var axis in CreateReferenceAxes())
            group.Children.Add(axis);

        _inhabitantPosition = _world.Centers[0];
        _inhabitantDirection = TangentDirection(_inhabitantPosition, new Vector3D(0, 0, 1));
        var footprintRadius = _world.SensorRange / 100;
        group.Children.Add(CreateInhabitantModel(Math.Max(footprintRadius, MinimumVisibleRadius)));

        WorldViewport.Children.Clear();
        WorldViewport.Children.Add(new ModelVisual3D { Content = group });

        WorldTitle.Text = $"Fibonacci sphere · n={_level}";
        WorldDetail.Text = $"Default sensor range r={_world.SensorRange:F3} · "
            + $"inhabitant footprint r/100={footprintRadius:F4}"
            + (footprintRadius < MinimumVisibleRadius ? $" (drawn at {MinimumVisibleRadius:F3} to stay visible)" : string.Empty);
        DecreaseButton.IsEnabled = _level > PlatonicWorldCatalog.MinLevel;
        UpdateCamera();
    }

    private static GeometryModel3D CreateWorldModel()
    {
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(60, 87, 77)));
        material.Freeze();
        return new GeometryModel3D(new SphereMeshBuilder(radius: 1, rings: 48, segments: 72).Build(), material)
        {
            BackMaterial = material
        };
    }

    private static IEnumerable<GeometryModel3D> CreateReferenceAxes()
    {
        yield return CreateAxisModel(new Vector3D(0, 1, 0), Color.FromRgb(232, 108, 108));
        yield return CreateAxisModel(new Vector3D(1, 0, 0), Color.FromRgb(120, 168, 240));
        yield return CreateAxisModel(new Vector3D(0, 0, 1), Color.FromRgb(132, 216, 148));
    }

    private static GeometryModel3D CreateAxisModel(Vector3D direction, Color color)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(
            (byte)(color.R / 3), (byte)(color.G / 3), (byte)(color.B / 3)))));
        material.Freeze();

        var mesh = new CylinderMeshBuilder(radius: 0.006, halfLength: 1.22, segments: 12).Build();
        var transform = new RotateTransform3D(RotationFromZAxis(direction));
        return new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = transform };
    }

    private GeometryModel3D CreateInhabitantModel(double radius)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 220, 60)));
        material.Freeze();
        _inhabitantTranslation = new TranslateTransform3D();
        _inhabitantRotation = new RotateTransform3D();
        var transform = new Transform3DGroup();
        transform.Children.Add(_inhabitantRotation);
        transform.Children.Add(_inhabitantTranslation);
        var model = new GeometryModel3D(new DiscMeshBuilder(radius, segments: 20).Build(), material)
        {
            BackMaterial = material,
            Transform = transform
        };
        UpdateInhabitantPosition();
        return model;
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

    private void Decrease_Click(object sender, RoutedEventArgs e)
    {
        if (_level <= PlatonicWorldCatalog.MinLevel) return;
        _level -= 50;
        RenderWorld();
    }

    private void Increase_Click(object sender, RoutedEventArgs e)
    {
        _level += 50;
        RenderWorld();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _walkTimer.IsEnabled = !_walkTimer.IsEnabled;
        StartButton.Content = _walkTimer.IsEnabled ? "Stop" : "Start";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(0.82);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(1.22);
    private void Left_Click(object sender, RoutedEventArgs e) => Rotate(-12, 0);
    private void Right_Click(object sender, RoutedEventArgs e) => Rotate(12, 0);
    private void Up_Click(object sender, RoutedEventArgs e) => Rotate(0, -10);
    private void Down_Click(object sender, RoutedEventArgs e) => Rotate(0, 10);

    private void Rotate(double yaw, double pitch)
    {
        _yaw += yaw;
        _pitch = Math.Clamp(_pitch + pitch, -85, 85);
        UpdateCamera();
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(WorldViewport);
        _dragYaw = _yaw;
        _dragPitch = _pitch;
        WorldViewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        WorldViewport.ReleaseMouseCapture();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || !WorldViewport.IsMouseCaptured) return;
        var now = e.GetPosition(WorldViewport);
        _yaw = _dragYaw + (now.X - start.X) * 0.5;
        _pitch = Math.Clamp(_dragPitch + (now.Y - start.Y) * 0.5, -85, 85);
        UpdateCamera();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) =>
        Zoom(e.Delta > 0 ? 0.88 : 1.14);

    private void Zoom(double factor)
    {
        _distance = Math.Clamp(_distance * factor, 2.1, 12);
        UpdateCamera();
    }

    private void AdvanceInhabitant()
    {
        // Vectors avoid the longitude discontinuity and the singularity at the north pole.
        var normal = new Vector3D(_inhabitantPosition.X, _inhabitantPosition.Y, _inhabitantPosition.Z);
        var sideways = Vector3D.CrossProduct(normal, _inhabitantDirection);
        sideways.Normalize();
        var steerRadians = (_random.NextDouble() - 0.5) * 0.36;
        _inhabitantDirection = TangentDirection(
            _inhabitantPosition,
            _inhabitantDirection * Math.Cos(steerRadians) + sideways * Math.Sin(steerRadians));

        const double stepRadians = 0.018;
        _inhabitantPosition = Normalize(_inhabitantPosition + _inhabitantDirection * stepRadians);
        _inhabitantDirection = TangentDirection(_inhabitantPosition, _inhabitantDirection);
        UpdateInhabitantPosition();
    }

    private void UpdateInhabitantPosition()
    {
        if (_inhabitantTranslation is null || _inhabitantRotation is null) return;
        var normal = new Vector3D(_inhabitantPosition.X, _inhabitantPosition.Y, _inhabitantPosition.Z);
        normal.Normalize();
        _inhabitantRotation.Rotation = RotationFromZAxis(normal);
        _inhabitantTranslation.OffsetX = normal.X * 1.004;
        _inhabitantTranslation.OffsetY = normal.Y * 1.004;
        _inhabitantTranslation.OffsetZ = normal.Z * 1.004;
    }

    private static Vector3D TangentDirection(Point3D position, Vector3D direction)
    {
        var normal = new Vector3D(position.X, position.Y, position.Z);
        normal.Normalize();
        var tangent = direction - normal * Vector3D.DotProduct(direction, normal);
        tangent.Normalize();
        return tangent;
    }

    private static Rotation3D RotationFromZAxis(Vector3D normal)
    {
        var zAxis = new Vector3D(0, 0, 1);
        var axis = Vector3D.CrossProduct(zAxis, normal);
        if (axis.LengthSquared < 1e-12)
            return new AxisAngleRotation3D(new Vector3D(1, 0, 0), normal.Z < 0 ? 180 : 0);

        axis.Normalize();
        var angle = Math.Acos(Math.Clamp(Vector3D.DotProduct(zAxis, normal), -1, 1)) * 180 / Math.PI;
        return new AxisAngleRotation3D(axis, angle);
    }

    private static Point3D Normalize(Point3D point)
    {
        var length = Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
        return new Point3D(point.X / length, point.Y / length, point.Z / length);
    }

    private sealed class DiscMeshBuilder(double radius, int segments)
    {
        public MeshGeometry3D Build()
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(new Point3D());
            for (var segment = 0; segment < segments; segment++)
            {
                var angle = Math.Tau * segment / segments;
                mesh.Positions.Add(new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), 0));
            }
            for (var segment = 0; segment < segments; segment++)
            {
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(segment + 1);
                mesh.TriangleIndices.Add((segment + 1) % segments + 1);
            }
            return mesh;
        }
    }

    private sealed class CylinderMeshBuilder(double radius, double halfLength, int segments)
    {
        public MeshGeometry3D Build()
        {
            var mesh = new MeshGeometry3D();
            for (var segment = 0; segment <= segments; segment++)
            {
                var angle = Math.Tau * segment / segments;
                var x = radius * Math.Cos(angle);
                var y = radius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, -halfLength));
                mesh.Positions.Add(new Point3D(x, y, halfLength));
            }
            for (var segment = 0; segment < segments; segment++)
            {
                var a = segment * 2;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 2);
                mesh.TriangleIndices.Add(a + 2);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 3);
            }
            return mesh;
        }
    }

    private sealed class SphereMeshBuilder(double radius, int rings, int segments)
    {
        public MeshGeometry3D Build()
        {
            var mesh = new MeshGeometry3D();
            for (var ring = 0; ring <= rings; ring++)
            {
                var theta = Math.PI * ring / rings;
                for (var segment = 0; segment <= segments; segment++)
                {
                    var phi = Math.Tau * segment / segments;
                    mesh.Positions.Add(new Point3D(
                        radius * Math.Sin(theta) * Math.Cos(phi),
                        radius * Math.Cos(theta),
                        radius * Math.Sin(theta) * Math.Sin(phi)));
                }
            }
            for (var ring = 0; ring < rings; ring++)
                for (var segment = 0; segment < segments; segment++)
                {
                    var a = ring * (segments + 1) + segment;
                    var b = a + segments + 1;
                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b + 1);
                }
            return mesh;
        }
    }
}
