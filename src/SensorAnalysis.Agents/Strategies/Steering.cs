using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>Shared 16-point compass steering helpers for search agents.</summary>
internal static class Steering
{
    public static AgentAction DirectionToward(Vec2 delta) => AgentActionExtensions.NearestTo(delta);

    public static AgentAction RandomAction(Random rng) =>
        AgentActionExtensions.MovementDirections[rng.Next(AgentActionExtensions.MovementDirections.Length)];

    /// <summary>
    /// Correlated compass walk: usually continue forward, sometimes turn slightly,
    /// almost never reverse or stay. Shared by RandomWalk, RandomWalkNoDrain, and
    /// HerdingDeceptive idle motion.
    /// </summary>
    public static AgentAction CorrelatedWalk(Random rng, ref int heading, ref bool initialized)
    {
        var compass = AgentActionExtensions.MovementDirections;
        if (!initialized)
        {
            heading = rng.Next(compass.Length);
            initialized = true;
        }

        var roll = rng.NextDouble();
        // Cumulative masses (must sum to 1):
        // forward 0.62, ±45° 0.15×2, ±90° 0.035×2, ±135° 0.004×2, reverse 0.002, stay 0.004
        int delta;
        if (roll < 0.62)
            delta = 0;
        else if (roll < 0.77)
            delta = 1;
        else if (roll < 0.92)
            delta = -1;
        else if (roll < 0.955)
            delta = 2;
        else if (roll < 0.99)
            delta = -2;
        else if (roll < 0.994)
            delta = 3;
        else if (roll < 0.998)
            delta = -3;
        else if (roll < 0.999)
            delta = compass.Length / 2;
        else
            return AgentAction.Stay;

        heading = ((heading + delta) % compass.Length + compass.Length) % compass.Length;
        return compass[heading];
    }

    /// <summary>Random heading including <see cref="AgentAction.Stay"/>.</summary>
    public static AgentAction RandomActionIncludingStay(Random rng)
    {
        var values = Enum.GetValues<AgentAction>();
        return values[rng.Next(values.Length)];
    }

    /// <summary>Clockwise tangent step about a relative center at preferred radius.</summary>
    public static AgentAction OrbitClockwise(Vec2 centerRelative, double radius, AgentObservation observation) =>
        OrbitAround(centerRelative, radius, observation, clockwise: true);

    /// <summary>Clockwise or counterclockwise orbit step about a relative center.</summary>
    public static AgentAction OrbitAround(
        Vec2 centerRelative,
        double radius,
        AgentObservation observation,
        bool clockwise)
    {
        var offset = observation.DisplacementBetween(centerRelative, Vec2.Zero);
        var dist = offset.Length;
        if (dist < 1e-6)
            return clockwise ? AgentAction.East : AgentAction.West;

        var radial = offset.Normalize();
        var tangent = clockwise ? new Vec2(radial.Y, -radial.X) : new Vec2(-radial.Y, radial.X);
        var desired = centerRelative + radial * radius + tangent * Math.Max(radius * 0.35, 4);
        return DirectionToward(observation.DisplacementTo(desired));
    }

    /// <summary>
    /// Stay on the orbit ring. Prefer clockwise; reverse if that step is clearly more open.
    /// Never yields with <see cref="AgentAction.Stay"/> — that is what made co-orbiters hitch.
    /// </summary>
    public static AgentAction OrbitRespectingPeers(
        Vec2 centerRelative,
        double radius,
        IReadOnlyList<AgentInfo> neighbors,
        double moveSpeed,
        AgentObservation observation)
    {
        var cw = OrbitAround(centerRelative, radius, observation, clockwise: true);
        if (neighbors.Count == 0)
            return cw;

        var ccw = OrbitAround(centerRelative, radius, observation, clockwise: false);
        double Clearance(AgentAction action)
        {
            var next = action.ToStep() * moveSpeed;
            return neighbors.Min(n => observation.DistanceBetween(next, n.Position));
        }

        return Clearance(ccw) > Clearance(cw) + 1e-6 ? ccw : cw;
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
    /// Walk toward a target while sliding around yielded peers. Never Stay: that is
    /// what made a recruiter look like a repulsive obstacle instead of a join cue.
    /// </summary>
    public static AgentAction ApproachRespectingPeers(
        Vec2 targetRelative,
        IReadOnlyList<AgentInfo> neighbors,
        double personalSpace,
        double moveSpeed,
        AgentObservation observation,
        Func<AgentInfo, bool> shouldYieldTo)
    {
        var preferred = DirectionToward(observation.DisplacementTo(targetRelative));
        bool Violates(AgentAction action)
        {
            if (neighbors.Count == 0 || personalSpace <= 0)
                return false;
            var step = action.ToStep() * moveSpeed;
            foreach (var peer in neighbors)
            {
                if (!shouldYieldTo(peer))
                    continue;
                if (observation.DistanceBetween(step, peer.Position) < personalSpace)
                    return true;
            }
            return false;
        }

        if (!Violates(preferred))
            return preferred;

        AgentAction best = preferred;
        var bestDist = double.PositiveInfinity;
        var found = false;
        foreach (var action in AgentActionExtensions.MovementDirections)
        {
            if (Violates(action))
                continue;
            var next = action.ToStep() * moveSpeed;
            var d = observation.DistanceBetween(next, targetRelative);
            if (d < bestDist)
            {
                bestDist = d;
                best = action;
                found = true;
            }
        }

        if (found)
            return best;

        var yielders = neighbors.Where(shouldYieldTo).ToList();
        if (yielders.Count == 0)
            return preferred;

        var bestClear = double.NegativeInfinity;
        foreach (var action in AgentActionExtensions.MovementDirections)
        {
            var next = action.ToStep() * moveSpeed;
            var clear = yielders.Min(n => observation.DistanceBetween(next, n.Position));
            if (clear > bestClear)
            {
                bestClear = clear;
                best = action;
            }
        }

        return best;
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
