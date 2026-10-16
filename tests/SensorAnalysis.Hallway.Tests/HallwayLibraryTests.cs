using FluentAssertions;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.Plugins;
using SensorAnalysis.Core.World;
using SensorAnalysis.Hallway;
using Xunit;

namespace SensorAnalysis.Hallway.Tests;

/// <summary>Floor plan geometry, passage features, reflection import, and one short trial.</summary>
public class HallwayLibraryTests
{
    public HallwayLibraryTests() => HallwaySession.Reset();

    /// <summary>A doorway is a gap: the door center is outside every wall.</summary>
    [Fact]
    public void DoorCenter_ClearsTheWallCapsules()
    {
        var plan = FloorPlan.Build(FloorPlanSpec.Compact);
        plan.Rooms.Count.Should().BeGreaterThan(1);
        plan.Walls.Should().NotBeEmpty();

        foreach (var room in plan.Rooms)
        {
            var clearance = plan.Walls.Min(w => w.DistanceToSurface(room.Door, 10_000, 10_000));
            clearance.Should().BeGreaterThan(plan.Spec.DoorWidth * 0.25);
        }
    }

    /// <summary>The route ends in the destination room and starts at the origin door.</summary>
    [Fact]
    public void Route_RunsFromOriginDoorToDestinationCenter()
    {
        var plan = FloorPlan.Build(FloorPlanSpec.Compact);
        var route = RoutePlanner.Plan(plan, 0, plan.Rooms.Count - 1, bodyRadius: 8);
        route[0].Should().Be(plan.Rooms[0].Door);
        route[^1].Should().Be(plan.Rooms[^1].Center);
    }

    /// <summary>A head-on contact is closing with no bearing rate. A offset pass has a miss.</summary>
    [Fact]
    public void Approach_SeparatesHeadOnFromAPassingContact()
    {
        var headOn = PassageGeometry.Approach(new Vec2(100, 0), new Vec2(-10, 0));
        headOn.RangeRate.Should().BeApproximately(-10, 1e-6);
        headOn.BearingRate.Should().BeApproximately(0, 1e-6);
        headOn.Miss.Should().BeApproximately(0, 1e-6);

        var passing = PassageGeometry.Approach(new Vec2(100, 30), new Vec2(-10, 0));
        passing.Miss.Should().BeApproximately(30, 1e-6);
        passing.BearingRate.Should().BeGreaterThan(0);
    }

    /// <summary>A better team fitness replaces the champion. A worse one does not.</summary>
    [Fact]
    public void Population_KeepsAnImprovement()
    {
        var population = HallwaySession.Population;
        population.EnsureSeed(3);
        var prior = population.WeightsForThisTrial();
        prior.Should().Equal(HallwayPopulation.Prior);

        population.Close(10);
        population.Generation.Should().Be(1);
        var mutant = population.WeightsForThisTrial();
        mutant.Should().NotEqual(prior);

        population.Close(50);
        population.ChampionFitness.Should().Be(50);
        population.Champion.Should().Equal(mutant);

        var accepted = population.Champion.ToArray();
        population.Close(1);
        population.Champion.Should().Equal(accepted);
        population.ChampionFitness.Should().Be(50);
    }

    /// <summary>The hallway assembly is a research library: reflection finds its deployer, agent, and watcher.</summary>
    [Fact]
    public void Reflection_ImportsTheHallwayLibrary()
    {
        var path = typeof(FloorPlanDeployer).Assembly.Location;
        var import = ResearchLibraryLoader.Import(new[] { path });

        import.AgentFactories.Should().Contain(f => f.Name == "HallwayStudent");
        import.DeployerFactories.Select(f => f().Name).Should().Contain("FloorPlan");
        import.CreateWatchers().Should().Contain(w => w.Name == "Hallway");

        var agents = new SensorAnalysis.Core.Agents.AgentFactoryRegistry();
        var deployables = new DeployableRegistry();
        var deployers = new Dictionary<string, Func<IDeployer>>(StringComparer.OrdinalIgnoreCase);
        import.Register(agents, deployables, deployers);
        deployables.Names.Should().Contain("HallwayStudent");
        deployers.Keys.Should().Contain("FloorPlan");
    }

    /// <summary>Students are placed off the walls, move along the plan, and a trial closes one generation.</summary>
    [Fact]
    public void Trial_PlacesStudentsAndRecordsAScore()
    {
        var deployer = new FloorPlanDeployer();
        var options = (FloorPlanDeployOptions)deployer.CreateOptions();
        options.StudentsPerRoom = 1;
        var spec = options.ToSpec();
        var world = WorldFor(spec);

        deployer.Attach(world, options, new DeployContext { Deployables = new DeployableRegistry() });

        world.Bodies.Should().NotBeEmpty();
        world.Bodies.Should().OnlyContain(b => b.Shape == BodyShape.Segment);
        world.Incidents.Should().OnlyContain(i => i.IncidentType == "Classroom" && i.DrainPerAgentPerTick == 0);
        var students = world.Agents.OfType<HallwayStudentAgent>().ToList();
        students.Should().NotBeEmpty();
        students.Select(s => s.Weights).Distinct().Should().ContainSingle();

        foreach (var student in students)
        {
            var clearance = world.Bodies.Min(b =>
                b.DistanceToSurface(student.Position, world.WorldWidthPx, world.WorldHeightPx));
            clearance.Should().BeGreaterThan(student.BodyRadius);
        }

        var watcher = new HallwayScoreWatcher(sampleEveryTicks: 1);
        var runner = new SimulationRunner();
        runner.AddWatcher(watcher);
        runner.Reset(world.Options, world.Seed);
        // The runner builds a fresh world. Deploy again inside that world.
        runner.World!.ClearEntities();
        deployer = new FloorPlanDeployer();
        deployer.Attach(runner.World, options, new DeployContext { Deployables = new DeployableRegistry() });
        watcher.OnTrialStart(runner.World, 0);
        runner.RunTicks(20);
        watcher.OnTrialEnd(runner.World, 0);

        var after = runner.World.Agents.OfType<HallwayStudentAgent>().ToList();
        after.Average(s => s.DistanceTraveled).Should().BeGreaterThan(10);
        watcher.Rows.Should().NotBeEmpty();
        HallwaySession.Population.Generation.Should().Be(1);
    }

    private static SimWorld WorldFor(FloorPlanSpec spec)
    {
        const int tile = 12;
        return new SimWorld(new WorldOptions
        {
            Title = "Hall",
            TileCount = new GridSize(
                (int)Math.Ceiling(spec.RequiredWidth / tile),
                (int)Math.Ceiling(spec.RequiredHeight / tile)),
            TileSizePx = new GridSize(tile, tile)
        }, seed: 7);
    }
}
