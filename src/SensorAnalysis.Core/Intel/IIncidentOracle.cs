using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Intel;

/// <summary>
/// Privileged view of active incidents. Injected into deceptive agents so omniscience
/// is an explicit capability, not hard-coded agent IDs inside the world loop.
/// </summary>
public interface IIncidentOracle
{
    /// <summary>Returns currently active (non-depleted) incidents.</summary>
    IReadOnlyList<IncidentInfo> GetActiveIncidents(SimWorld world);
}

/// <summary>Oracle backed by the live <see cref="SimWorld"/> incident list.</summary>
public sealed class WorldIncidentOracle : IIncidentOracle
{
    /// <inheritdoc />
    public IReadOnlyList<IncidentInfo> GetActiveIncidents(SimWorld world) =>
        world.ActiveIncidents
            .Select(i => new IncidentInfo(i.Id, i.Position, i.Intensity))
            .ToList();
}
