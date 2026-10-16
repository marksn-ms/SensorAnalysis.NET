using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Hallway;

/// <summary>Dimensions, class size, and passage device for <see cref="FloorPlanDeployer"/>.</summary>
public sealed class FloorPlanDeployOptions : DeployerOptionsBase
{
    /// <summary>Hall length in world units. 3600 is 100 yards when the unit is an inch.</summary>
    [DeployerParam("Hall length", Description = "World units. 3600 = 100 yards at one unit per inch.")]
    public double HallLength { get; set; } = 360;

    /// <summary>Clear hall width. 96 is 8 feet. 600 is 50 feet.</summary>
    [DeployerParam("Hall width")]
    public double HallWidth { get; set; } = 96;

    /// <summary>Classroom depth on each side.</summary>
    [DeployerParam("Room depth")]
    public double RoomDepth { get; set; } = 120;

    /// <summary>Distance between door centers.</summary>
    [DeployerParam("Door spacing")]
    public double DoorSpacing { get; set; } = 180;

    /// <summary>Doorway gap width.</summary>
    [DeployerParam("Door width")]
    public double DoorWidth { get; set; } = 36;

    /// <summary>Wall thickness. The body radius is half of this.</summary>
    [DeployerParam("Wall thickness")]
    public double WallThickness { get; set; } = 6;

    /// <summary>Torus margin around the building. Raised automatically when sensor range is longer.</summary>
    [DeployerParam("Margin")]
    public double Margin { get; set; } = 200;

    /// <summary>Students scattered around each classroom beacon.</summary>
    [DeployerParam("Students per room")]
    public int StudentsPerRoom { get; set; } = 4;

    /// <summary>Student proximity radius.</summary>
    [DeployerParam("Body radius")]
    public double BodyRadius { get; set; } = 8;

    /// <summary>Eye and radio sensor range. Radar multiplies this by 2.5.</summary>
    [DeployerParam("Sensor range")]
    public double SensorRange { get; set; } = 180;

    /// <summary>Step length per tick.</summary>
    [DeployerParam("Velocity")]
    public double Velocity { get; set; } = 6;

    /// <summary>Eye, Math, Radar, or Radio.</summary>
    [DeployerParam("Device", Description = "Eye, Math, Radar, or Radio.")]
    public string Device { get; set; } = "Eye";

    /// <summary>Spec used both to size the world and to build the plan.</summary>
    public FloorPlanSpec ToSpec()
    {
        var device = PassageDevices.Parse(Device);
        var sense = device == PassageDeviceKind.Radar ? SensorRange * 2.5 : SensorRange;
        return new FloorPlanSpec
        {
            HallLength = HallLength,
            HallWidth = HallWidth,
            RoomDepth = RoomDepth,
            DoorSpacing = DoorSpacing,
            DoorWidth = DoorWidth,
            WallThickness = WallThickness,
            Margin = Math.Max(Margin, sense + 24),
            StudentsPerRoom = Math.Max(0, StudentsPerRoom),
            BodyRadius = BodyRadius,
            SensorRange = sense,
            Velocity = Velocity,
            Device = device.ToString()
        };
    }
}

/// <summary>
/// Tick-0 deployer: wall bodies, a classroom incident in each room, and students
/// scattered on a Gaussian about that incident.
/// </summary>
public sealed class FloorPlanDeployer : IDeployer
{
    private bool _dormant;

    /// <inheritdoc />
    public string Name => "FloorPlan";

    /// <inheritdoc />
    public string Description =>
        "Hallway of fixed-size wall bodies, classroom beacons, and students with a shared genome.";

    /// <inheritdoc />
    public bool IsDormant => _dormant;

    /// <inheritdoc />
    public Type OptionsType => typeof(FloorPlanDeployOptions);

    /// <inheritdoc />
    public object CreateOptions() => new FloorPlanDeployOptions();

    /// <inheritdoc />
    public void Attach(SimWorld world, object options, DeployContext context)
    {
        var spec = ((FloorPlanDeployOptions)options).ToSpec();
        if (world.WorldWidthPx + 1e-6 < spec.RequiredWidth || world.WorldHeightPx + 1e-6 < spec.RequiredHeight)
        {
            var cols = (int)Math.Ceiling(spec.RequiredWidth / 12.0);
            var rows = (int)Math.Ceiling(spec.RequiredHeight / 12.0);
            throw new InvalidOperationException(
                $"Floor plan needs a world at least {spec.RequiredWidth:0} by {spec.RequiredHeight:0} units. " +
                $"This world is {world.WorldWidthPx:0} by {world.WorldHeightPx:0}. " +
                $"With 12-unit tiles that is {cols} by {rows}.");
        }

        var plan = FloorPlan.Build(spec);
        foreach (var wall in plan.Walls)
            world.Add(wall);

        foreach (var room in plan.Rooms)
        {
            world.Add(new Incident
            {
                Position = room.Center,
                IncidentType = "Classroom",
                Label = $"Room {room.Index}",
                Intensity = 1,
                DrainPerAgentPerTick = 0
            });
        }

        HallwaySession.Population.EnsureSeed(world.Seed);
        var weights = HallwaySession.Population.WeightsForThisTrial();
        var device = PassageDevices.Parse(spec.Device);
        var rng = world.Random;
        for (var roomIndex = 0; roomIndex < plan.Rooms.Count; roomIndex++)
        {
            var room = plan.Rooms[roomIndex];
            for (var s = 0; s < spec.StudentsPerRoom; s++)
            {
                var destination = NextDestination(rng, plan.Rooms.Count, roomIndex);
                var position = SampleRoom(rng, room, spec.BodyRadius);
                var route = RoutePlanner.Plan(plan, roomIndex, destination, spec.BodyRadius);
                var student = new HallwayStudentAgent
                {
                    Position = position,
                    Label = $"S{roomIndex}-{s}",
                    SensorRange = spec.SensorRange,
                    Velocity = spec.Velocity,
                    BodyRadius = spec.BodyRadius,
                    Device = device,
                    OriginRoom = roomIndex,
                    DestinationRoom = destination,
                    Waypoints = route,
                    Weights = weights,
                    InitialGoalDistance = Math.Max(1, (route[^1] - position).Length),
                    ContributesToIncidentDrain = false
                };
                student.SeedHeading();
                world.Add(student);
            }
        }

        _dormant = true;
    }

    /// <inheritdoc />
    public void Tick(WorldContext ctx)
    {
    }

    private static int NextDestination(Random rng, int roomCount, int origin)
    {
        if (roomCount <= 1)
            return origin;
        var dest = rng.Next(roomCount - 1);
        if (dest >= origin)
            dest++;
        return dest;
    }

    private static Vec2 SampleRoom(Random rng, Classroom room, double bodyRadius)
    {
        var minX = room.MinX + bodyRadius;
        var maxX = room.MaxX - bodyRadius;
        var minY = room.MinY + bodyRadius;
        var maxY = room.MaxY - bodyRadius;
        if (minX >= maxX || minY >= maxY)
            return room.Center;

        var sigmaX = Math.Max(4, (maxX - minX) * 0.25);
        var sigmaY = Math.Max(4, (maxY - minY) * 0.25);
        for (var n = 0; n < 40; n++)
        {
            var p = new Vec2(
                room.Center.X + NextGaussian(rng) * sigmaX,
                room.Center.Y + NextGaussian(rng) * sigmaY);
            if (p.X > minX && p.X < maxX && p.Y > minY && p.Y < maxY)
                return p;
        }

        return room.Center;
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
