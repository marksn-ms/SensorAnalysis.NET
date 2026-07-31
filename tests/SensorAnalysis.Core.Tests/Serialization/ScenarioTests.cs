using FluentAssertions;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.Serialization;

/// <summary>Tests for scenario document round-trip serialization.</summary>
public class ScenarioTests
{
    /// <summary>Verifies save/load preserves world title, seed, and entity placements.</summary>
    [Fact]
    public void RoundTrip_PreservesPlacements()
    {
        var world = new SimWorld(new WorldOptions
        {
            Title = "Test",
            TileCount = new GridSize(6, 6),
            TileSizePx = new GridSize(20, 20)
        }, seed: 7);

        world.Add(new GreedyIncidentAgent { Position = new Vec2(10, 15), SensorRange = 50, Label = "A" });
        world.Add(new Incident { Position = new Vec2(30, 40), Intensity = 0.8, Label = "I" });

        var path = Path.Combine(Path.GetTempPath(), $"sa-scenario-{Guid.NewGuid():N}.json");
        try
        {
            var doc = ScenarioDocument.FromWorld(world);
            ScenarioSerializer.Save(path, doc);
            var loaded = ScenarioSerializer.Load(path);

            loaded.Title.Should().Be("Test");
            loaded.Seed.Should().Be(7);
            loaded.Agents.Should().ContainSingle(a => a.Label == "A" && a.X == 10 && a.Y == 15);
            loaded.Incidents.Should().ContainSingle(i => i.Label == "I" && i.Intensity == 0.8);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
