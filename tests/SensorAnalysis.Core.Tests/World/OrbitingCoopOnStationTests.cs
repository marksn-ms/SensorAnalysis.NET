using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>On-station orbiters keep moving; they do not Stay-hitch on the ring.</summary>
public class OrbitingCoopOnStationTests
{
    [Fact]
    public void TwoOrbiters_DoNotHaltOnStation()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = true
        }, seed: 4);

        var a = new OrbitingCoopAgent { Position = new Vec2(200, 165), SensorRange = 80 };
        var b = new OrbitingCoopAgent { Position = new Vec2(200, 235), SensorRange = 80 };
        w.Add(a);
        w.Add(b);
        w.Add(new Incident { Position = new Vec2(200, 200), Intensity = 1.0, DrainPerAgentPerTick = 0.0001 });

        var stay = 0;
        const int ticks = 80;
        for (int i = 0; i < ticks; i++)
        {
            w.Tick();
            if (a.LastAction == AgentAction.Stay)
                stay++;
            if (b.LastAction == AgentAction.Stay)
                stay++;
        }

        ((double)stay / (ticks * 2)).Should().BeLessThan(0.15);
        w.Distance(a.Position, new Vec2(200, 200)).Should().BeLessThan(70);
        w.Distance(b.Position, new Vec2(200, 200)).Should().BeLessThan(70);
    }
}
