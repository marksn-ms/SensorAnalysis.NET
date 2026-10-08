using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Hallway;

/// <summary>
/// Follows a waypoint plan and applies a bounded steering correction from windshield cues.
/// One genome is shared by every student in the trial.
/// </summary>
public sealed class HallwayStudentAgent : Agent
{
    private int _waypoint;

    /// <summary>Creates a student. The floor-plan deployer fills the route and the genome.</summary>
    public HallwayStudentAgent()
    {
        AgentType = "HallwayStudent";
        Velocity = 6;
        ContributesToIncidentDrain = false;
        ActionIntervalBase = 8;
    }

    /// <summary>Classroom the student starts in.</summary>
    public int OriginRoom { get; init; }

    /// <summary>Classroom the student is trying to enter.</summary>
    public int DestinationRoom { get; init; }

    /// <summary>Personal radius used for proximity penalties. The engine body radius is separate.</summary>
    public double BodyRadius { get; init; } = 8;

    /// <summary>Sensor wrapper. Default: eye glyphs.</summary>
    public PassageDeviceKind Device { get; init; } = PassageDeviceKind.Eye;

    /// <summary>Shared genome weights for this trial.</summary>
    public double[] Weights { get; set; } = HallwayPopulation.Prior.ToArray();

    /// <summary>Absolute waypoints. The last one is the destination room center.</summary>
    public IReadOnlyList<Vec2> Waypoints { get; set; } = Array.Empty<Vec2>();

    /// <summary>Distance to the goal at the moment the student was placed.</summary>
    public double InitialGoalDistance { get; set; } = 1;

    /// <summary>Current facing. Updated when a heading is chosen.</summary>
    public Vec2 Heading { get; private set; } = new(1, 0);

    /// <summary>Path length accumulated after each move.</summary>
    public double DistanceTraveled { get; private set; }

    /// <summary>Ticks spent closer to a wall than <see cref="BodyRadius"/>.</summary>
    public int WallViolations { get; private set; }

    /// <summary>Ticks spent closer to another student than twice <see cref="BodyRadius"/>.</summary>
    public int AgentViolations { get; private set; }

    /// <summary>True once the student has reached the last waypoint.</summary>
    public bool Arrived { get; private set; }

    /// <summary>Team member fitness for the current tick.</summary>
    public double Fitness { get; private set; }

    /// <summary>Point the student at its first waypoint before the first decision.</summary>
    public void SeedHeading()
    {
        if (Waypoints.Count == 0)
            return;
        var delta = Waypoints[0] - Position;
        if (delta.Length > 1e-6)
            Heading = delta.Normalize();
    }

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        if (Waypoints.Count == 0)
            return AgentAction.Stay;

        var capture = Math.Max(18, BodyRadius * 2);
        if ((Waypoints[^1] - Position).Length <= capture)
            Arrived = true;
        if (Arrived)
            return AgentAction.Stay;

        var advance = Math.Max(14, BodyRadius * 1.5);
        while (_waypoint < Waypoints.Count - 1 && (Waypoints[_waypoint] - Position).Length < advance)
            _waypoint++;

        var goal = Waypoints[_waypoint] - Position;
        if (goal.Length < 1e-6)
            return AgentAction.Stay;

        var goalDir = goal.Normalize();
        var weights = Weights.Length >= 7 ? Weights : HallwayPopulation.Prior;
        var cues = PassageDevices.Sense(Device, this, observation);
        var avoid = Vec2.Zero;
        var maxUrgency = 0.0;
        foreach (var cue in cues)
        {
            var urgency = cue.EyeUrgency * weights[3] + cue.MissUrgency * weights[5];
            if (cue.Hold)
                urgency += weights[6];
            if (urgency > maxUrgency)
                maxUrgency = urgency;
            if (Math.Abs(cue.Side) < 0.5 || urgency <= 0)
                continue;

            var away = Rotate(Heading, -cue.Side * Math.Clamp(urgency, 0, 2) * 0.6);
            avoid += away * (urgency * weights[1]);
        }

        if (maxUrgency > weights[2])
            return AgentAction.Stay;

        var steer = goalDir * weights[0] + avoid;
        if (steer.Length < 1e-6)
            steer = goalDir;

        var delta = PassageGeometry.Wrap(Angle(steer) - Angle(goalDir));
        delta = Math.Clamp(delta, -Math.PI / 2, Math.PI / 2);
        var chosen = new Vec2(Math.Cos(Angle(goalDir) + delta), Math.Sin(Angle(goalDir) + delta));
        return AgentActionExtensions.NearestTo(chosen);
    }

    /// <inheritdoc />
    public override void Tick(WorldContext ctx)
    {
        var before = Position;
        base.Tick(ctx);
        if (LastAction != AgentAction.Stay)
            Heading = LastAction.ToStep();
        DistanceTraveled += ctx.World.Distance(before, Position);
        NoteViolations(ctx.World);
        if (Device == PassageDeviceKind.Radio && ctx.Tick % 8 == 0 && !Arrived)
            ctx.World.Transmit(this, "hold", ttl: 8);

        var remaining = Waypoints.Count == 0 ? 0 : (Waypoints[^1] - Position).Length;
        var progress = InitialGoalDistance <= 1e-6
            ? 1
            : Math.Clamp(1 - remaining / InitialGoalDistance, 0, 1);
        Fitness = progress * 100
            + (Arrived ? 80 : 0)
            - WallViolations * 2
            - AgentViolations * 2
            - ctx.Tick * 0.02;
    }

    private void NoteViolations(SimWorld world)
    {
        foreach (var body in world.Bodies)
        {
            if (body.DistanceToSurface(Position, world.WorldWidthPx, world.WorldHeightPx) < BodyRadius)
                WallViolations++;
        }

        foreach (var other in world.Agents)
        {
            if (other.Id == Id || other is not HallwayStudentAgent)
                continue;
            if (world.Distance(Position, other.Position) < BodyRadius * 2)
                AgentViolations++;
        }
    }

    private static double Angle(Vec2 vector) => Math.Atan2(vector.Y, vector.X);

    private static Vec2 Rotate(Vec2 vector, double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(vector.X * c - vector.Y * s, vector.X * s + vector.Y * c);
    }
}

/// <summary>Factory exported for reflection loading. Placement of a full class is done by <see cref="FloorPlanDeployer"/>.</summary>
public sealed class HallwayStudentAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "HallwayStudent";

    /// <inheritdoc />
    public string Description => "Waypoint plan plus a learned windshield correction.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new HallwayStudentAgent { Label = string.IsNullOrWhiteSpace(label) ? "Student" : label };
}
