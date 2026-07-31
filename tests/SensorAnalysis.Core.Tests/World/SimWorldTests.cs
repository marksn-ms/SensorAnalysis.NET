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
            TileSizePx = new GridSize(25, 25)
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

    /// <summary>Verifies <see cref="SimWorld.Clamp"/> keeps coordinates inside world bounds.</summary>
    [Fact]
    public void Clamp_KeepsAgentsInsideWorld()
    {
        var w = CreateWorld();
        var agent = new Agent { Position = new Vec2(10, 10), StepSize = 100 };
        // Force a huge step westward via Stay then manual position
        w.Add(agent);
        agent.Position = w.Clamp(new Vec2(-50, -50));
        agent.Position.X.Should().BeGreaterThanOrEqualTo(0);
        agent.Position.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    /// <summary>Verifies observations only include entities within the agent's sensor range.</summary>
    [Fact]
    public void Observe_OnlyReturnsEntitiesWithinSensorRange()
    {
        var w = CreateWorld();
        var self = new Agent { Position = new Vec2(50, 50), SensorRange = 30 };
        w.Add(self);
        w.Add(new Agent { Position = new Vec2(55, 55) }); // near
        w.Add(new Agent { Position = new Vec2(200, 200) }); // far
        w.Add(new Incident { Position = new Vec2(60, 50) }); // near
        w.Add(new Incident { Position = new Vec2(300, 300) }); // far

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
        deployer.Deploy(w, new DeployRequest
        {
            AgentCount = 7,
            IncidentCount = 3,
            AgentTypeName = "RandomWalk",
            AgentFactory = _ => new RandomWalkAgent(),
            SensorRange = 50
        });

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
}
