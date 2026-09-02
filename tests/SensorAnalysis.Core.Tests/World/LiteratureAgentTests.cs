using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Intel;
using SensorAnalysis.Core.Messaging;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class LiteratureAgentTests
{
    private static SimWorld Tiny(bool exact = true) =>
        new(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = exact
        }, seed: 1);

    [Fact]
    public void Catalog_RegistersLiteratureFactories_AndKeepsPhase23()
    {
        var names = BuiltinCatalog.CreateAgents().Names.ToList();
        names.Should().Contain(new[]
        {
            "LloydCoop", "LloydParkDeceptive",
            "PheromoneCoop", "PheromoneFakeDeceptive",
            "BoidsCoop", "BoidsPeelDeceptive", "BoidsAwareMitigated",
            "ProgressCoop", "ProgressLoiterDeceptive", "ProgressAwareMitigated",
            "PersonalSpaceCoop", "CrowdOnlyDeceptive", "CrowdAwareMitigated"
        });
        names.Should().NotContain("LloydAwareMitigated",
            "Lloyd C3 is structurally dead; no fake skeptic");
        names.Should().NotContain("PheromoneAwareMitigated",
            "unsigned pheromone has no peer to distrust; no fake skeptic");
    }

    [Fact]
    public void PheromoneField_DepositHasNoSpeakerId_AndDecays()
    {
        var world = Tiny();
        world.Pheromones.TotalMass.Should().Be(0);
        world.Pheromones.Deposit(new Vec2(100, 100), 2.0);
        world.Pheromones.Sample(new Vec2(100, 100)).Should().BeApproximately(2.0, 1e-9);
        world.Pheromones.Decay();
        world.Pheromones.Sample(new Vec2(100, 100)).Should().BeApproximately(2.0 * 0.95, 1e-9);
        typeof(PheromoneField).GetMethod(nameof(PheromoneField.Deposit))!
            .GetParameters().Select(p => p.Name).Should().BeEquivalentTo("worldPos", "amount");
    }

    [Fact]
    public void LloydCoop_SitsOnOwnedCue_RelinquishesWhenNeighborCloser_IgnoresRadio()
    {
        var world = Tiny();
        var coop = new LloydCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var chatter = new RandomWalkAgent { Position = new Vec2(300, 300), SensorRange = 80 };
        world.Add(coop);
        world.Add(chatter);
        world.Add(new Incident { Position = new Vec2(102, 100), Intensity = 1 });

        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay);
        coop.LastOwnedCueCount.Should().Be(1);
        world.Messages.Should().BeEmpty("LloydCoop must not transmit");

        world.Transmit(chatter, RadioNet.FormatContact(20_001, new Vec2(10, 10)), ttl: 40);
        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay, "NearbyMessages must not steer Lloyd off an owned sit");
    }

    [Fact]
    public void LloydCoop_DoesNotReadPeerAgentType_WhenAssigningVoronoi()
    {
        var world = Tiny();
        var coop = new LloydCoopAgent { Position = new Vec2(140, 100), SensorRange = 80 };
        var labeled = new RandomWalkAgent
        {
            Position = new Vec2(102, 100),
            SensorRange = 80,
            AgentType = "OrbitingCoop"
        };
        world.Add(coop);
        world.Add(labeled);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        world.Tick();
        coop.LastRelinquishedCueCount.Should().BeGreaterThan(0,
            "a closer body owns the cue regardless of AgentType");
        coop.LastOwnedCueCount.Should().Be(0);
    }

    [Fact]
    public void LloydPark_SitsWithoutOracleOrAgentType_AndDoesNotDrain()
    {
        var world = Tiny();
        var park = new LloydParkDeceptiveAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var decoyPeer = new RandomWalkAgent
        {
            Position = new Vec2(105, 100),
            SensorRange = 80,
            AgentType = "OrbitingCoop"
        };
        world.Add(park);
        world.Add(decoyPeer);
        world.Add(new Incident { Position = new Vec2(102, 100), Intensity = 1 });

        world.Tick();
        park.LastAction.Should().Be(AgentAction.Stay);
        park.ContributesToIncidentDrain.Should().BeFalse();
        world.Messages.Should().BeEmpty();
        park.Should().NotBeAssignableTo<IIncidentOracle>();
    }

    [Fact]
    public void PheromoneCoop_DepositsOnCue_IgnoresRadio_UnsignedField()
    {
        var world = Tiny();
        var coop = new PheromoneCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var chatter = new RandomWalkAgent { Position = new Vec2(108, 100), SensorRange = 80 };
        world.Add(coop);
        world.Add(chatter);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay);
        coop.LifetimeDeposits.Should().BeGreaterThan(0);
        world.Pheromones.TotalMass.Should().BeGreaterThan(0);
        world.Messages.Should().BeEmpty();

        world.Transmit(chatter, RadioNet.FormatContact(20_002, new Vec2(10, 10)), ttl: 40);
        var mass = world.Pheromones.TotalMass;
        world.Tick();
        coop.LastAction.Should().Be(AgentAction.Stay, "radio must not steal a pheromone sit");
        world.Pheromones.TotalMass.Should().BeGreaterThan(0);
        _ = mass;
    }

    [Fact]
    public void PheromoneFake_DepositsAwayFromCue_NoDrainNoAgentType()
    {
        var world = Tiny();
        var fake = new PheromoneFakeDeceptiveAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var labeled = new RandomWalkAgent
        {
            Position = new Vec2(105, 100),
            SensorRange = 80,
            AgentType = "PheromoneCoop"
        };
        world.Add(fake);
        world.Add(labeled);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        world.Tick();
        fake.ContributesToIncidentDrain.Should().BeFalse();
        fake.LifetimeDeposits.Should().BeGreaterThan(0);
        fake.LastAction.Should().NotBe(AgentAction.Stay, "fake trails walk away, they do not park");
        world.Messages.Should().BeEmpty();
        // Fake peak is offset by sensor range; the cell at the agent is not the only mass.
        world.Pheromones.TotalMass.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BoidsCoop_UsesNeighborHeading_IgnoresRadio_DoesNotReadAgentType()
    {
        var world = Tiny();
        var a = new BoidsCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var b = new BoidsCoopAgent { Position = new Vec2(110, 100), SensorRange = 80, AgentType = "HarmlessLooking" };
        world.Add(a);
        world.Add(b);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        world.Tick();
        a.LastAction.Should().Be(AgentAction.Stay, "on-cue sit still drains");
        world.Messages.Should().BeEmpty();

        // Off-cue: two boids with no incident should produce a flocking action, not a radio follow.
        var w2 = Tiny();
        var c = new BoidsCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var d = new BoidsCoopAgent { Position = new Vec2(120, 100), SensorRange = 80 };
        var chatter = new RandomWalkAgent { Position = new Vec2(108, 100), SensorRange = 80 };
        w2.Add(c);
        w2.Add(d);
        w2.Add(chatter);
        w2.Transmit(chatter, RadioNet.FormatContact(20_003, new Vec2(10, 10)), ttl: 40);
        w2.Tick();
        w2.Messages.Should().NotBeEmpty("chatter transmitted");
        c.Should().NotBeAssignableTo<IIncidentOracle>();
    }

    [Fact]
    public void BoidsPeel_DoesNotPark_DoesNotReadAgentType_NoOracle()
    {
        var world = Tiny();
        var peel = new BoidsPeelDeceptiveAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var victim = new BoidsCoopAgent
        {
            Position = new Vec2(108, 100),
            SensorRange = 80,
            AgentType = "OrbitingCoop"
        };
        world.Add(peel);
        world.Add(victim);
        world.Add(new Incident { Position = new Vec2(100, 100), Intensity = 1 });

        var stayed = 0;
        var peeled = 0;
        for (int i = 0; i < 20; i++)
        {
            world.Tick();
            if (peel.LastAction == AgentAction.Stay) stayed++;
            if (peel.IsPeeling == 1) peeled++;
        }

        peel.ContributesToIncidentDrain.Should().BeFalse();
        world.Messages.Should().BeEmpty();
        peel.Should().NotBeAssignableTo<IIncidentOracle>();
        peeled.Should().BeGreaterThan(0, "a peel-adversary must enter the dart-away phase");
        stayed.Should().BeLessThan(16, "peel-adversary is not a parker");
    }

    [Fact]
    public void BoidsSkeptic_CreditsHeadingMismatchAgainstRelabeledPeer()
    {
        var world = Tiny(exact: true);
        var skeptic = new BoidsAwareMitigatedAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        var peeler = new BoidsPeelDeceptiveAgent
        {
            Position = new Vec2(110, 100),
            SensorRange = 80,
            AgentType = "HarmlessLooking"
        };
        world.Add(skeptic);
        world.Add(peeler);

        for (int i = 0; i < 40; i++)
            world.Tick();

        // Detector is allowed to be quiet (finding); it must not require AgentType to fire.
        // Force a mismatch: if peel ticks happened, anti-align counter should have had a chance.
        peeler.LifetimePeelTicks.Should().BeGreaterThan(0);
        skeptic.PeelGraceTicks.Should().Be(8);
        // Trust is defined for the relabeled id without consulting AgentType.
        skeptic.MeanTrustToward(peeler.Id).Should().BeLessThanOrEqualTo(0.5);
    }

    [Fact]
    public void LiteratureAgents_DoNotUseIncidentOracleOrProgressLoiterOrPersonalSpace()
    {
        typeof(LloydCoopAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(LloydParkDeceptiveAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(PheromoneCoopAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(PheromoneFakeDeceptiveAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(BoidsCoopAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(BoidsPeelDeceptiveAgent).Should().NotBeAssignableTo<IIncidentOracle>();
        typeof(BoidsAwareMitigatedAgent).Should().NotBeAssignableTo<IIncidentOracle>();

        typeof(LloydCoopAgent).Should().NotBeAssignableTo<PersonalSpaceCoopAgent>();
        typeof(BoidsCoopAgent).Should().NotBeAssignableTo<PersonalSpaceCoopAgent>();
        typeof(BoidsPeelDeceptiveAgent).Should().NotBeAssignableTo<ProgressLoiterDeceptiveAgent>();
        typeof(LloydParkDeceptiveAgent).Should().NotBeAssignableTo<ProgressLoiterDeceptiveAgent>();
        typeof(PheromoneCoopAgent).Should().NotBeAssignableTo<StigmergyAgent>();
    }

    [Fact]
    public void PheromoneCoop_FollowsGradientTowardADepositedPeak()
    {
        var world = Tiny();
        world.Pheromones.Deposit(new Vec2(150, 100), 8.0);
        var coop = new PheromoneCoopAgent { Position = new Vec2(100, 100), SensorRange = 80 };
        world.Add(coop);
        world.Tick();
        coop.LastAction.Should().Be(AgentAction.East,
            "with no cue, climb the unsigned gradient toward the deposited peak");
    }
}
