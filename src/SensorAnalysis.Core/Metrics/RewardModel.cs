using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Metrics;

/// <summary>
/// Configurable reward terms inspired by the classic platform
/// (incident proximity, agent crowding, optional coverage bonus).
/// </summary>
public sealed class RewardModel
{
    /// <summary>Weight for proximity to active incidents (P_e style).</summary>
    public double ScaleIncidentProximity { get; set; } = 1.0;

    /// <summary>Penalty weight for having more than MaxComfortNeighbors nearby agents.</summary>
    public double ScaleCrowding { get; set; } = 0.05;

    /// <summary>Neighbors above this count start to incur crowding penalty.</summary>
    public int MaxComfortNeighbors { get; set; } = 2;

    /// <summary>Small bonus for being the first to cover a tile this trial.</summary>
    public double ScaleCoverageBonus { get; set; } = 0.02;

    /// <summary>
    /// Computes the instantaneous reward for <paramref name="self"/> given its observation and world state.
    /// </summary>
    /// <param name="self">The agent being scored.</param>
    /// <param name="obs">Local observation used for proximity and crowding terms.</param>
    /// <param name="world">Full world (needed for coverage bonus via tile state).</param>
    /// <returns>Scalar reward for this tick.</returns>
    public double Evaluate(Agent self, AgentObservation obs, SimWorld world)
    {
        double reward = 0;

        // Incident proximity: higher intensity / closer → more reward
        foreach (var inc in obs.NearbyIncidents)
        {
            var dist = Math.Max(1.0, obs.DistanceTo(inc.Position));
            reward += ScaleIncidentProximity * (inc.Intensity / dist);
        }

        // Crowding penalty
        var excess = Math.Max(0, obs.NearbyAgents.Count - MaxComfortNeighbors);
        reward -= ScaleCrowding * excess;

        // Coverage bonus: tile not covered before this tick
        var tile = world.TileAt(obs.SelfPosition);
        if (tile.LastCoveredTick < 0)
            reward += ScaleCoverageBonus;

        return reward;
    }
}
