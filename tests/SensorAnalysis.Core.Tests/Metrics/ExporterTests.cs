using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Metrics;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.Metrics;

public class ExporterTests
{
    [Fact]
    public void Export_WritesRunMetaAndMetricsCsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sa-export-{Guid.NewGuid():N}");
        try
        {
            var deployables = BuiltinCatalog.CreateDeployables();
            var coverage = new CoverageWatcher(sampleEveryTicks: 1);
            var runner = new SimulationRunner();
            runner.AddWatcher(coverage);
            var manifest = runner.RunExperiment(new ExperimentRequest
            {
                ExperimentName = "export-test",
                BaseSeed = 1,
                Trials = 2,
                TicksPerTrial = 10,
                WorldOptions = new WorldOptions
                {
                    TileCount = new GridSize(4, 4),
                    TileSizePx = new GridSize(20, 20)
                },
                Steps =
                [
                    new DeployStep
                    {
                        CreateDeployer = () => new UniformDeployer(),
                        Options = new UniformDeployOptions
                        {
                            Count = 3,
                            TypeName = "RandomWalk"
                        },
                        Parameters = new Dictionary<string, object?> { ["SensorRange"] = 40.0 }
                    },
                    new DeployStep
                    {
                        CreateDeployer = () => new UniformDeployer(),
                        Options = new UniformDeployOptions
                        {
                            Count = 2,
                            TypeName = "Incident"
                        }
                    }
                ],
                Context = new DeployContext { Deployables = deployables }
            });

            ExperimentExporter.Export(dir, manifest, new[] { coverage });

            File.Exists(Path.Combine(dir, "run-meta.json")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "metrics.csv")).Should().BeTrue();

            var csv = File.ReadAllLines(Path.Combine(dir, "metrics.csv"));
            csv.Length.Should().BeGreaterThan(1);
            csv[0].Should().Contain("Coverage.tick");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
