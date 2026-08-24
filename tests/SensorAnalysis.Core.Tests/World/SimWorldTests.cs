using FluentAssertions;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>Unit tests for <see cref="SimWorld"/> behavior and deployment.</summary>
public class SimWorldTests
{
    private static SimWorld CreateWorld(int seed = 42) =>
        new(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25),
            ExactSensing = true
        }, seed);

    /// <summary>Verifies identical seeds and agent setups produce identical tick trajectories.</summary>
    [Fact]
    public void Tick_IsDeterministic_WithSameSeedAndAgents()
    {
        var w1 = CreateWorld(99);
        var w2 = CreateWorld(99);

        void Setup(SimWorld w)
        {
            w.Add(new RandomWalkAgent(seed: 1) { Position = new Vec2(50, 50), SensorRange = 40 });
            w.Add(new RandomWalkAgent(seed: 2) { Position = new Vec2(60, 60), SensorRange = 40 });
            w.Add(new Incident { Position = new Vec2(55, 55), Intensity = 1 });
        }

        Setup(w1);
        Setup(w2);

        for (int i = 0; i < 20; i++)
        {
            w1.Tick();
            w2.Tick();
        }

        w1.CurrentTick.Should().Be(w2.CurrentTick);
        w1.Agents[0].Position.Should().Be(w2.Agents[0].Position);
        w1.Agents[1].Position.Should().Be(w2.Agents[1].Position);
    }

    /// <summary>Verifies <see cref="SimWorld.Wrap"/> maps out-of-range coordinates onto the torus.</summary>
    [Fact]
    public void Wrap_MapsCoordinatesOntoToroidalWorld()
    {
        var w = CreateWorld();
        // World is 200×200; -50 wraps to 150.
        var wrapped = w.Wrap(new Vec2(-50, -50));
        wrapped.X.Should().BeApproximately(150, 1e-9);
        wrapped.Y.Should().BeApproximately(150, 1e-9);

        w.Wrap(new Vec2(200, 0)).X.Should().BeApproximately(0, 1e-9);
    }

    /// <summary>Verifies sensing uses shortest toroidal distance across world edges.</summary>
    [Fact]
    public void Observe_SeesEntitiesAcrossToroidalEdge()
    {
        var w = CreateWorld(); // 200×200
        var self = new Agent { Position = new Vec2(5, 100), SensorRange = 20 };
        w.Add(self);
        // Just across the left/right seam from self (near x=0 ↔ x=200).
        w.Add(new Agent { Position = new Vec2(195, 100) });
        w.Add(new Incident { Position = new Vec2(198, 100) });

        var obs = w.Observe(self);
        obs.NearbyAgents.Should().HaveCount(1);
        obs.NearbyIncidents.Should().HaveCount(1);
        w.Distance(self.Position, new Vec2(195, 100)).Should().BeApproximately(10, 1e-9);
    }

    /// <summary>Verifies observations only include entities within the agent's sensor range.</summary>
    [Fact]
    public void Observe_OnlyReturnsEntitiesWithinSensorRange()
    {
        var w = CreateWorld();
        var self = new Agent { Position = new Vec2(50, 50), SensorRange = 30 };
        w.Add(self);
        w.Add(new Agent { Position = new Vec2(55, 55) }); // near
        w.Add(new Agent { Position = new Vec2(200, 200) }); // far on torus (~70)
        w.Add(new Incident { Position = new Vec2(60, 50) }); // near
        w.Add(new Incident { Position = new Vec2(150, 150) }); // far

        var obs = w.Observe(self);
        obs.NearbyAgents.Should().HaveCount(1);
        obs.NearbyIncidents.Should().HaveCount(1);
    }

    /// <summary>Verifies a deployer places the requested agent and incident counts.</summary>
    [Fact]
    public void Deployer_PlacesRequestedCounts()
    {
        var w = CreateWorld();
        var deployer = new RandomDeployer();
        deployer.Attach(w, new RandomDeployOptions
        {
            Count = 7,
            TypeName = "RandomWalk",
            SensorRange = 50
        }, new DeployContext
        {
            ResolveAgent = _ => new RandomWalkAgent()
        });
        new UniformIncidentDeployer().Attach(w, new UniformIncidentOptions { Count = 3 },
            new DeployContext { ResolveAgent = _ => new RandomWalkAgent() });

        w.Agents.Should().HaveCount(7);
        w.Incidents.Should().HaveCount(3);
    }

    /// <summary>Verifies incident intensity decreases when an in-range agent ticks.</summary>
    [Fact]
    public void Incident_DrainsWhenAgentInRange()
    {
        var w = CreateWorld();
        var incident = new Incident
        {
            Position = new Vec2(50, 50),
            Intensity = 1.0,
            DrainPerAgentPerTick = 0.1
        };
        w.Add(incident);
        w.Add(new Agent { Position = new Vec2(50, 50), SensorRange = 20 });

        for (int i = 0; i < 5; i++)
            w.Tick();

        incident.Intensity.Should().BeLessThan(1.0);
    }

    /// <summary>Only the closest N in-range agents contribute to drain; extras do not accelerate clearance.</summary>
    [Fact]
    public void Incident_DrainCapsAtClosestContributors()
    {
        var w = CreateWorld();
        w.Options.MaxDrainingAgentsPerIncident = 3;
        var incident = new Incident
        {
            Position = new Vec2(50, 50),
            Intensity = 1.0,
            DrainPerAgentPerTick = 0.1,
            MultiAgentDrainExponent = 1.0
        };
        w.Add(incident);

        // Five agents in range; only three closest should count (linear exponent ⇒ 0.3/tick).
        w.Add(new Agent { Position = new Vec2(50, 50), SensorRange = 20 });   // d=0
        w.Add(new Agent { Position = new Vec2(52, 50), SensorRange = 20 });   // d=2
        w.Add(new Agent { Position = new Vec2(54, 50), SensorRange = 20 });   // d=4
        w.Add(new Agent { Position = new Vec2(60, 50), SensorRange = 20 });   // d=10 — excluded
        w.Add(new Agent { Position = new Vec2(65, 50), SensorRange = 20 });   // d=15 — excluded

        w.Tick();
        incident.Intensity.Should().BeApproximately(0.7, 1e-9);

        // Move the two far agents closer so all five qualify by range but cap stays at 3.
        w.Agents[3].Position = new Vec2(51, 50); // d=1 — now among closest three
        w.Agents[4].Position = new Vec2(53, 50); // d=3 — still outside top 3
        w.Tick();
        incident.Intensity.Should().BeApproximately(0.4, 1e-9);
    }

    /// <summary>World-echo remaining life is 1 at birth and tracks TTL linearly to 0.</summary>
    [Fact]
    public void MessageEcho_RemainingLifeTracksTtl()
    {
        var w = CreateWorld();
        var sender = new Agent { Position = new Vec2(50, 50), SensorRange = 40, AgentType = "OrbitingCoop" };
        w.Add(sender);
        w.Transmit(sender, "contact", ttl: 10);

        w.Messages.Should().HaveCount(1);
        w.Messages[0].RemainingLife.Should().BeApproximately(1.0, 1e-9);

        for (int i = 0; i < 5; i++)
            w.Tick();

        w.Messages.Should().HaveCount(1);
        w.Messages[0].Ttl.Should().Be(5);
        w.Messages[0].RemainingLife.Should().BeApproximately(0.5, 1e-9);
    }
}
