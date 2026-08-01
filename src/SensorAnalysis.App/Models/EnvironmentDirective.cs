using CommunityToolkit.Mvvm.ComponentModel;

namespace SensorAnalysis.App.Models;

/// <summary>
/// A scheduled or applied environment change (typically a deployment) keyed by simulation tick.
/// Composed from registered factory/deployer names — no regime vocabulary.
/// </summary>
public partial class EnvironmentDirective : ObservableObject
{
    private static int _nextId;

    /// <summary>Stable id for list identity and RNG salt.</summary>
    public int Id { get; } = Interlocked.Increment(ref _nextId);

    /// <summary>Tick at which this directive should run (0 = initial population).</summary>
    [ObservableProperty] private long _atTick;

    /// <summary>Registered <see cref="Core.Deployment.IDeployer"/> name.</summary>
    [ObservableProperty] private string _deployerName = "Gaussian";

    /// <summary>Total agents to place (honest + adversaries), matching CLI <c>DeployRequest.AgentCount</c>.</summary>
    [ObservableProperty] private int _agentCount = 10;

    /// <summary>Honest / primary agent factory name.</summary>
    [ObservableProperty] private string _agentTypeName = "OrbitingCoop";

    /// <summary>Number of adversary slots within <see cref="AgentCount"/>.</summary>
    [ObservableProperty] private int _adversaryCount;

    /// <summary>Adversary factory name when <see cref="AdversaryCount"/> &gt; 0.</summary>
    [ObservableProperty] private string _adversaryTypeName = "HerdingDeceptive";

    /// <summary>Incidents to place with this directive.</summary>
    [ObservableProperty] private int _incidentCount = 5;

    /// <summary>Sensor range assigned to placed agents.</summary>
    [ObservableProperty] private double _sensorRange = 80;

    /// <summary>Gaussian sigma (ignored by Random deployer).</summary>
    [ObservableProperty] private double _deploySigma = 80;

    /// <summary>True after the directive has been executed in the current world session.</summary>
    [ObservableProperty] private bool _isApplied;

    /// <summary>Last error message if apply failed; otherwise empty.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>One-line summary for the directive list.</summary>
    public string Summary
    {
        get
        {
            var adv = AdversaryCount > 0 ? $" + {AdversaryCount}×{AdversaryTypeName}" : "";
            var honest = Math.Max(0, AgentCount - AdversaryCount);
            return $"t={AtTick}: {DeployerName} → {honest}×{AgentTypeName}{adv}, {IncidentCount} incidents";
        }
    }

    partial void OnAtTickChanged(long value) => OnPropertyChanged(nameof(Summary));
    partial void OnDeployerNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnAgentCountChanged(int value) => OnPropertyChanged(nameof(Summary));
    partial void OnAgentTypeNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnAdversaryCountChanged(int value) => OnPropertyChanged(nameof(Summary));
    partial void OnAdversaryTypeNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnIncidentCountChanged(int value) => OnPropertyChanged(nameof(Summary));
}
