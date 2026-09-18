using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>HerdingDeceptive latches a guard post and stays on station.</summary>
public class HerdingDeceptiveGuardTests
{
    [Fact]
    public void OrbitsVisibleEvent_WhenAlone()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = true
        }, seed: 3);

        var dog = new HerdingDeceptiveAgent
        {
            Position = new Vec2(200, 200),
            SensorRange = 80
        };
        w.Add(dog);
        w.Add(new Incident
        {
            Position = new Vec2(200, 140),
            Intensity = 1.0
        });

        for (int i = 0; i < 80; i++)
            w.Tick();

        var dist = w.Distance(dog.Position, new Vec2(200, 140));
        dist.Should().BeInRange(20, 60);
    }

    [Fact]
    public void KeepsGuarding_AfterCueDrops()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(10, 10),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = true
        }, seed: 3);

        var dog = new HerdingDeceptiveAgent
        {
            Position = new Vec2(200, 200),
            SensorRange = 80
        };
        var incident = new Incident
        {
            Position = new Vec2(200, 160),
            Intensity = 1.0
        };
        w.Add(dog);
        w.Add(incident);

        for (int i = 0; i < 20; i++)
            w.Tick();

        incident.Intensity = 0;
        for (int i = 0; i < 20; i++)
            w.Tick();

        w.Distance(dog.Position, new Vec2(200, 160)).Should().BeLessThan(70);
    }
}
