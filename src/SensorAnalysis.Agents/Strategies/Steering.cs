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
}
