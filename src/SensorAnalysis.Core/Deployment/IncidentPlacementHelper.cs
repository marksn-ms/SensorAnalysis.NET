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
    /// Near-host jitter radius (rangeFactor × sensor range × 1.15).
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

    /// <summary>Positions of all living agents and active incidents.</summary>
    /// <param name="world">Target world.</param>
    public static List<Vec2> CollectOccupiedPositions(SimWorld world)
    {
        var positions = new List<Vec2>(world.Agents.Count + world.Incidents.Count);
        foreach (var agent in world.Agents)
            positions.Add(agent.Position);
        foreach (var incident in world.Incidents)
        {
            if (!incident.IsDepleted)
                positions.Add(incident.Position);
        }

        return positions;
    }

    /// <summary>
    /// Samples a position via <paramref name="candidateFactory"/> until it is at least
    /// <paramref name="minDistance"/> from every point in <paramref name="avoid"/>,
    /// or <see cref="IDeployer.MaxPlacementRetries"/> attempts are exhausted.
    /// </summary>
    /// <param name="world">Target world (toroidal distance).</param>
    /// <param name="rng">Deterministic RNG.</param>
    /// <param name="candidateFactory">Produces a candidate position each attempt.</param>
    /// <param name="minDistance">Minimum separation (0 disables and returns the first candidate).</param>
    /// <param name="avoid">Existing and co-wave positions to respect.</param>
    public static Vec2 PlaceWithMinSpacing(
        SimWorld world,
        Random rng,
        Func<Vec2> candidateFactory,
        double minDistance,
        IReadOnlyList<Vec2> avoid)
    {
        if (minDistance <= 0 || avoid.Count == 0)
            return candidateFactory();

        var minDist = minDistance;
        var attempts = Math.Max(1, IDeployer.MaxPlacementRetries);
        Vec2 fallback = candidateFactory();
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var candidate = attempt == 0 ? fallback : candidateFactory();
            if (attempt == 0)
                fallback = candidate;

            if (IsSpaced(world, candidate, avoid, minDist))
                return candidate;
        }

        return fallback;
    }

    private static bool IsSpaced(
        SimWorld world,
        Vec2 candidate,
        IReadOnlyList<Vec2> avoid,
        double minDistance)
    {
        foreach (var other in avoid)
        {
            if (world.Distance(candidate, other) < minDistance)
                return false;
        }

        return true;
    }
}
