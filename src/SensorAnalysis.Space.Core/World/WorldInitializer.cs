using SensorAnalysis.Space.Core.Agents;
using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.Core.World;

/// <summary>
/// Populates a freshly constructed <see cref="SimWorld"/> according to <see cref="WorldOptions.Kind"/>.
/// Planetoid-belt orbits are slow and Keplerian; <see cref="WorldKind.ToroidalStream"/> is a no-gravity wrapping convoy.
/// </summary>
public static class WorldInitializer
{
    /// <summary>Applies the configured initializer to <paramref name="world"/>.</summary>
    /// <param name="world">Empty world to populate.</param>
    public static void Populate(SimWorld world)
    {
        switch (world.Options.Kind)
        {
            case WorldKind.PlanetoidBelt:
                PopulatePlanetoidBelt(world);
                break;
            case WorldKind.ToroidalStream:
                PopulateToroidalStream(world);
                break;
            case WorldKind.Empty:
            default:
                break;
        }
    }

    /// <summary>Places an anchored planetoid at the origin, a Keplerian belt in the XZ plane, and weaver agents.</summary>
    /// <param name="world">Target world.</param>
    public static void PopulatePlanetoidBelt(SimWorld world)
    {
        var options = world.Options;
        var rng = world.Random;
        var rMid = 0.5 * (options.BeltInnerRadius + options.BeltOuterRadius);
        var mu = Physics.MuForPeriod(rMid, options.OrbitPeriodTicks);
        var cutoff = options.HalfExtent * options.GravityCutoffFraction;
        var spinRate = Math.Tau / options.OrbitPeriodTicks;

        var planetoid = new Body
        {
            ObjectType = "Planetoid",
            Label = "Planetoid",
            Position = Vec3.Zero,
            Visual = VisualKind.Sphere,
            VisualRadius = options.PlanetoidRadius,
            ShieldRadius = options.PlanetoidRadius * 1.05,
            Mu = mu,
            GravityCutoff = cutoff,
            IsAnchored = true,
            HasInertia = true,
            Mass = 1e9,
            AngularVelocity = new Vec3(0, spinRate, 0),
            Forward = Vec3.UnitZ,
            Up = Vec3.UnitY,
            Restitution = 0.35
        };
        world.Add(planetoid);

        var inclination = options.BeltInclinationDegrees * Math.PI / 180.0;
        for (var i = 0; i < options.AsteroidCount; i++)
        {
            var radius = Lerp(options.BeltInnerRadius, options.BeltOuterRadius, rng.NextDouble());
            var theta = rng.NextDouble() * Math.Tau;
            var pos = new Vec3(radius * Math.Cos(theta), 0, radius * Math.Sin(theta));
            var tilt = (rng.NextDouble() * 2 - 1) * inclination;
            var tiltAxis = new Vec3(-Math.Sin(theta), 0, Math.Cos(theta));
            pos = Vec3.Rotate(pos, tiltAxis * tilt);

            var speed = Physics.CircularSpeed(mu, radius);
            var tangent = new Vec3(-pos.Z, 0, pos.X);
            var vel = tangent.LengthSquared < 1e-12
                ? Vec3.UnitZ * speed
                : tangent.Normalize() * speed;
            vel = Vec3.Rotate(vel, tiltAxis * tilt);

            var size = 2.4 + rng.NextDouble() * 3.2;
            var spin = Vec3.Zero;
            if (rng.NextDouble() < options.AsteroidSpinFraction)
            {
                var tumblePeriod = options.OrbitPeriodTicks * (2.2 + rng.NextDouble() * 3.0);
                spin = Vec3.RandomUnit(rng) * (Math.Tau / tumblePeriod);
            }

            var (forward, up) = Vec3.RandomOrientation(rng);
            world.Add(new Body
            {
                ObjectType = "Asteroid",
                Label = $"Asteroid {i + 1}",
                Position = pos,
                Velocity = vel,
                Forward = forward,
                Up = up,
                AngularVelocity = spin,
                Visual = VisualKind.Icosphere,
                VisualRadius = size,
                ShieldRadius = size * 1.15,
                Mass = 1.0,
                HasInertia = true,
                Mu = 0,
                Restitution = 0.4
            });
        }

        for (var i = 0; i < options.AgentCount; i++)
        {
            var radius = Lerp(options.BeltInnerRadius, options.BeltOuterRadius, 0.25 + 0.5 * rng.NextDouble());
            var theta = rng.NextDouble() * Math.Tau;
            var pos = new Vec3(radius * Math.Cos(theta), (rng.NextDouble() - 0.5) * 8, radius * Math.Sin(theta));
            var speed = Physics.CircularSpeed(mu, Math.Max(pos.Length, 1));
            var tangent = new Vec3(-pos.Z, 0, pos.X);
            var vel = tangent.LengthSquared < 1e-12
                ? Vec3.UnitZ * speed
                : tangent.Normalize() * speed;
            var (forward, up) = Vec3.OrthonormalFrame(vel, Vec3.UnitY);

            var agent = new RandomWalkAgent
            {
                Label = $"Weaver {i + 1}",
                Position = pos,
                Velocity = vel,
                Forward = forward,
                Up = up,
                SensorRange = options.SensorRange,
                CommRange = options.CommunicationRange,
                Mass = 1.0,
                HasInertia = true,
                Fuel = double.PositiveInfinity,
                MaxDeltaVPerTick = Math.Max(0.015, speed * 0.004)
            };
            agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
            world.Add(agent);
        }

        world.Record($"Planetoid belt: 1 planetoid, {options.AsteroidCount} asteroids, {options.AgentCount} weavers.");
    }

