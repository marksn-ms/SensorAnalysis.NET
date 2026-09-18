using System.ComponentModel;
using System.Runtime.CompilerServices;
using SensorAnalysis.Space.Core.World;

namespace SensorAnalysis.Space.App.Views;

/// <summary>Editable New World options for SensorAnalysis.Space.</summary>
public sealed class WorldOptionsDialogViewModel : INotifyPropertyChanged
{
    private string _title = "Space";
    private string _seed = "42";
    private string _cubesPerAxis = "24";
    private string _cubeSize = "40";
    private int _kindIndex;
    private int _driftAxisIndex;
    private string _streamRadius = "120";
    private string _asteroidCount = "28";
    private string _agentCount = "4";
    private string _orbitPeriodTicks = "1200";
    private string _sensorRange = "80";
    private string _fieldOfView = "48";
    private string _summary = string.Empty;
    private bool _canAccept = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or sets the world title.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets the seed text.</summary>
    public string Seed
    {
        get => _seed;
        set { _seed = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets cubes along each axis.</summary>
    public string CubesPerAxis
    {
        get => _cubesPerAxis;
        set { _cubesPerAxis = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets cube edge length.</summary>
    public string CubeSize
    {
        get => _cubeSize;
        set { _cubeSize = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets 0 = PlanetoidBelt, 1 = ToroidalStream, 2 = Empty.</summary>
    public int KindIndex
    {
        get => _kindIndex;
        set { _kindIndex = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets 0 = X, 1 = Y, 2 = Z for <see cref="WorldKind.ToroidalStream"/>.</summary>
    public int DriftAxisIndex
    {
        get => _driftAxisIndex;
        set { _driftAxisIndex = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets the ToroidalStream cylinder radius text.</summary>
    public string StreamRadius
    {
        get => _streamRadius;
        set { _streamRadius = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets asteroid count text.</summary>
    public string AsteroidCount
    {
        get => _asteroidCount;
        set { _asteroidCount = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets weaver count text.</summary>
    public string AgentCount
    {
        get => _agentCount;
        set { _agentCount = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets mid-belt period text.</summary>
    public string OrbitPeriodTicks
    {
        get => _orbitPeriodTicks;
        set { _orbitPeriodTicks = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets default sensor range text.</summary>
    public string SensorRange
    {
        get => _sensorRange;
        set { _sensorRange = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets or sets horizontal FOV text.</summary>
    public string FieldOfView
    {
        get => _fieldOfView;
        set { _fieldOfView = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Gets a derived extent summary.</summary>
    public string Summary
    {
        get => _summary;
        private set { _summary = value; OnPropertyChanged(); }
    }

    /// <summary>Gets whether OK may be accepted.</summary>
    public bool CanAccept
    {
        get => _canAccept;
        private set { _canAccept = value; OnPropertyChanged(); }
    }

    /// <summary>Creates the view model and computes the initial summary.</summary>
    public WorldOptionsDialogViewModel() => RefreshDerived();

    /// <summary>Copies fields from an existing options + seed pair.</summary>
    /// <param name="options">Current world options.</param>
    /// <param name="seed">Current seed.</param>
    public void LoadFrom(WorldOptions options, int seed)
    {
        Title = options.Title;
        Seed = seed.ToString();
        CubesPerAxis = options.CubesPerAxis.ToString();
        CubeSize = options.CubeSize.ToString();
        KindIndex = IndexFromKind(options.Kind);
        DriftAxisIndex = (int)options.DriftAxis;
        StreamRadius = options.StreamRadius.ToString();
        AsteroidCount = options.AsteroidCount.ToString();
        AgentCount = options.AgentCount.ToString();
        OrbitPeriodTicks = options.OrbitPeriodTicks.ToString();
        SensorRange = options.SensorRange.ToString();
        FieldOfView = options.FieldOfView.ToString();
    }

    /// <summary>Validates the form and returns typed options when successful.</summary>
    /// <param name="options">Built options.</param>
    /// <param name="seed">Parsed seed.</param>
    /// <param name="error">Validation error when unsuccessful.</param>
    /// <returns><c>true</c> when the form is valid.</returns>
    public bool TryBuild(out WorldOptions options, out int seed, out string error)
    {
        options = default!;
        seed = 0;
        error = string.Empty;
        if (!TryParse(out var parsed, out seed, out error))
            return false;
        options = parsed;
        return true;
    }

    private void RefreshDerived()
    {
        if (!TryParse(out var options, out _, out var error))
        {
            Summary = error;
            CanAccept = false;
            return;
        }

        Summary = options.Kind == WorldKind.ToroidalStream
            ? $"{options.CubesPerAxis}³ cubes × {options.CubeSize} · extent {options.Extent:0} · {options.Kind} r={options.StreamRadius:0} along {options.DriftAxis}"
            : $"{options.CubesPerAxis}³ cubes × {options.CubeSize} · extent {options.Extent:0} · {options.Kind}";
        CanAccept = true;
    }

    private bool TryParse(out WorldOptions options, out int seed, out string error)
    {
        options = new WorldOptions();
        seed = 0;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Title))
        {
            error = "Title is required.";
            return false;
        }

        if (!int.TryParse(Seed, out seed))
        {
            error = "Seed must be an integer.";
            return false;
        }

        if (!int.TryParse(CubesPerAxis, out var n) || n < 2 || n > 128)
        {
            error = "Cubes per axis must be an integer from 2 to 128.";
            return false;
        }

        if (!double.TryParse(CubeSize, out var size) || size <= 0)
        {
            error = "Cube size must be a positive number.";
            return false;
        }

        if (!int.TryParse(AsteroidCount, out var asteroids) || asteroids < 0 || asteroids > 200)
        {
            error = "Asteroid count must be 0–200.";
            return false;
        }

        if (!int.TryParse(AgentCount, out var agents) || agents < 0 || agents > 64)
        {
            error = "Agent count must be 0–64.";
            return false;
        }

        if (!double.TryParse(OrbitPeriodTicks, out var period) || period < 60)
        {
            error = "Orbit period must be at least 60 ticks.";
            return false;
        }

        if (!double.TryParse(StreamRadius, out var streamRadius) || streamRadius < 0)
        {
            error = "Stream radius must be a non-negative number.";
            return false;
        }

        if (!double.TryParse(SensorRange, out var sensor) || sensor < 0)
        {
            error = "Sensor range must be non-negative.";
            return false;
        }

        if (!double.TryParse(FieldOfView, out var fov) || fov < 35 || fov > 75)
        {
            error = "Field of view must be between 35° and 75°.";
            return false;
        }

        options = new WorldOptions
        {
            Title = Title.Trim(),
            CubesPerAxis = n,
            CubeSize = size,
            Kind = KindFromIndex(KindIndex),
            DriftAxis = DriftAxisIndex switch
            {
                1 => WorldAxis.Y,
                2 => WorldAxis.Z,
                _ => WorldAxis.X
            },
            StreamRadius = streamRadius,
            AsteroidCount = asteroids,
            AgentCount = agents,
            OrbitPeriodTicks = period,
            SensorRange = sensor,
            CommunicationRange = sensor * 2,
            FieldOfView = fov
        };
        return true;
    }

    private static WorldKind KindFromIndex(int index) => index switch
    {
        1 => WorldKind.ToroidalStream,
        2 => WorldKind.Empty,
        _ => WorldKind.PlanetoidBelt
    };

    private static int IndexFromKind(WorldKind kind) => kind switch
    {
        WorldKind.ToroidalStream => 1,
        WorldKind.Empty => 2,
        _ => 0
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
