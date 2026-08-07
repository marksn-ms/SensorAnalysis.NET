using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Shared helpers for placing points uniformly or near living agents of a given type.
/// </summary>
public static class IncidentPlacementHelper
{
    /// <summary>Uniform random point in the world.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="rng">Deterministic RNG.</param>
    public static Vec2 PlaceUniform(SimWorld world, Random rng) =>
        new(rng.NextDouble() * world.WorldWidthPx, rng.NextDouble() * world.WorldHeightPx);

    /// <summary>
    /// Place near a random agent whose <see cref="Agent.AgentType"/> matches
    /// <paramref name="nearTypeName"/> (ordinal ignore-case). Radius is
    /// <paramref name="rangeFactor"/> × host <see cref="Agent.SensorRange"/>
    /// (with light jitter). Falls back to uniform when no hosts match.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="rng">Deterministic RNG.</param>
    /// <param name="nearTypeName">Host agent type name.</param>
    /// <param name="rangeFactor">Fraction of host sensor range (e.g. 0.5).</param>
    public static Vec2 PlaceNearType(
        SimWorld world,
        Random rng,
        string nearTypeName,
        double rangeFactor)
    {
        var hosts = world.Agents
            .Where(a => string.Equals(a.AgentType, nearTypeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hosts.Count == 0)
            return PlaceUniform(world, rng);

        var host = hosts[rng.Next(hosts.Count)];
        var factor = Math.Max(0.0, rangeFactor);
        // Mild radial jitter so stacked waves are not identical.
        var radius = host.SensorRange * factor * (0.85 + 0.3 * rng.NextDouble());
        var angle = rng.NextDouble() * Math.PI * 2;
        return world.Wrap(new Vec2(
            host.Position.X + Math.Cos(angle) * radius,
            host.Position.Y + Math.Sin(angle) * radius));
    }
}
