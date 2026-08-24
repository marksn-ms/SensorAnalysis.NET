using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class DecoyLureTests
{
    [Fact]
    public void AwayFromEvent_PlacesClaimAt90PercentRangeOppositeTheTruth()
    {
        var victim = new Vec2(100, 100);
        var ev = new Vec2(100, 60); // event is north of victim (smaller Y)
        var claimed = DecoyLure.AwayFromEvent(victim, ev, sensorRange: 80, 400, 400);
        claimed.Should().NotBeNull();
        // Away from event (south) ⇒ larger Y, distance 0.9*80 = 72
        claimed!.Value.X.Should().BeApproximately(100, 1e-6);
        claimed.Value.Y.Should().BeApproximately(172, 1e-6);
        Vec2.Distance(victim, claimed.Value).Should().BeApproximately(72, 1e-6);
    }
}
