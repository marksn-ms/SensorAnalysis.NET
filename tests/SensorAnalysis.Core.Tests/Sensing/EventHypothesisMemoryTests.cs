using FluentAssertions;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Sensing;
using Xunit;

namespace SensorAnalysis.Core.Tests.Sensing;

public class EventHypothesisMemoryTests
{
    [Fact]
    public void Rumor_SurvivesAMissedTick_ThenDecaysAwayWithoutConfirmation()
    {
        var mem = new EventHypothesisMemory
        {
            DecayPerTick = 0.2,
            DropBelow = 0.15,
            StaleGraceTicks = 3,
            RumorBump = 0.32
        };

        mem.TickAge();
        var h = mem.IngestRumor(new Vec2(10, 10), fromAgentId: 7, 100, 100);
        h.Confidence.Should().BeGreaterThan(0.2);
        mem.DecayAndPrune();
        mem.Items.Should().HaveCount(1);

        for (int i = 0; i < 12; i++)
        {
            mem.TickAge();
            mem.DecayAndPrune();
        }

        mem.Items.Should().BeEmpty();
    }

    [Fact]
    public void NearbyCue_MergesIntoTheSameTrack()
    {
        var mem = new EventHypothesisMemory { AssociationRadius = 20 };
        mem.IngestRumor(new Vec2(0, 0), 1, 400, 400);
        mem.IngestCue(new Vec2(5, 0), strength: 0.8, 400, 400);
        mem.Items.Should().HaveCount(1);
        mem.Items[0].LastSource.Should().Be(HypothesisSource.Sensor);
    }
}
