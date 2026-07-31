using FluentAssertions;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.Metrics;

/// <summary>Tests for <see cref="ExperimentExporter"/> output files.</summary>
public class ExporterTests
{
    /// <summary>Verifies that export writes run-meta.json and a metrics CSV with expected headers.</summary>
    [Fact]
    public void ExperimentExporter_WritesMetaAndCsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coverage = new CoverageWatcher(1);
            var runner = new SimulationRunner();
            runner.AddWatcher(coverage);

            var manifest = runner.RunExperiment(new ExperimentRequest
            {
                ExperimentName = "unit-test",
                BaseSeed = 1,
                Trials = 2,
                TicksPerTrial = 10,
                WorldOptions = new WorldOptions
                {
                    TileCount = new GridSize(4, 4),
                    TileSizePx = new GridSize(20, 20)
                },
                Deployer = new RandomDeployer(),
                DeployRequest = new DeployRequest
                {
                    AgentCount = 3,
                    IncidentCount = 2,
                    AgentTypeName = "RandomWalk",
                    AgentFactory = _ => new RandomWalkAgent(),
                    SensorRange = 40
                }
            });

            ExperimentExporter.Export(dir, manifest, new[] { coverage });

            File.Exists(Path.Combine(dir, "run-meta.json")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "metrics.csv")).Should().BeTrue();

            var csv = File.ReadAllLines(Path.Combine(dir, "metrics.csv"));
            csv.Length.Should().BeGreaterThan(1); // header + rows
            csv[0].Should().Contain("Coverage.tick");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
