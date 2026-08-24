using System.ComponentModel;
using System.Runtime.CompilerServices;
using SensorAnalysis.Sphere.Core.Geometry;
using SensorAnalysis.Sphere.Core.Simulation;

namespace SensorAnalysis.Sphere.App.Views;

/// <summary>Editable New World options for the spherical platform.</summary>
public sealed class SphereWorldOptionsDialogViewModel : INotifyPropertyChanged
{
    private string _title = "Spherical world";
    private string _seed = "42";
    private string _radius = "1000";
    private string _sensorRange = "100";
    private string _communicationRange = "200";
    private string _resolution = "4";
    private string _stepSize = "1";
    private string _summary = string.Empty;
    private string _warning = string.Empty;
    private bool _canAccept = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>World title.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Deterministic seed text.</summary>
    public string Seed
    {
        get => _seed;
        set { _seed = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Sphere radius R.</summary>
    public string Radius
    {
        get => _radius;
        set { _radius = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Default sensor range r.</summary>
    public string SensorRange
    {
        get => _sensorRange;
        set { _sensorRange = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Default communication range c.</summary>
    public string CommunicationRange
    {
        get => _communicationRange;
        set { _communicationRange = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Geodesic resolution n.</summary>
    public string Resolution
    {
        get => _resolution;
        set { _resolution = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Default continuous step size.</summary>
    public string StepSize
    {
        get => _stepSize;
        set { _stepSize = value; OnPropertyChanged(); RefreshDerived(); }
    }

    /// <summary>Derived mesh and hop summary.</summary>
    public string Summary
    {
        get => _summary;
        private set { _summary = value; OnPropertyChanged(); }
    }

    /// <summary>Hop warning or validation message.</summary>
    public string Warning
    {
        get => _warning;
        private set { _warning = value; OnPropertyChanged(); }
    }

    /// <summary>Whether OK may be accepted.</summary>
    public bool CanAccept
    {
        get => _canAccept;
        private set { _canAccept = value; OnPropertyChanged(); }
    }

    /// <summary>Creates the view model and computes the initial derived summary.</summary>
    public SphereWorldOptionsDialogViewModel() => RefreshDerived();

    /// <summary>Validates the form and returns typed options when successful.</summary>
    public bool TryBuild(out SphericalWorldOptions options, out int seed, out string error)
    {
        options = default!;
        seed = 0;
        error = string.Empty;
        if (!TryParse(out var radius, out var sensorRange, out var communicationRange, out var resolution, out var stepSize, out seed, out error))
            return false;

        var sensorHops = SphericalWorld.EstimateRequiredHops(radius, resolution, sensorRange);
        var communicationHops = SphericalWorld.EstimateRequiredHops(radius, resolution, communicationRange);
        if (sensorHops > 5 || communicationHops > 5)
        {
            error = "r and c may require at most five dual-graph hops.";
            return false;
        }

        options = new SphericalWorldOptions
        {
            Radius = radius,
            Resolution = resolution,
            SensorRange = sensorRange,
            CommunicationRange = communicationRange,
            StepSize = stepSize
        };
        return true;
    }

    private void RefreshDerived()
    {
        if (!TryParse(out var radius, out var sensorRange, out var communicationRange, out var resolution, out var stepSize, out _, out var error))
        {
            Summary = error;
            Warning = string.Empty;
            CanAccept = false;
            return;
        }

        var faces = 20 * resolution * resolution;
        var sensorHops = SphericalWorld.EstimateRequiredHops(radius, resolution, sensorRange);
        var communicationHops = SphericalWorld.EstimateRequiredHops(radius, resolution, communicationRange);
        var edge = radius * GeodesicMesh.Create(resolution).CharacteristicEdgeRadians;
        Summary = $"resolution n={resolution} · {faces:N0} faces · ℓ≈{edge:F1} · step={stepSize} · r hops {sensorHops} · c hops {communicationHops}";
        Warning = sensorHops > 2 || communicationHops > 2
            ? "Warning: more than two hops reduces the efficiency of candidate search."
            : string.Empty;
        CanAccept = sensorHops <= 5 && communicationHops <= 5;
    }

    private bool TryParse(
        out double radius,
        out double sensorRange,
        out double communicationRange,
        out int resolution,
        out double stepSize,
        out int seed,
        out string error)
    {
        radius = sensorRange = communicationRange = stepSize = 0;
        resolution = seed = 0;
        error = string.Empty;
        if (!double.TryParse(Radius, out radius) || radius <= 0)
        {
            error = "R must be a positive number.";
            return false;
        }
        if (!double.TryParse(SensorRange, out sensorRange) || sensorRange < 0)
        {
            error = "r must be a non-negative number.";
            return false;
        }
        if (!double.TryParse(CommunicationRange, out communicationRange) || communicationRange < 0)
        {
            error = "c must be a non-negative number.";
            return false;
        }
        if (!int.TryParse(Resolution, out resolution) || resolution < 1)
        {
            error = "Geodesic resolution n must be an integer ≥ 1.";
            return false;
        }
        if (!double.TryParse(StepSize, out stepSize) || stepSize < 0)
        {
            error = "Step size must be a non-negative number.";
            return false;
        }
        if (!int.TryParse(Seed, out seed))
        {
            error = "Seed must be an integer.";
            return false;
        }
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
