using FluentAssertions;
using SensorAnalysis.Core.Domain;
using Xunit;

namespace SensorAnalysis.Core.Tests.Domain;

/// <summary>Tests for <see cref="AgentAction"/> movement vectors.</summary>
public class AgentActionTests
{
    /// <summary>All sixteen headings produce unit-length step vectors.</summary>
    [Fact]
    public void ToStep_MovementDirections_AreUnitLength()
    {
        foreach (var action in AgentActionExtensions.MovementDirections)
            action.ToStep().Length.Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>Cardinal and intercardinal headings match screen coordinates.</summary>
    [Theory]
    [InlineData(AgentAction.North, 0, -1)]
    [InlineData(AgentAction.East, 1, 0)]
    [InlineData(AgentAction.South, 0, 1)]
    [InlineData(AgentAction.West, -1, 0)]
    public void ToStep_Cardinals_MatchAxes(AgentAction action, double x, double y)
    {
        var step = action.ToStep();
        step.X.Should().BeApproximately(x, 1e-9);
        step.Y.Should().BeApproximately(y, 1e-9);
    }

    /// <summary>Nearest heading snaps toward the requested displacement.</summary>
    [Theory]
    [InlineData(1, 0, AgentAction.East)]
    [InlineData(0, -1, AgentAction.North)]
    [InlineData(1, -1, AgentAction.NorthEast)]
    [InlineData(0.2, -1, AgentAction.NorthNorthEast)]
    public void NearestTo_QuantizesToSixteenPointCompass(double dx, double dy, AgentAction expected)
    {
        AgentActionExtensions.NearestTo(new Vec2(dx, dy)).Should().Be(expected);
    }
}
