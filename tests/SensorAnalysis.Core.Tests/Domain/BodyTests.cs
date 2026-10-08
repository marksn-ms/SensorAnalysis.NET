using FluentAssertions;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.Domain;

/// <summary>Fixed-size bodies: surface distance, sensing from a segment end, and solid walls.</summary>
public class BodyTests
{
    /// <summary>Clearance is center distance minus radius, and it is negative inside the disc.</summary>
    [Fact]
    public void Disc_DistanceToSurface_UsesRadius()
    {
        var disc = new Body
        {
            Shape = BodyShape.Disc,
            Position = new Vec2(0, 0),
            Radius = 3
        };

        disc.DistanceToSurface(new Vec2(10, 0), 1000, 1000).Should().BeApproximately(7, 1e-6);
        disc.DistanceToSurface(new Vec2(1, 0), 1000, 1000).Should().BeApproximately(-2, 1e-6);
    }

    /// <summary>A student at the end of a long wall senses the near surface when the center is out of range.</summary>
    [Fact]
    public void Segment_IsSensedFromItsNearEnd()
    {
        var world = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(40, 20),
            TileSizePx = new GridSize(20, 20)
        }, seed: 1);

        var wall = new Body
        {
            Shape = BodyShape.Segment,
            Position = new Vec2(200, 100),
            Length = 300,
            HeadingRadians = 0,
            Radius = 4,
            BodyType = "Wall"
        };
        world.Add(wall);

        var agent = new Agent
        {
            Position = new Vec2(50, 110),
            SensorRange = 40
        };
        world.Add(agent);

        world.Distance(agent.Position, wall.Position).Should().BeGreaterThan(agent.SensorRange);
        var observed = world.Observe(agent);
        observed.NearbyBodies.Should().ContainSingle();
        observed.NearbyBodies[0].Id.Should().Be(wall.Id);
        observed.NearbyBodies[0].Clearance.Should().BeApproximately(6, 1e-6);
    }

    /// <summary>An agent that steps into a disc is placed back on the surface. Empty worlds are unchanged.</summary>
    [Fact]
    public void AnchoredDisc_PushesPenetratingAgentToTheSurface()
    {
        var world = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(20, 20),
            TileSizePx = new GridSize(20, 20)
        }, seed: 1);
        world.Add(new Body
        {
            Shape = BodyShape.Disc,
            Position = new Vec2(100, 100),
            Radius = 20
        });
        world.Add(new NorthAgent { Position = new Vec2(100, 125) });

        world.Tick();

        var agent = world.Agents[0];
        agent.Position.X.Should().BeApproximately(100, 1e-6);
        agent.Position.Y.Should().BeApproximately(120, 1e-6);
        world.Bodies[0].DistanceToSurface(agent.Position, world.WorldWidthPx, world.WorldHeightPx)
            .Should().BeGreaterOrEqualTo(-1e-6);
    }

    private sealed class NorthAgent : Agent
    {
        public NorthAgent() => Velocity = 10;

        public override AgentAction Decide(AgentObservation observation) => AgentAction.North;
    }
}
