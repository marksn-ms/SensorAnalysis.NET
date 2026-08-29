using System.Reflection;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Reflection helpers for deployer ↔ deployable capability checks.</summary>
public static class DeployableCapabilities
{
    /// <summary>
    /// <c>true</c> when <paramref name="factory"/> produces objects usable as PlaceNear anchors
    /// (must expose a readable <c>SensorRange</c> and <c>Position</c>).
    /// </summary>
    /// <param name="factory">Deployable factory to probe.</param>
    /// <param name="reason">Human-readable failure reason when returning <c>false</c>.</param>
    /// <returns><c>true</c> when the factory's product can anchor PlaceNear placement.</returns>
    public static bool SupportsNearAnchor(IDeployableFactory factory, out string? reason)
    {
        WorldObject sample;
        try
        {
            sample = factory.Create(new DeployableSpawnRequest
            {
                Position = default,
                Label = "probe",
                Parameters = DeployableParameterCatalog.Defaults(factory.ConfigType),
                Rng = Random.Shared
            });
        }
        catch (Exception ex)
        {
            reason = $"Cannot probe '{factory.Name}': {ex.Message}";
            return false;
        }

        if (!HasReadable(sample, nameof(Agent.Position)) ||
            !HasReadable(sample, nameof(Agent.SensorRange)))
        {
            reason = $"'{factory.Name}' does not expose Position and SensorRange; cannot use as a PlaceNear anchor.";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool HasReadable(object target, string propertyName)
    {
        var prop = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return prop is not null && prop.CanRead;
    }
}
