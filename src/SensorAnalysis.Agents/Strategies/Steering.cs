using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>Shared 16-point compass steering helpers for search agents.</summary>
internal static class Steering
{
    public static AgentAction DirectionToward(Vec2 delta) => AgentActionExtensions.NearestTo(delta);

    public static AgentAction RandomAction(Random rng) =>
        AgentActionExtensions.MovementDirections[rng.Next(AgentActionExtensions.MovementDirections.Length)];

    /// <summary>Random heading including <see cref="AgentAction.Stay"/>.</summary>
    public static AgentAction RandomActionIncludingStay(Random rng)
    {
        var values = Enum.GetValues<AgentAction>();
        return values[rng.Next(values.Length)];
    }

    /// <summary>Clockwise tangent step about a relative center at preferred radius.</summary>
    public static AgentAction OrbitClockwise(Vec2 centerRelative, double radius, AgentObservation observation)
    {
        var offset = observation.DisplacementBetween(centerRelative, Vec2.Zero);
        var dist = offset.Length;
        if (dist < 1e-6)
            return AgentAction.East;

        var radial = offset.Normalize();
        var tangent = new Vec2(radial.Y, -radial.X);
        var desired = centerRelative + radial * radius + tangent * Math.Max(radius * 0.35, 4);
        return DirectionToward(observation.DisplacementTo(desired));
    }

    /// <summary>
    /// Keeps a task-preferred heading unless the agent must yield personal space to a peer.
    /// Yields with <see cref="AgentAction.Stay"/> when <paramref name="shouldYieldTo"/> says so
    /// and the peer is inside <paramref name="personalSpace"/> or the step would enter it.
    /// </summary>
    public static AgentAction RespectPersonalSpace(
        AgentAction preferred,
        IReadOnlyList<AgentInfo> neighbors,
        double personalSpace,
        double moveSpeed,
        AgentObservation observation,
        Func<AgentInfo, bool> shouldYieldTo)
    {
        if (neighbors.Count == 0 || personalSpace <= 0)
            return preferred;

        var step = preferred.ToStep() * moveSpeed;
        foreach (var peer in neighbors)
        {
            if (!shouldYieldTo(peer))
                continue;

            var distNow = observation.DistanceTo(peer.Position);
            var distAfter = observation.DistanceBetween(step, peer.Position);
            if (distNow < personalSpace || distAfter < personalSpace)
                return AgentAction.Stay;
        }

        return preferred;
    }

    /// <summary>
    /// Persist heading when free; otherwise pick a direction that increases clearance
    /// from neighbors inside personal space.
    /// </summary>
    public static AgentAction ExploreWithPersonalSpace(
        AgentAction currentHeading,
        int persistTicksRemaining,
        IReadOnlyList<AgentInfo> neighbors,
        double personalSpace,
        double moveSpeed,
        AgentObservation observation,
        Random rng,
        int headingChangeInterval,
        out AgentAction newHeading,
        out int newPersist)
    {
        bool Crowded(Vec2 at) =>
            neighbors.Any(n => observation.DistanceBetween(at, n.Position) < personalSpace);

        if (persistTicksRemaining > 0 && currentHeading != AgentAction.Stay)
        {
            var step = currentHeading.ToStep() * moveSpeed;
            if (!Crowded(step))
            {
                newHeading = currentHeading;
                newPersist = persistTicksRemaining - 1;
                return currentHeading;
            }
        }

        AgentAction best = RandomAction(rng);
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 12; i++)
        {
            var h = RandomAction(rng);
            if (h == AgentAction.Stay) continue;
            var next = h.ToStep() * moveSpeed;
            var minDist = neighbors.Count == 0
                ? personalSpace
                : neighbors.Min(n => observation.DistanceBetween(next, n.Position));
            if (minDist > bestScore)
            {
                bestScore = minDist;
                best = h;
            }
        }

        newHeading = best;
        newPersist = headingChangeInterval;
        return best;
    }
}
