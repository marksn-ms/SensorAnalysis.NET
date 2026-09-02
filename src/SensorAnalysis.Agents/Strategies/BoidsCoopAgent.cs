using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// Classic Reynolds boids cooperator using local neighbor heading and position
/// (align + cohere + separate), plus a seek term toward locally sensed incidents.
/// Distinct from PersonalSpaceCoop (P-only). No radio. Alignment is this family's
/// stimulus, not a new campaign channel.
/// </summary>
/// <remarks>
/// Weights are Reynolds-order constants (separate 1.5, align 1.0, cohere 1.0, seek 1.2),
/// not fit to ρ. Separation radius is r/4. If this loses to GreedyIncident, that is the
/// sheepdog test — do not retune.
/// </remarks>
public class BoidsCoopAgent : Agent
{
    /// <summary>Creates a boids cooperator.</summary>
    public BoidsCoopAgent()
    {
        AgentType = "BoidsCoop";
        Velocity = 5;
        ContributesToIncidentDrain = true;
    }

    /// <summary>Reynolds separation weight.</summary>
    public double WeightSeparate { get; set; } = 1.5;

    /// <summary>Reynolds alignment weight.</summary>
    public double WeightAlign { get; set; } = 1.0;

    /// <summary>Reynolds cohesion weight.</summary>
    public double WeightCohere { get; set; } = 1.0;

    /// <summary>Seek-incident weight when a local cue is visible.</summary>
    public double WeightSeek { get; set; } = 1.2;

    /// <summary>Separation disc (r/4). Smaller than personal-space r/2 so this is not the P channel.</summary>
    protected double SeparateRadius => SensorRange * 0.25;

    /// <summary>Sit-and-cover once this close to a sensed cue.</summary>
    protected double SitRadius => SensorRange * 0.35;

    /// <summary>
    /// Blend align/cohere/separate on local neighbors with optional cue-seek. Ignores radio.
    /// Does not read <see cref="AgentInfo.AgentType"/>.
    /// </summary>
    public override AgentAction Decide(AgentObservation observation)
    {
        return DecideWithNeighbors(observation, NeighborsForFlock(observation));
    }

    /// <summary>Peers used for align/cohere (all local peers for naïve).</summary>
    protected virtual IReadOnlyList<AgentInfo> NeighborsForFlock(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <summary>Peers used for separation (all local peers for naïve).</summary>
    protected virtual IReadOnlyList<AgentInfo> NeighborsForSeparate(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <summary>Boids + seek using the given flock/separate neighbor sets.</summary>
    protected AgentAction DecideWithNeighbors(
        AgentObservation observation,
        IReadOnlyList<AgentInfo> flock)
    {
        // Deliberately unused: observation.NearbyMessages, AgentInfo.AgentType.
        var separateNeighbors = NeighborsForSeparate(observation);

        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            if (observation.DistanceTo(nearest.Position) <= SitRadius)
                return AgentAction.Stay;
        }

        var steer = Vec2.Zero;

        var sep = Separate(observation, separateNeighbors);
        if (sep.Length > 1e-9)
            steer += sep.Normalize() * WeightSeparate;

        var ali = Align(flock);
        if (ali.Length > 1e-9)
            steer += ali.Normalize() * WeightAlign;

        var coh = Cohere(observation, flock);
        if (coh.Length > 1e-9)
            steer += coh.Normalize() * WeightCohere;

        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            var seek = observation.DisplacementTo(nearest.Position);
            if (seek.Length > 1e-9)
                steer += seek.Normalize() * WeightSeek;
        }

        if (steer.Length < 1e-9)
            return Steering.RandomAction(Rng);
        return Steering.DirectionToward(steer);
    }

    /// <summary>Repel from neighbors inside <see cref="SeparateRadius"/>.</summary>
    protected Vec2 Separate(AgentObservation observation, IReadOnlyList<AgentInfo> neighbors)
    {
        var acc = Vec2.Zero;
        var r = SeparateRadius;
        var n = 0;
        foreach (var p in neighbors)
        {
            var d = observation.DistanceTo(p.Position);
            if (d >= r || d < 1e-9) continue;
            var away = observation.DisplacementBetween(p.Position, Vec2.Zero);
            acc += away.Normalize() * ((r - d) / r);
            n++;
        }

        return n == 0 ? Vec2.Zero : acc;
    }

    /// <summary>Mean neighbor heading (LastAction unit step).</summary>
    protected static Vec2 Align(IReadOnlyList<AgentInfo> flock)
    {
        if (flock.Count == 0) return Vec2.Zero;
        var acc = Vec2.Zero;
        foreach (var p in flock)
            acc += p.Heading.ToStep();
        return acc;
    }

    /// <summary>Displacement to the centroid of flock positions (observer-relative).</summary>
    protected static Vec2 Cohere(AgentObservation observation, IReadOnlyList<AgentInfo> flock)
    {
        _ = observation;
        if (flock.Count == 0) return Vec2.Zero;
        var sx = 0.0;
        var sy = 0.0;
        foreach (var p in flock)
        {
            sx += p.Position.X;
            sy += p.Position.Y;
        }

        return new Vec2(sx / flock.Count, sy / flock.Count);
    }
}

/// <summary>Factory for <see cref="BoidsCoopAgent"/>.</summary>
public sealed class BoidsCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "BoidsCoop";

    /// <inheritdoc />
    public string Description =>
        "Classic boids (align+cohere+separate) on local neighbor heading/position, plus local cue-seek. No radio.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new BoidsCoopAgent { Label = label ?? "BD" };
}
