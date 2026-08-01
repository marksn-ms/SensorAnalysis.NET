using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>Shared 8-connected steering helpers for search agents.</summary>
internal static class Steering
{
    public static AgentAction DirectionToward(Vec2 delta)
    {
        var ax = Math.Abs(delta.X);
        var ay = Math.Abs(delta.Y);
        if (ax < 1e-6 && ay < 1e-6) return AgentAction.Stay;

        var sx = Math.Sign(delta.X);
        var sy = Math.Sign(delta.Y);

        if (ax > ay * 2) return sx > 0 ? AgentAction.East : AgentAction.West;
        if (ay > ax * 2) return sy > 0 ? AgentAction.South : AgentAction.North;

        if (sx > 0 && sy > 0) return AgentAction.SouthEast;
        if (sx > 0 && sy < 0) return AgentAction.NorthEast;
        if (sx < 0 && sy > 0) return AgentAction.SouthWest;
        if (sx < 0 && sy < 0) return AgentAction.NorthWest;
        return AgentAction.Stay;
    }

    public static AgentAction RandomAction(Random rng)
    {
        var values = Enum.GetValues<AgentAction>();
        return values[rng.Next(values.Length)];
    }

    /// <summary>Clockwise tangent step about <paramref name="center"/> at preferred radius.</summary>
    public static AgentAction OrbitClockwise(Vec2 self, Vec2 center, double radius)
    {
        var offset = self - center;
        var dist = offset.Length;
        if (dist < 1e-6)
            return AgentAction.East;

        var radial = offset.Normalize();
        // Clockwise tangent in screen coords (Y down): (x,y) -> (-y, x) is CCW; (y, -x) is CW.
        var tangent = new Vec2(radial.Y, -radial.X);
        var desired = center + radial * radius + tangent * Math.Max(radius * 0.35, 4);
        return DirectionToward(desired - self);
    }

    /// <summary>
    /// Persist heading when free; otherwise pick a free-ish direction that increases clearance
    /// from neighbors inside personal space.
    /// </summary>
    public static AgentAction ExploreWithPersonalSpace(
        Vec2 self,
        AgentAction currentHeading,
        int persistTicksRemaining,
        IReadOnlyList<AgentInfo> neighbors,
        double personalSpace,
        Random rng,
        out AgentAction newHeading,
        out int newPersist)
    {
        bool Crowded(Vec2 at) =>
            neighbors.Any(n => Vec2.Distance(at, n.Position) < personalSpace);

        if (persistTicksRemaining > 0 && currentHeading != AgentAction.Stay)
        {
            var step = self + currentHeading.ToStep() * 5;
            if (!Crowded(step))
            {
                newHeading = currentHeading;
                newPersist = persistTicksRemaining - 1;
                return currentHeading;
            }
        }

        // Try random headings that reduce crowding.
        AgentAction best = RandomAction(rng);
        var bestScore = double.NegativeInfinity;
        for (int i = 0; i < 12; i++)
        {
            var h = RandomAction(rng);
            if (h == AgentAction.Stay) continue;
            var next = self + h.ToStep() * 5;
            var minDist = neighbors.Count == 0
                ? personalSpace
                : neighbors.Min(n => Vec2.Distance(next, n.Position));
            if (minDist > bestScore)
            {
                bestScore = minDist;
                best = h;
            }
        }

        newHeading = best;
        newPersist = 8;
        return best;
    }
}
