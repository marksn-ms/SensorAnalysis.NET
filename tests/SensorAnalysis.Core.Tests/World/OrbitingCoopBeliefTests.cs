using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class OrbitingCoopBeliefTests
{
    [Fact]
    public void FollowedDecoyRumor_IsNotDroppedJustBecauseTheIdIsFake()
    {
        var world = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 1);

        var honest = new OrbitingCoopAgent(seed: 2)
        {
            Position = new Vec2(100, 100),
            SensorRange = 80
        };
        var sender = new RandomWalkAgent(seed: 3)
        {
            Position = new Vec2(108, 100),
            SensorRange = 80
        };
        world.Add(honest);
        world.Add(sender);

        var lure = new Vec2(100, 28); // ~90% of r=80 south of the honest agent
        world.Transmit(sender, RadioNet.FormatContact(20_001, lure), ttl: 40);

        var ctx = new WorldContext
        {
            Tick = 0,
            Trial = 0,
            World = world,
            Rng = new Random(1)
        };

        honest.Tick(ctx);
        honest.TrackedEvents.Should().NotBeEmpty();
        honest.CommittedTrackId.Should().NotBeNull();
        world.ActiveIncidents.Should().BeEmpty();

        honest.Tick(ctx);
        honest.CommittedTrackId.Should().NotBeNull(
            "a missing world id must not clear a rumor on the next tick");
        honest.Position.Y.Should().BeLessThan(100,
            "the agent should step toward the claimed lure");
    }
}
