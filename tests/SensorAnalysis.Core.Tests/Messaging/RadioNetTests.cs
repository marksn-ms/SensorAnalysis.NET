using FluentAssertions;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using Xunit;

namespace SensorAnalysis.Core.Tests.Messaging;

/// <summary>Tests for radio payload codecs and narration.</summary>
public class RadioNetTests
{
    /// <summary>Contact / orbit / break round-trip.</summary>
    [Fact]
    public void RadioNet_ParsesStateChangePayloads()
    {
        var pos = new Vec2(12.5, 40);
        RadioNet.TryParseClaim(RadioNet.FormatContact(3, pos), out var k1, out var id1, out var p1)
            .Should().BeTrue();
        k1.Should().Be(RadioClaimKind.Contact);
        id1.Should().Be(3);
        p1.X.Should().BeApproximately(12.5, 1e-6);

        RadioNet.TryParseClaim(RadioNet.FormatOrbit(3, pos), out var k2, out _, out _)
            .Should().BeTrue();
        k2.Should().Be(RadioClaimKind.Orbit);

        RadioNet.TryParseBreak(RadioNet.FormatBreak(9), out var broken).Should().BeTrue();
        broken.Should().Be(9);
    }

    /// <summary>Narrator distinguishes orbit confirmation from deceptive status.</summary>
    [Fact]
    public void RadioNarrator_ColorsKinds()
    {
        var (_, contact) = RadioNarrator.Narrate(1, "OrbitingCoop",
            RadioNet.FormatContact(2, new Vec2(10, 20)), new Vec2(10, 20), 80);
        contact.Should().Contain("possible Tango spotted");

        var (kind, orbit) = RadioNarrator.Narrate(1, "OrbitingCoop",
            RadioNet.FormatOrbit(2, new Vec2(10, 20)), new Vec2(10, 20), 80);
        kind.Should().Be("Orbit");
        orbit.Should().Contain("Tango confirmed");

        var (_, decoy) = RadioNarrator.Narrate(7, "HerdingDeceptive",
            IncidentMarker.Format(99, new Vec2(1, 2)), new Vec2(100, 100), 80);
        decoy.Should().Contain("possible Tango");
    }
}
