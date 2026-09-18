using FluentAssertions;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class IncidentDrainRateTests
{
    [Fact]
    public void IncidentFactory_CopiesWorldDrainRateOntoSpawnedIncidents()
    {
        var world = new SimWorld(
            new WorldOptions
            {
                TileCount = new GridSize(10, 10),
                TileSizePx = new GridSize(40, 40),
                DrainPerAgentPerTick = 0.0025
            },
            seed: 1);
        var factory = new IncidentDeployableFactory();
        var obj = factory.Create(new DeployableSpawnRequest
        {
            Position = new Vec2(50, 50),
            Label = "I",
            Parameters = new Dictionary<string, object?>(),
            Rng = new Random(1)
        });
        factory.Install(world, obj);

        world.Incidents.Should().ContainSingle();
        world.Incidents[0].DrainPerAgentPerTick.Should().Be(0.0025);
    }
}
