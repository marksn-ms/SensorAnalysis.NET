using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>Deployable factory for point incidents / events.</summary>
public sealed class IncidentDeployableFactory : IDeployableFactory
{
    /// <inheritdoc />
    public string Name => "Incident";

    /// <inheritdoc />
    public string Description => "Point incident / event.";

    /// <inheritdoc />
    public Type? ConfigType => null;

    /// <inheritdoc />
    public WorldObject Create(DeployableSpawnRequest request) => new Incident
    {
        Position = request.Position,
        Label = request.Label,
        IncidentType = ResolveIncidentType(request.Parameters),
        Intensity = 1.0
    };

    /// <inheritdoc />
    public void Install(SimWorld world, WorldObject obj)
    {
        var incident = (Incident)obj;
        incident.DrainPerAgentPerTick = world.Options.DrainPerAgentPerTick;
        world.Add(incident);
    }

    private static string ResolveIncidentType(IReadOnlyDictionary<string, object?> parameters)
    {
        // Legacy recipe keys only — not exposed in the UI.
        if (parameters.TryGetValue("Kind", out var kind) && kind is not null)
        {
            var s = kind.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        if (parameters.TryGetValue("IncidentType", out var legacy) && legacy is not null)
        {
            var s = legacy.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        return "Incident";
    }
}
