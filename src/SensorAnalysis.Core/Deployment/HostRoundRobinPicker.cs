using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Cycles a shuffled host list, preferring hosts that do not already cover an incident.
/// </summary>
public sealed class HostRoundRobinPicker
{
    private int[]? _hostOrder;
    private int _hostCursor;

    /// <summary>Place near the next eligible host of <paramref name="nearTypeName"/>.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="rng">Deterministic RNG for shuffle and fallback.</param>
    /// <param name="nearTypeName">Host agent type name.</param>
    /// <param name="rangeFactor">Fraction of host sensor range.</param>
    /// <returns>A point near the chosen host, or uniform if no hosts match.</returns>
    public Vec2 Next(SimWorld world, Random rng, string nearTypeName, double rangeFactor)
    {
        var hosts = world.Agents
            .Where(a => string.Equals(a.AgentType, nearTypeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hosts.Count == 0)
            return IncidentPlacementHelper.PlaceUniform(world, rng);

        EnsureHostOrder(hosts, rng);
        var free = hosts.Where(h =>
            IncidentPlacementHelper.HostIsFreeOfIncidents(world, h, rangeFactor)).ToList();
        var pool = free.Count > 0 ? free : hosts;

        var host = NextHostInOrder(pool) ?? pool[rng.Next(pool.Count)];
        return IncidentPlacementHelper.PlaceNearAgent(world, rng, host, rangeFactor);
    }

    /// <summary>Forget the shuffled order (call when attaching a new trial).</summary>
    public void Reset()
    {
        _hostOrder = null;
        _hostCursor = 0;
    }

    private void EnsureHostOrder(List<Agent> hosts, Random rng)
    {
        var ids = hosts.Select(h => h.Id).OrderBy(id => id).ToArray();
        if (_hostOrder is not null &&
            _hostOrder.Length == ids.Length &&
            _hostOrder.OrderBy(id => id).SequenceEqual(ids))
            return;

        _hostOrder = hosts.Select(h => h.Id).ToArray();
        for (int i = _hostOrder.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_hostOrder[i], _hostOrder[j]) = (_hostOrder[j], _hostOrder[i]);
        }
        _hostCursor = 0;
    }

    private Agent? NextHostInOrder(List<Agent> pool)
    {
        if (_hostOrder is null || _hostOrder.Length == 0 || pool.Count == 0)
            return null;

        for (int n = 0; n < _hostOrder.Length; n++)
        {
            var id = _hostOrder[(_hostCursor + n) % _hostOrder.Length];
            var host = pool.FirstOrDefault(h => h.Id == id);
            if (host is null) continue;
            _hostCursor = (_hostCursor + n + 1) % _hostOrder.Length;
            return host;
        }

        return null;
    }
}
