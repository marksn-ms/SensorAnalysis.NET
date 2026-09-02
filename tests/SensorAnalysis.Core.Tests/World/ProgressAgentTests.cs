using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.Sensing;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class ProgressAgentTests
{
    private static SimWorld Tiny(bool exact = true) =>
        new(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = exact
        }, seed: 1);

    [Fact]
    public void Catalog_RegistersProgressFactories()
    {
        var names = BuiltinCatalog.CreateAgents().Names.ToList();
        names.Should().Contain("ProgressCoop");
        names.Should().Contain("ProgressLoiterDeceptive");
        names.Should().Contain("ProgressAwareMitigated");
    }

    [Fact]
    public void ProgressCoop_SitsOnACloseCue_AndIgnoresRadioMessages()
    {
        var world = Tiny();
        var coop = new ProgressCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var chatter = new RandomWalkAgent { Position = new Vec2(108, 100), SensorRange = 80 };
        world.Add(coop);
        world.Add(chatter);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay, "on-cue covering does not need radio");
        world.Messages.Should().BeEmpty("ProgressCoop must not transmit");

        world.Transmit(chatter, RadioNet.FormatContact(20_001, new Vec2(10, 10)), ttl: 40);
        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay, "NearbyMessages must not steer ProgressCoop off a resolving sit");
    }

    [Fact]
    public void ProgressLoiter_SitsInTheDisc_WithoutReadingPeerAgentType()
    {
        var world = Tiny();
        var loiter = new ProgressLoiterDeceptiveAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var decoyPeer = new RandomWalkAgent
        {
            Position = new Vec2(105, 100),
            SensorRange = 80,
            AgentType = "OrbitingCoop"
        };
        world.Add(loiter);
        world.Add(decoyPeer);
        world.Add(new Incident { Position = new Vec2(102, 100), Intensity = 1 });

        world.Tick();
        loiter.LastAction.Should().Be(AgentAction.Stay);
        loiter.ContributesToIncidentDrain.Should().BeFalse();
        world.Messages.Should().BeEmpty("loiterer has no radio");
    }

    [Fact]
    public void ProgressSkeptic_CreditsOffenseWhenCoPresentAndIntensityStallsAtSoloRate()
    {
        var world = Tiny(exact: true);
        var skeptic = new ProgressAwareMitigatedAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        // Relabel so a type-reading detector would miss this infiltrator.
        var loiter = new ProgressLoiterDeceptiveAgent
        {
            Position = new Vec2(102, 100),
            SensorRange = 80,
            AgentType = "HarmlessLooking"
        };
        world.Add(skeptic);
        world.Add(loiter);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        for (int i = 0; i < 20; i++)
            world.Tick();

        skeptic.ProgressOffenseCountToward(loiter.Id).Should().BeGreaterThan(0,
            "co-present + solo-rate shrink is the progress offense; AgentType must not be required");
        skeptic.LifetimeProgressOffenseWindows.Should().BeGreaterThan(0);
        skeptic.MeanTrustToward(loiter.Id).Should().BeLessThan(0.5);
        skeptic.LifetimeCopresentTicks.GetValueOrDefault(loiter.Id).Should().BeGreaterThan(10);
    }

    [Fact]
    public void ProgressSkeptic_DoesNotBlameASecondDrainer()
    {
        var world = Tiny(exact: true);
        var skeptic = new ProgressAwareMitigatedAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var helper = new ProgressCoopAgent { Position = new Vec2(102, 100), SensorRange = 80 };
        world.Add(skeptic);
        world.Add(helper);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        for (int i = 0; i < 20; i++)
            world.Tick();

        skeptic.ProgressOffenseCountToward(helper.Id).Should().Be(0,
            "two drainers shrink faster than the one-drainer prior; not an offense");
        skeptic.MeanTrustToward(helper.Id).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void CueSensor_ForwardsIncidentIntensity_WhichShrinksWithDrain()
    {
        var incident = new Incident { Position = new Vec2(50, 50), Intensity = 0.42 };
        var observer = new Agent { Position = new Vec2(50, 50), SensorRange = 80 };
        CueSensor.TrySense(
                incident, observer, tick: 3, worldSeed: 42,
                new IncidentSensingProfile(), 400, 400, out var cue)
            .Should().BeTrue();
        cue.Intensity.Should().BeApproximately(0.42, 1e-12);
        cue.Id.Should().Be(0, "cues do not carry a world incident id");
    }
}
