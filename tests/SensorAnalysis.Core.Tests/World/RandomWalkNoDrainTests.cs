using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class RandomWalkNoDrainTests
{
    private static SimWorld Tiny() =>
        new(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = true
        }, seed: 1);

    [Fact]
    public void Catalog_RegistersNoDrainRandomWalk()
    {
        BuiltinCatalog.CreateAgents().Names.Should().Contain("RandomWalkNoDrain");
    }

    [Fact]
    public void RandomWalkNoDrain_DoesNotReduceIncidentIntensity()
    {
        var world = Tiny();
        var wanderer = new RandomWalkNoDrainAgent { Position = new Vec2(50, 50), SensorRange = 80 };
        var incident = new Incident
        {
            Position = new Vec2(50, 50),
            Intensity = 1.0,
            DrainPerAgentPerTick = 0.1
        };
        world.Add(wanderer);
        world.Add(incident);

        wanderer.ContributesToIncidentDrain.Should().BeFalse();
        for (int i = 0; i < 10; i++)
            world.Tick();

        incident.Intensity.Should().BeApproximately(1.0, 1e-9);
    }
}
