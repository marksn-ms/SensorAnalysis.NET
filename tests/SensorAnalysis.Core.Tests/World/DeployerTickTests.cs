using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Deployers;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Serialization;
using SensorAnalysis.Core.World;
using Xunit;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>Tests for ticking deployers registered on <see cref="SimWorld"/>.</summary>
public class DeployerTickTests
{
    private static DeployContext Ctx(IReadOnlyDictionary<string, object?>? parameters = null, int salt = 0) => new()
    {
        Deployables = BuiltinCatalog.CreateDeployables(),
        Parameters = parameters ?? new Dictionary<string, object?>(StringComparer.Ordinal),
        RandomSalt = salt
    };

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
                    Options = new GaussianDeployOptions { Count = 4, TypeName = "RandomWalk" },
                    Parameters = new Dictionary<string, object?> { ["SensorRange"] = 80.0 }
                },
                new DeployStep
                {
                    CreateDeployer = () => new PeriodicDeployer(),
                    Options = new PeriodicDeployOptions { PeriodTicks = 5, Count = 1, TypeName = "Incident" }
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

    [Fact]
    public void Periodic_NearHost_PlacesWithinHostSensorDisc()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 7);

        var host = new RandomWalkAgent
        {
            Position = new Vec2(100, 100),
            SensorRange = 40
        };
        w.Add(host);

        var periodic = new PeriodicDeployer();
        periodic.Name.Should().Be("Periodic");
        periodic.Attach(w, new PeriodicDeployOptions
        {
            Count = 1,
            NearTypeName = "RandomWalk",
            RangeFactor = 0.5,
            PeriodTicks = 0,
            TypeName = "Incident"
        }, Ctx());

        w.Incidents.Should().HaveCount(1);
        w.Distance(w.Incidents[0].Position, host.Position)
            .Should().BeLessThanOrEqualTo(host.SensorRange * 0.5 * 1.15 + 1e-6);
    }

    [Fact]
    public void Periodic_NearHost_RoundRobin_SpreadsAcrossHosts()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 7);

        var hosts = new[]
        {
            new RandomWalkAgent { Position = new Vec2(40, 40), SensorRange = 40 },
            new RandomWalkAgent { Position = new Vec2(160, 40), SensorRange = 40 },
            new RandomWalkAgent { Position = new Vec2(40, 160), SensorRange = 40 }
        };
        foreach (var host in hosts)
            w.Add(host);

        var periodic = new PeriodicDeployer();
        periodic.Attach(w, new PeriodicDeployOptions
        {
            Count = 3,
            NearTypeName = "RandomWalk",
            RangeFactor = 0.5,
            PeriodTicks = 0,
            RoundRobinHosts = true,
            TypeName = "Incident"
        }, Ctx());

        w.Incidents.Should().HaveCount(3);

        var assigned = w.Incidents
            .Select(inc => hosts.OrderBy(h => w.Distance(inc.Position, h.Position)).First().Id)
            .Distinct()
            .ToList();
        assigned.Should().HaveCount(3);
    }

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
            Options = new GaussianDeployOptions { Count = 2, TypeName = "RandomWalk" },
            Parameters = new Dictionary<string, object?> { ["SensorRange"] = 80.0 }
        }, Ctx(null, 1));

        runner.World.Agents.Should().HaveCount(3);
    }

    [Fact]
    public void DeployerParameterCatalog_DescribesGaussianOptions()
    {
        var options = new GaussianDeployOptions();
        var props = DeployerParameterCatalog.Describe(typeof(GaussianDeployOptions), options);
        props.Should().Contain(p => p.Name == "Count" && p.Required);
        props.Should().Contain(p => p.Name == "TypeName" && p.IsDeployableType);
        DeployerParameterCatalog.TryValidate(options, out _).Should().BeTrue();
    }

    [Fact]
    public void DeployableParameterCatalog_DescribesAgentDefaults()
    {
        var props = DeployableParameterCatalog.Describe(typeof(AgentDeployDefaults));
        props.Should().Contain(p => p.Name == "SensorRange");
        props.Should().Contain(p => p.Name == "MaxShareEvent");
    }

    [Fact]
    public void UniformDeployer_MinDistance_SpacesIncidentsApart()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 99);

        var uniform = new UniformDeployer();
        uniform.Attach(w, new UniformDeployOptions
        {
            Count = 4,
            TypeName = "Incident",
            MinDistance = 80
        }, Ctx());

        w.Incidents.Should().HaveCount(4);
        for (int i = 0; i < w.Incidents.Count; i++)
        {
            for (int j = i + 1; j < w.Incidents.Count; j++)
            {
                w.Distance(w.Incidents[i].Position, w.Incidents[j].Position)
                    .Should().BeGreaterThanOrEqualTo(80 - 1e-6);
            }
        }
    }

    [Fact]
    public void AgentDeployableFactory_AppliesMaxShareEvent_ToOrbitingCoop()
    {
        var registry = BuiltinCatalog.CreateDeployables();
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(4, 4),
            TileSizePx = new GridSize(25, 25)
        }, seed: 1);

        registry.Deploy(w, "OrbitingCoop", new DeployableSpawnRequest
        {
            Position = new Vec2(10, 10),
            Label = "oc",
            Parameters = new Dictionary<string, object?>
            {
                ["SensorRange"] = 80.0,
                ["MaxShareEvent"] = 1
            },
            Rng = w.Random
        });

        var agent = w.Agents.OfType<OrbitingCoopAgent>().Single();
        agent.MaxShareEvent.Should().Be(1);
    }

    [Fact]
    public void UniformAgentsAndIncidents_DoNotSharePlacementSequence()
    {
        var w = new SimWorld(new WorldOptions
        {
            TileCount = new GridSize(8, 8),
            TileSizePx = new GridSize(25, 25)
        }, seed: 42);

        var uniformAgents = new UniformDeployer();
        uniformAgents.Attach(w, new UniformDeployOptions
        {
            Count = 10,
            TypeName = "RandomWalk"
        }, Ctx(new Dictionary<string, object?> { ["SensorRange"] = 80.0 }));

        var uniformIncidents = new UniformDeployer();
        uniformIncidents.Attach(w, new UniformDeployOptions
        {
            Count = 10,
            TypeName = "Incident"
        }, Ctx());

        w.Agents.Should().HaveCount(10);
        w.Incidents.Should().HaveCount(10);

        for (int i = 0; i < 10; i++)
            w.Agents[i].Position.Should().NotBe(w.Incidents[i].Position);
    }

    [Fact]
    public void RecipeCompiler_AcceptsLegacyDeployerNames()
    {
        var deployers = BuiltinCatalog.CreateDeployers();
        var recipe = new ExperimentRecipe
        {
            Directives =
            [
                new RecipeDirective
                {
                    Deployer = "Random",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["Count"] = 2,
                        ["TypeName"] = "RandomWalk",
                        ["SensorRange"] = 50.0
                    }
                },
                new RecipeDirective
                {
                    Deployer = "UniformIncidents",
                    Parameters = new Dictionary<string, object?> { ["Count"] = 1 }
                },
                new RecipeDirective
                {
                    Deployer = "PlaceNear",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["Count"] = 2,
                        ["NearTypeName"] = "HerdingDeceptive",
                        ["PeriodTicks"] = 0,
                        ["RangeFactor"] = 0.5
                    }
                }
            ]
        };

        var steps = RecipeCompiler.CompileSteps(recipe, deployers);
        steps.Should().HaveCount(3);
        steps[0].Options.Should().BeOfType<UniformDeployOptions>()
            .Which.TypeName.Should().Be("RandomWalk");
        steps[1].Options.Should().BeOfType<UniformDeployOptions>()
            .Which.TypeName.Should().Be("Incident");
        var near = steps[2].Options.Should().BeOfType<PeriodicDeployOptions>().Subject;
        near.NearTypeName.Should().Be("HerdingDeceptive");
        near.PeriodTicks.Should().Be(0);
        near.TypeName.Should().Be("Incident");
    }

    [Fact]
    public void DeployableCapabilities_RejectsIncidentNearAnchor()
    {
        var registry = BuiltinCatalog.CreateDeployables();
        DeployableCapabilities.SupportsNearAnchor(registry.Get("Incident"), out var reason)
            .Should().BeFalse();
        reason.Should().Contain("SensorRange");
    }

    [Fact]
    public void RecipeCompiler_MapsLegacyIncidentKindToIncidentFactory()
    {
        var deployers = BuiltinCatalog.CreateDeployers();
        var recipe = new ExperimentRecipe
        {
            Directives =
            [
                new RecipeDirective
                {
                    Deployer = "Uniform",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["Count"] = 1,
                        ["TypeName"] = "Hotspot"
                    }
                }
            ]
        };

        var steps = RecipeCompiler.CompileSteps(recipe, deployers);
        steps[0].Parameters["TypeName"].Should().Be("Incident");
        steps[0].Parameters["Kind"].Should().Be("Hotspot");
    }
}
