using FluentAssertions;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>Tests for ticking deployers registered on <see cref="SimWorld"/>.</summary>
public class DeployerTickTests
{
    private static DeployContext Ctx(int salt = 0) => new()
    {
        RandomSalt = salt,
        ResolveAgent = _ => new RandomWalkAgent()
    };

    /// <summary>Gaussian + periodic steps spawn waves on schedule.</summary>
    [Fact]
    public void SimulationRunner_RegistersPeriodicWaves_AndSpawnsOnSchedule()
    {
        var runner = new SimulationRunner();
        runner.Reset(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 11);

        runner.Deploy(
            new[]
            {
                new DeployStep
                {
                    CreateDeployer = () => new GaussianDeployer(),
                    Options = new GaussianDeployOptions { Count = 4, TypeName = "RandomWalk" }
                },
                new DeployStep
                {
                    CreateDeployer = () => new PeriodicIncidentDeployer(),
                    Options = new PeriodicIncidentOptions { PeriodTicks = 5, Count = 1 }
                }
            },
            Ctx());

        runner.World!.Agents.Should().HaveCount(4);
        runner.World.Incidents.Should().BeEmpty();

        runner.Step();
        runner.World.Incidents.Should().HaveCount(1);
        for (int i = 0; i < 4; i++)
            runner.Step();
        runner.World.Incidents.Should().HaveCount(1);
        runner.Step();
        runner.World.Incidents.Should().HaveCount(2);
    }

    /// <summary>PlaceNear lands within RangeFactor × host sensor range.</summary>
    [Fact]
    public void PlaceNear_UsesRangeFactorOfHostSensor()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 7);

        var host = new RandomWalkAgent(seed: 1)
        {
            Position = new Vec2(100, 100),
            SensorRange = 40
        };
        // AgentType comes from class name "RandomWalk"
        w.Add(host);

        var near = new PlaceNearDeployer();
        near.Attach(w, new PlaceNearOptions
        {
            Count = 1,
            NearTypeName = "RandomWalk",
            RangeFactor = 0.5,
            PeriodTicks = 0
        }, Ctx());

        w.Incidents.Should().HaveCount(1);
        w.Distance(w.Incidents[0].Position, host.Position)
            .Should().BeLessThanOrEqualTo(host.SensorRange * 0.5 * 1.15 + 1e-6);
    }

    /// <summary>Additive attach does not clear existing agents.</summary>
    [Fact]
    public void AttachDeployer_DoesNotClearExistingEntities()
    {
        var runner = new SimulationRunner();
        runner.Reset(new WorldOptions
        {
            TileCount = new GridSize(6, 6),
            TileSizePx = new GridSize(20, 20)
        }, seed: 3);
        runner.World!.Add(new RandomWalkAgent { Position = new Vec2(10, 10) });

        runner.AttachDeployer(new DeployStep
        {
            CreateDeployer = () => new GaussianDeployer(),
            Options = new GaussianDeployOptions { Count = 2, TypeName = "RandomWalk" }
        }, Ctx(1));

        runner.World.Agents.Should().HaveCount(3);
    }

    /// <summary>Gaussian options expose Count + Type for the UI catalog.</summary>
    [Fact]
    public void DeployerParameterCatalog_DescribesGaussianOptions()
    {
        var props = DeployerParameterCatalog.Describe(typeof(GaussianDeployOptions));
        props.Should().Contain(p => p.Name == "Count" && p.Required);
        props.Should().Contain(p => p.Name == "TypeName" && p.IsAgentType);
        DeployerParameterCatalog.TryValidate(new GaussianDeployOptions(), out _).Should().BeTrue();
    }
}
