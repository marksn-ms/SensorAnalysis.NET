namespace SensorAnalysis.Sphere.Core.Simulation;

/// <summary>Actions that may be scheduled into a spherical world at a simulation tick.</summary>
public enum SphericalDirectiveKind
{
    /// <summary>Deploy agents uniformly across the continuous surface.</summary>
    DeployUniformAgents,

    /// <summary>Deploy agents near host agent zero.</summary>
    DeployNearHost,

    /// <summary>Register an ongoing periodic incident deployer.</summary>
    DeployPeriodicIncidents,

    /// <summary>Enable the coverage watcher metric sampler.</summary>
    EnableCoverageWatcher
}

/// <summary>A tick-scheduled world modification configured from the App deployment panel.</summary>
public sealed record SphericalDirective(
    long Tick,
    SphericalDirectiveKind Kind,
    int Count = 1,
    SphericalAgentKind AgentKind = SphericalAgentKind.CooperativeSearch,
    double? SensorRange = null,
    double? CommunicationRange = null,
    double? StepSize = null,
    int? PeriodTicks = null,
    double? RangeFactor = null,
    string? IncidentType = null)
{
    /// <inheritdoc />
    public override string ToString()
    {
        var detail = Kind switch
        {
            SphericalDirectiveKind.DeployUniformAgents =>
                $"{Count} {AgentKind} (r={SensorRange?.ToString("0.###") ?? "default"}, c={CommunicationRange?.ToString("0.###") ?? "default"})",
            SphericalDirectiveKind.DeployNearHost =>
                $"{Count} {AgentKind} near #0",
            SphericalDirectiveKind.DeployPeriodicIncidents =>
                $"{Count} {IncidentType ?? "StationaryIncident"} every {PeriodTicks ?? 20}",
            SphericalDirectiveKind.EnableCoverageWatcher => "coverage watcher",
            _ => Kind.ToString()
        };
        return $"t{Tick}: {detail}";
    }
}
