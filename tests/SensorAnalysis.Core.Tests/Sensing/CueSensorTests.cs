using FluentAssertions;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Sensing;
using Xunit;

namespace SensorAnalysis.Core.Tests.Sensing;

public class CueSensorTests
{
    [Fact]
    public void DetectionProbability_IsCertainAtTheSource_AndZeroAtRange()
    {
        var profile = new IncidentSensingProfile();
        CueSensor.DetectionProbability(0, 80, profile).Should().Be(1);
        CueSensor.DetectionProbability(80, 80, profile).Should().Be(0);
        CueSensor.DetectionProbability(40, 80, profile).Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void PositionSigma_IsZeroAtSource_AndROver4AtHorizon()
    {
        var profile = new IncidentSensingProfile { PositionErrorAtRangeFactor = 0.25 };
        CueSensor.PositionSigma(0, 80, profile).Should().Be(0);
        CueSensor.PositionSigma(80, 80, profile).Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void TrySense_AlwaysHits_WhenObserverSitsOnTheEvent()
    {
        var incident = new Incident { Position = new Vec2(50, 50) };
        var observer = new Agent { Position = new Vec2(50, 50), SensorRange = 80 };
        CueSensor.TrySense(
                incident, observer, tick: 3, worldSeed: 42,
                new IncidentSensingProfile(), 200, 200, out var cue)
            .Should().BeTrue();
        cue.Id.Should().Be(0);
        cue.Position.X.Should().BeApproximately(50, 1e-9);
        cue.Position.Y.Should().BeApproximately(50, 1e-9);
    }
}
