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
        return PlaceNearAgent(world, rng, host, rangeFactor);
    }

    /// <summary>
    /// Place near a specific living host. Radius is
    /// <paramref name="rangeFactor"/> × host <see cref="Agent.SensorRange"/>
    /// (with light jitter).
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="rng">Deterministic RNG.</param>
    /// <param name="host">Agent to place near.</param>
    /// <param name="rangeFactor">Fraction of host sensor range (e.g. 0.5).</param>
    public static Vec2 PlaceNearAgent(
        SimWorld world,
        Random rng,
        Agent host,
        double rangeFactor)
    {
        var factor = Math.Max(0.0, rangeFactor);
        // Mild radial jitter so stacked waves are not identical.
        var radius = host.SensorRange * factor * (0.85 + 0.3 * rng.NextDouble());
        var angle = rng.NextDouble() * Math.PI * 2;
        return world.Wrap(new Vec2(
            host.Position.X + Math.Cos(angle) * radius,
            host.Position.Y + Math.Sin(angle) * radius));
    }

    /// <summary>
    /// True when <paramref name="host"/> has no active incident inside the
    /// PlaceNear jitter radius (rangeFactor × sensor range × 1.15).
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="host">Candidate host.</param>
    /// <param name="rangeFactor">Same factor used for placement.</param>
    public static bool HostIsFreeOfIncidents(
        SimWorld world,
        Agent host,
        double rangeFactor)
    {
        var radius = host.SensorRange * Math.Max(0.0, rangeFactor) * 1.15;
        return !world.Incidents.Any(i =>
            !i.IsDepleted && world.Distance(i.Position, host.Position) <= radius);
    }
}