    /// <summary>
    /// Places a convoy of asteroids along <see cref="WorldOptions.DriftAxis"/> through the origin.
    /// No gravity source; they coast and wrap. Each rock has a slow tumble.
    /// </summary>
    /// <param name="world">Target world.</param>
    public static void PopulateToroidalStream(SimWorld world)
    {
        var options = world.Options;
        SpawnStreamAsteroids(world, options.AsteroidCount, options.DriftAxis);
        world.Record(
            $"Toroidal stream: {options.AsteroidCount} asteroids along {options.DriftAxis}, " +
            $"crossing in {options.OrbitPeriodTicks:0} ticks.");
    }

    /// <summary>Spawns random-walk weavers at random wrapped positions (used by directives).</summary>
    /// <param name="world">Target world.</param>
    /// <param name="count">How many agents to add.</param>
    public static void SpawnRandomWalkers(SimWorld world, int count)
    {
        var rng = world.Random;
        var half = world.Options.HalfExtent;
        for (var i = 0; i < count; i++)
        {
            var pos = new Vec3(
                (rng.NextDouble() * 2 - 1) * half * 0.8,
                (rng.NextDouble() * 2 - 1) * half * 0.8,
                (rng.NextDouble() * 2 - 1) * half * 0.8);
            var (forward, up) = Vec3.RandomOrientation(rng);
            var agent = new RandomWalkAgent
            {
                Label = $"Weaver {world.Agents.Count + 1}",
                Position = world.Wrap(pos),
                Velocity = Vec3.RandomUnit(rng) * 0.15,
                Forward = forward,
                Up = up,
                SensorRange = world.Options.SensorRange,
                CommRange = world.Options.CommunicationRange,
                Mass = 1.0,
                HasInertia = true,
                Fuel = double.PositiveInfinity
            };
            agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
            world.Add(agent);
        }
    }

    /// <summary>Spawns free-drifting asteroids at random poses (used by directives).</summary>
    /// <param name="world">Target world.</param>
    /// <param name="count">How many asteroids to add.</param>
    public static void SpawnDriftAsteroids(SimWorld world, int count)
    {
        var rng = world.Random;
        var half = world.Options.HalfExtent;
        var period = Math.Max(world.Options.OrbitPeriodTicks, 60);
        for (var i = 0; i < count; i++)
        {
            var pos = new Vec3(
                (rng.NextDouble() * 2 - 1) * half * 0.85,
                (rng.NextDouble() * 2 - 1) * half * 0.85,
                (rng.NextDouble() * 2 - 1) * half * 0.85);
            var speed = world.Options.Extent / period * (0.4 + rng.NextDouble() * 0.4);
            AddAsteroid(
                world,
                world.Wrap(pos),
                Vec3.RandomUnit(rng) * speed,
                SlowSpin(rng, period),
                2.4 + rng.NextDouble() * 3.2,
                $"Asteroid {world.Bodies.Count(b => b.ObjectType == "Asteroid") + 1}");
        }
    }

    private static void SpawnStreamAsteroids(SimWorld world, int count, WorldAxis axis)
    {
        var rng = world.Random;
        var n = Math.Max(0, count);
        if (n == 0)
            return;

        var options = world.Options;
        var extent = options.Extent;
        var half = options.HalfExtent;
        var speed = extent / Math.Max(options.OrbitPeriodTicks, 1);
        var along = AxisUnit(axis);
        var (perpA, perpB) = AxisPerps(axis);
        var period = Math.Max(options.OrbitPeriodTicks, 60);

        for (var i = 0; i < n; i++)
        {
            var s = ((i + 0.5) / n) * extent - half;
            var o1 = (rng.NextDouble() - 0.5) * 10;
            var o2 = (rng.NextDouble() - 0.5) * 10;
            var pos = along * s + perpA * o1 + perpB * o2;
            AddAsteroid(
                world,
                world.Wrap(pos),
                along * speed,
                SlowSpin(rng, period),
                2.6 + rng.NextDouble() * 2.8,
                $"Asteroid {i + 1}");
        }
    }

    private static void AddAsteroid(SimWorld world, Vec3 position, Vec3 velocity, Vec3 spin, double size, string label)
    {
        var (forward, up) = Vec3.RandomOrientation(world.Random);
        world.Add(new Body
        {
            ObjectType = "Asteroid",
            Label = label,
            Position = position,
            Velocity = velocity,
            Forward = forward,
            Up = up,
            AngularVelocity = spin,
            Visual = VisualKind.Icosphere,
            VisualRadius = size,
            ShieldRadius = size * 1.15,
            Mass = 1.0,
            HasInertia = true,
            Mu = 0,
            Restitution = 0.4
        });
    }

    private static Vec3 SlowSpin(Random rng, double periodTicks)
    {
        var tumblePeriod = periodTicks * (2.4 + rng.NextDouble() * 3.2);
        return Vec3.RandomUnit(rng) * (Math.Tau / tumblePeriod);
    }

    private static Vec3 AxisUnit(WorldAxis axis) => axis switch
    {
        WorldAxis.Y => Vec3.UnitY,
        WorldAxis.Z => Vec3.UnitZ,
        _ => Vec3.UnitX
    };

    private static (Vec3 A, Vec3 B) AxisPerps(WorldAxis axis) => axis switch
    {
        WorldAxis.Y => (Vec3.UnitX, Vec3.UnitZ),
        WorldAxis.Z => (Vec3.UnitX, Vec3.UnitY),
        _ => (Vec3.UnitY, Vec3.UnitZ)
    };

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
