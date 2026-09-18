using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

public class HerdingDeceptiveIdleMotionTests
{
    [Fact]
    public void IdleHerdingDeceptive_MostlyMovesLikeCorrelatedWalk()
    {
        var world = new SimWorld(
            new WorldOptions
            {
                TileCount = new GridSize(10, 10),
                TileSizePx = new GridSize(40, 40),
                ExactSensing = true
            },
            seed: 7);
        var agent = new HerdingDeceptiveAgent
        {
            Position = new Vec2(200, 200),
            SensorRange = 80
        };
        world.Add(agent);

        var stay = 0;
        const int ticks = 500;
        for (var i = 0; i < ticks; i++)
        {
            world.Tick();
            if (agent.LastAction == AgentAction.Stay)
                stay++;
        }

        // Correlated walk stays with mass 0.004; the old loiter stayed 4/5 of ticks.
        ((double)stay / ticks).Should().BeLessThan(0.05);
    }
}
