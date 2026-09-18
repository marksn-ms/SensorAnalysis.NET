using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>Skeptics follow a trusted peer's radio and join the orbit, they do not flee the recruiter.</summary>
public class MitigatedOrbitJoinTests
{
    [Fact]
    public void FollowsTrustedPeerContact_AndCommits()
    {
        var world = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 1);

        var skeptic = new MitigatedOrbitAgent
        {
            Position = new Vec2(100, 100),
            SensorRange = 80
        };
        var sender = new OrbitingCoopAgent
        {
            Position = new Vec2(108, 100),
            SensorRange = 80
        };
        world.Add(skeptic);
        world.Add(sender);

        var lure = new Vec2(100, 28);
        world.Transmit(sender, RadioNet.FormatContact(20_001, lure), ttl: 40);

        var ctx = new WorldContext
        {
            Tick = 0,
            Trial = 0,
            World = world,
            Rng = new Random(1)
        };

        skeptic.Tick(ctx);
        skeptic.TrackedEvents.Should().NotBeEmpty();
        skeptic.CommittedTrackId.Should().NotBeNull(
            "prior trust is above threshold, so one contact must be enough to assist");
        skeptic.Position.Y.Should().BeLessThan(100);
    }

    [Fact]
    public void SecondAgent_JoinsOrbit_FromRadioWhenEventIsOutOfSensorRange()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = true
        }, seed: 4);

        var onStation = new MitigatedOrbitAgent { Position = new Vec2(200, 160), SensorRange = 80 };
        var far = new MitigatedOrbitAgent { Position = new Vec2(130, 160), SensorRange = 80 };
        w.Add(onStation);
        w.Add(far);
        w.Add(new Incident
        {
            Position = new Vec2(200, 200),
            Intensity = 1.0,
            DrainPerAgentPerTick = 0.0001
        });

        w.Distance(far.Position, new Vec2(200, 200)).Should().BeGreaterThan(80);

        for (int i = 0; i < 80; i++)
            w.Tick();

        far.CommittedTrackId.Should().NotBeNull();
        w.Distance(far.Position, new Vec2(200, 200)).Should().BeLessThan(70);
        w.Distance(onStation.Position, new Vec2(200, 200)).Should().BeLessThan(70);
    }
}
