using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Limited-range Lloyd / Voronoi cooperator. No radio.
/// <para>
/// Stimulus (local): neighbor positions determine a discrete Voronoi partition of the
/// sensing disc; locally sensed incidents in this agent's cell are the coverage task.
/// Motion: sit/seek the centroid of <em>owned</em> cues; otherwise move toward the
/// sampled centroid of the free Voronoi cell (spread). Distinct from PersonalSpaceCoop
/// (P-only yield) and from boids cohere (family 3).
/// </para>
/// </summary>
public class LloydCoopAgent : Agent
{
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;

    /// <summary>Creates a limited-range Lloyd cooperator.</summary>
    public LloydCoopAgent()
    {
        AgentType = "LloydCoop";
        Velocity = 5;
        ContributesToIncidentDrain = true;
    }

    /// <summary>Sit-and-cover once this close to the owned-cue centroid.</summary>
    protected double SitRadius => SensorRange * 0.35;

    /// <summary>Owned cues on the last Decide (measurement).</summary>
    public int LastOwnedCueCount { get; private set; }

    /// <summary>Sensed cues given to a closer neighbor on the last Decide (measurement).</summary>
    public int LastRelinquishedCueCount { get; private set; }

    /// <summary>
    /// Voronoi-owns locally sensed incidents using neighbor positions, then moves toward
    /// the owned-cue centroid or the free-cell centroid. Ignores radio. Does not read
    /// <see cref="AgentInfo.AgentType"/>.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        var owned = new List<IncidentInfo>();
        var neighbors = observation.NearbyAgents;
        LastRelinquishedCueCount = 0;
        foreach (var cue in observation.NearbyIncidents)
        {
            var dSelf = observation.DistanceTo(cue.Position);
            var stolen = false;
            foreach (var n in neighbors)
            {
                if (observation.DistanceBetween(n.Position, cue.Position) < dSelf)
                {
                    stolen = true;
                    break;
                }
            }

            if (stolen) LastRelinquishedCueCount++;
            else owned.Add(cue);
        }

        LastOwnedCueCount = owned.Count;
        if (owned.Count > 0)
        {
            var cx = 0.0;
            var cy = 0.0;
            foreach (var c in owned)
            {
                cx += c.Position.X;
                cy += c.Position.Y;
            }

            var centroid = new Vec2(cx / owned.Count, cy / owned.Count);
            if (observation.DistanceTo(centroid) <= SitRadius)
                return AgentAction.Stay;
            return Steering.DirectionToward(observation.DisplacementTo(centroid));
        }

        var cell = ApproximateVoronoiCentroid(observation, neighbors, SensorRange);
        if (cell is { } target)
            return Steering.DirectionToward(observation.DisplacementTo(target));
        return Explore();
    }

    /// <summary>
    /// Sample the sensing disc; keep points closer to self than to any neighbor; return
    /// their centroid in observer-relative coordinates. Null if the disc is fully claimed.
    /// </summary>
    protected static Vec2? ApproximateVoronoiCentroid(
        AgentObservation observation,
        IReadOnlyList<AgentInfo> neighbors,
        double sensingRange)
    {
        var sx = 0.0;
        var sy = 0.0;
        var nKeep = 0;
        const int rings = 3;
        const int spokes = 8;
        for (int ring = 1; ring <= rings; ring++)
        {
            var rad = sensingRange * (ring / (double)(rings + 1));
            for (int k = 0; k < spokes; k++)
            {
                var ang = 2 * Math.PI * k / spokes;
                var p = new Vec2(rad * Math.Cos(ang), rad * Math.Sin(ang));
                var dSelf = p.Length;
                var closerNeighbor = false;
                foreach (var nb in neighbors)
                {
                    if (observation.DistanceBetween(nb.Position, p) < dSelf)
                    {
                        closerNeighbor = true;
                        break;
                    }
                }

                if (closerNeighbor) continue;
                sx += p.X;
                sy += p.Y;
                nKeep++;
            }
        }

        if (nKeep == 0) return null;
        return new Vec2(sx / nKeep, sy / nKeep);
    }

    /// <summary>Persistent random explore when the Voronoi cell sample is empty.</summary>
    protected AgentAction Explore()
    {
        if (_explorePersist <= 0 || _exploreHeading == AgentAction.Stay)
        {
            _exploreHeading = Steering.RandomAction(Rng);
            if (_exploreHeading == AgentAction.Stay)
                _exploreHeading = AgentAction.East;
            _explorePersist = HeadingChangeInterval;
        }

        _explorePersist--;
        return _exploreHeading;
    }
}

/// <summary>Factory for <see cref="LloydCoopAgent"/>.</summary>
public sealed class LloydCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "LloydCoop";

    /// <inheritdoc />
    public string Description =>
        "Limited-range Lloyd: cover centroid of Voronoi-owned local cues; else spread to free-cell centroid. No radio.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new LloydCoopAgent { Label = label ?? "LL" };
}
