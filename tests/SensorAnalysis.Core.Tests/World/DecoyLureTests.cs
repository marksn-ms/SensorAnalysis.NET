using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class DecoyLureTests
{
    [Fact]
    public void AwayFromEvent_PlacesClaimAt90PercentRangeOppositeTheTruth()
    {
        var victimAbsolute = new Vec2(100, 100);
        var eventAbsolute = new Vec2(100, 60);
        var observation = ObservationAt(victimAbsolute);

        var victim = observation.RelativeFromAbsolute(victimAbsolute);
        var ev = observation.RelativeFromAbsolute(eventAbsolute);
        victim.X.Should().BeApproximately(0, 1e-6);
        victim.Y.Should().BeApproximately(0, 1e-6);

        var claimedRelative = DecoyLure.AwayFromEvent(victim, ev, sensorRange: 80, observation);
        claimedRelative.Should().NotBeNull();

        var claimed = observation.AbsoluteFromRelative(claimedRelative!.Value);
        claimed.X.Should().BeApproximately(100, 1e-6);
        claimed.Y.Should().BeApproximately(172, 1e-6);
        Vec2.Distance(victimAbsolute, claimed).Should().BeApproximately(72, 1e-6);
    }

    private static AgentObservation ObservationAt(Vec2 origin)
    {
        var world = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40)
        }, seed: 1);
        var agent = new Agent { Position = origin, SensorRange = 500 };
        world.Add(agent);
        return world.Observe(agent);
    }
}
