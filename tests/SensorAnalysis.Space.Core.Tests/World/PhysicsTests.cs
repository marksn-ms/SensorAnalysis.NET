using FluentAssertions;
using SensorAnalysis.Space.Core.Domain;
using SensorAnalysis.Space.Core.World;
using Xunit;

namespace SensorAnalysis.Space.Core.Tests.World;

/// <summary>Gravity, wrapping collisions, and the planetoid-belt initializer.</summary>
public sealed class PhysicsTests
{
    [Fact]
    public void CircularOrbit_StaysNearRadiusUnderLeapfrog()
    {
        var world = new SimWorld(new WorldOptions
        {
            Kind = WorldKind.Empty,
            CubesPerAxis = 20,
            CubeSize = 40,
            GravityCutoffFraction = 0.9
        }, seed: 1);

        const double radius = 80;
        const double period = 400;
        var mu = Physics.MuForPeriod(radius, period);
        world.Add(new Body
        {
            ObjectType = "Planetoid",
            Position = Vec3.Zero,
            Mu = mu,
            VisualRadius = 10,
            ShieldRadius = 10,
            IsAnchored = true,
            GravityCutoff = 300
        });

        var omega = Math.Tau / period;
        var rock = new Body
        {
            ObjectType = "Asteroid",
            Position = new Vec3(radius, 0, 0),
            Velocity = new Vec3(0, 0, omega * radius),
            VisualRadius = 2,
            ShieldRadius = 2,
            HasInertia = true,
            Mass = 1
        };
        world.Add(rock);

        for (var i = 0; i < 200; i++)
            world.Tick();

        var r = world.Distance(Vec3.Zero, rock.Position);
        r.Should().BeApproximately(radius, radius * 0.08);
    }

    [Fact]
    public void ShieldCollision_SeparatesApproachingBodies()
    {
        var world = new SimWorld(new WorldOptions
        {
            Kind = WorldKind.Empty,
            CubesPerAxis = 16,
            CubeSize = 20
        }, seed: 2);

        var a = new Body
        {
            Position = new Vec3(-4, 0, 0),
            Velocity = new Vec3(0.8, 0, 0),
            ShieldRadius = 2,
            VisualRadius = 2,
            Mass = 1,
            HasInertia = true,
            Restitution = 1
        };
        var b = new Body
        {
            Position = new Vec3(4, 0, 0),
            Velocity = new Vec3(-0.8, 0, 0),
            ShieldRadius = 2,
            VisualRadius = 2,
            Mass = 1,
            HasInertia = true,
            Restitution = 1
        };
        world.Add(a);
        world.Add(b);

        for (var i = 0; i < 20; i++)
            world.Tick();

        world.Distance(a.Position, b.Position).Should().BeGreaterThanOrEqualTo(a.ShieldRadius + b.ShieldRadius - 0.05);
        (a.Velocity.X < 0 || b.Velocity.X > 0).Should().BeTrue();
    }

    [Fact]
    public void PlanetoidBelt_SpawnsKeplerianRocksAndWeavers()
    {
        var options = new WorldOptions
        {
            Kind = WorldKind.PlanetoidBelt,
            CubesPerAxis = 24,
            CubeSize = 40,
            AsteroidCount = 12,
            AgentCount = 3,
            OrbitPeriodTicks = 900
        };
        var world = new SimWorld(options, seed: 42);

        world.Bodies.Should().ContainSingle(b => b.ObjectType == "Planetoid" && b.IsAnchored && b.Mu > 0);
        world.Bodies.Count(b => b.ObjectType == "Asteroid").Should().Be(12);
        world.Agents.Should().HaveCount(3);

        var planetoid = world.Bodies.Single(b => b.ObjectType == "Planetoid");
        var rMid = 0.5 * (options.BeltInnerRadius + options.BeltOuterRadius);
        var expectedSpeed = Physics.CircularSpeed(planetoid.Mu, rMid);
        var rock = world.Bodies.First(b => b.ObjectType == "Asteroid");
        var r = world.Distance(planetoid.Position, rock.Position);
        r.Should().BeInRange(options.BeltInnerRadius * 0.9, options.BeltOuterRadius * 1.1);
        rock.Velocity.Length.Should().BeApproximately(Physics.CircularSpeed(planetoid.Mu, r), expectedSpeed * 0.2);
    }

    [Fact]
    public void TileAt_AndRay_VisitPrimaryCell()
    {
        var world = new SimWorld(new WorldOptions { Kind = WorldKind.Empty, CubesPerAxis = 8, CubeSize = 10 }, seed: 3);
        var tile = world.TileAt(Vec3.Zero);
        tile.Ix.Should().Be(4);
        tile.Iy.Should().Be(4);
        tile.Iz.Should().Be(4);

        var along = world.TilesAlongRay(new Vec3(-35, 0, 0), Vec3.UnitX, 20);
        along.Should().NotBeEmpty();
        along[0].Should().Be(world.TileAt(new Vec3(-35, 0, 0)));
    }

    [Fact]
    public void ToroidalStream_CoastsAlongAxisWithoutGravity()
    {
        var options = new WorldOptions
        {
            Kind = WorldKind.ToroidalStream,
            DriftAxis = WorldAxis.X,
            CubesPerAxis = 16,
            CubeSize = 20,
            AsteroidCount = 8,
            AgentCount = 0,
            OrbitPeriodTicks = 200,
            StreamRadius = 40
        };
        var world = new SimWorld(options, seed: 7);

        world.Bodies.Should().HaveCount(8);
        world.Bodies.Should().OnlyContain(b => b.ObjectType == "Asteroid" && b.Mu == 0 && !b.IsAnchored);
        world.Agents.Should().BeEmpty();

        var radial = world.Bodies.Select(b => Math.Sqrt(b.Position.Y * b.Position.Y + b.Position.Z * b.Position.Z)).ToArray();
        radial.Should().OnlyContain(r => r <= options.StreamRadius + 1e-6);
        radial.Max().Should().BeGreaterThan(options.StreamRadius * 0.35);

        var speed = options.Extent / options.OrbitPeriodTicks;
        foreach (var rock in world.Bodies)
        {
            rock.Velocity.X.Should().BeApproximately(speed, 1e-9);
            rock.Velocity.Y.Should().BeApproximately(0, 1e-9);
            rock.Velocity.Z.Should().BeApproximately(0, 1e-9);
            rock.AngularVelocity.Length.Should().BeGreaterThan(0);
        }

        var before = world.Bodies.Select(b => b.Position.X).ToArray();
        for (var i = 0; i < 25; i++)
            world.Tick();

        for (var i = 0; i < world.Bodies.Count; i++)
        {
            var delta = Vec3.WrapCoord(world.Bodies[i].Position.X - before[i], world.Extent);
            delta.Should().BeApproximately(speed * 25, 0.5);
        }
    }
}
