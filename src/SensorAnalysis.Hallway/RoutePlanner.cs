using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Hallway;

/// <summary>
/// Door-to-door tends for one student. The plan is geometry. The genome only
/// corrects the heading that follows these points.
/// </summary>
public static class RoutePlanner
{
    /// <summary>
    /// Waypoints from the origin doorway, along a right-hand lane, to the destination room center.
    /// </summary>
    public static IReadOnlyList<Vec2> Plan(FloorPlan plan, int originIndex, int destinationIndex, double bodyRadius)
    {
        var origin = plan.Rooms[originIndex];
        var dest = plan.Rooms[destinationIndex];
        var into = Math.Max(12, bodyRadius * 2);
        var originApproach = Approach(origin, into);
        var destApproach = Approach(dest, into);
        var travel = Math.Sign(destApproach.X - originApproach.X);
        if (travel == 0)
            travel = 1;
        var laneY = plan.HallCenterY + travel * (plan.Spec.HallWidth * 0.25);

        var points = new List<Vec2>
        {
            origin.Door,
            originApproach,
            new Vec2(originApproach.X, laneY),
            new Vec2(destApproach.X, laneY),
            destApproach,
            dest.Door,
            dest.Center
        };

        var kept = new List<Vec2> { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            if ((points[i] - kept[^1]).Length >= 8)
                kept.Add(points[i]);
        }

        if ((kept[^1] - dest.Center).Length >= 1)
            kept.Add(dest.Center);
        return kept;
    }

    private static Vec2 Approach(Classroom room, double into)
    {
        var sign = room.North ? 1.0 : -1.0;
        return new Vec2(room.Door.X, room.Door.Y + sign * into);
    }
}
