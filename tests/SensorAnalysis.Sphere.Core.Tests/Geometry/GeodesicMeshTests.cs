using FluentAssertions;
using SensorAnalysis.Sphere.Core.Geometry;
using SensorAnalysis.Sphere.Core.Serialization;
using SensorAnalysis.Sphere.Core.Simulation;
using Xunit;

namespace SensorAnalysis.Sphere.Core.Tests.Geometry;

/// <summary>Validates geodesic mesh topology, continuous motion, and directive behavior.</summary>
public sealed class GeodesicMeshTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Create_HasExpectedFaceCountAndThreeNeighborsPerFace(int resolution)
    {
        var mesh = GeodesicMesh.Create(resolution);

        mesh.Faces.Should().HaveCount(20 * resolution * resolution);
        mesh.Faces.Should().OnlyContain(face => face.Neighbors.Count == 3);
        mesh.Faces.Sum(face => face.AreaSteradians).Should().BeApproximately(4 * Math.PI, 1e-9);
    }

    [Fact]
    public void RangeGuardrail_RejectsRangesBeyondFiveHops()
    {
        var mesh = GeodesicMesh.Create(4);
        var radius = 1_000d;
        var edgeLength = radius * mesh.CharacteristicEdgeRadians;

        var action = () => new SphericalWorld(new SphericalWorldOptions
        {
            Radius = radius,
            Resolution = 4,
            SensorRange = edgeLength * 5.01,
            CommunicationRange = edgeLength
        }, seed: 1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ContinuousRandomWalk_MovesApproximatelyOneStepSize()
    {
        var world = new SphericalWorld(new SphericalWorldOptions
        {
            Radius = 1_000,
            Resolution = 4,
            SensorRange = 100,
            StepSize = 1
        }, seed: 42);
        var agent = world.AddAgent(new SphereVector(0, 1, 0), SphericalAgentKind.RandomWalk, stepSize: 1);
        var before = agent.Position;

        world.Tick();

        var distance = SphereVector.GeodesicDistance(before, agent.Position, world.Options.Radius);
        distance.Should().BeApproximately(1, 1e-6);
        agent.Position.Length.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Tick_AccumulatesAreaWeightedCoverageAndRunsDeployers()
    {
        var world = new SphericalWorld(new SphericalWorldOptions { Resolution = 2, Radius = 1_000, SensorRange = 100 }, seed: 42);
        world.AddRandomAgent();
        world.AddDeployer(new PeriodicIncidentDeployer(new PeriodicIncidentOptions { PeriodTicks = 1, Count = 2 }));

        world.Tick();

        world.CoverageFraction.Should().BeGreaterThan(0);
        world.Incidents.Should().HaveCount(2);
        world.GetMetrics().ActiveIncidents.Should().Be(2);
    }

    [Fact]
    public void CooperativeAgent_LearnsIncidentOnFirstTick()
    {
        var world = new SphericalWorld(new SphericalWorldOptions
        {
            Resolution = 1,
            Radius = 1_000,
            SensorRange = 2_000,
            CommunicationRange = 2_000
        }, seed: 42);
        var agent = world.AddAgent(
            world.Mesh.Faces[0].Center,
            SphericalAgentKind.CooperativeSearch,
            sensorRange: 2_000,
            communicationRange: 2_000,
            stepSize: 0);
        var incident = world.AddIncident(world.Mesh.Faces[0].Center);

        world.Tick();

        agent.KnownIncidentPositions.Should().ContainKey(incident.Id);
        agent.ActionInterval.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AddAgent_SamplesDistinctActionIntervals()
    {
        var world = new SphericalWorld(new SphericalWorldOptions
        {
            Resolution = 1,
            Radius = 1_000,
            SensorRange = 100
        }, seed: 42);

        for (var i = 0; i < 10; i++)
            world.AddRandomAgent();

        world.Agents.Select(agent => agent.ActionInterval).Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void CooperativeTick_RelaysSensedIncidentPositionToCommunicationNeighbor()
    {
        var world = new SphericalWorld(new SphericalWorldOptions
        {
            Resolution = 1,
            Radius = 1_000,
            SensorRange = 2_000,
            CommunicationRange = 2_000
        }, seed: 42);
        var neighborFaceId = world.Mesh.Faces[0].Neighbors[0];
        var sensingAgent = world.AddAgent(
            world.Mesh.Faces[0].Center,
            SphericalAgentKind.CooperativeSearch,
            sensorRange: 2_000,
            communicationRange: 2_000,
            stepSize: 0);
        var receiver = world.AddAgent(
            world.Mesh.Faces[neighborFaceId].Center,
            SphericalAgentKind.CooperativeSearch,
            sensorRange: 2_000,
            communicationRange: 2_000,
            stepSize: 0);
        var incident = world.AddIncident(world.Mesh.Faces[0].Center);
        sensingAgent.TicksSinceRelay = sensingAgent.ActionInterval;

        world.Tick();
        sensingAgent.KnownIncidentPositions.Should().ContainKey(incident.Id);
        receiver.KnownIncidentPositions.Should().ContainKey(incident.Id);
    }

    [Fact]
    public void UniformAgentDeployer_AssignsRequestedBehavior()
    {
        var world = new SphericalWorld(new SphericalWorldOptions { Resolution = 1, Radius = 1_000, SensorRange = 100 }, seed: 42);

        new UniformAgentDeployer(new UniformAgentOptions { Count = 3, Kind = SphericalAgentKind.Stationary }).Tick(world);

        world.Agents.Should().HaveCount(3);
        world.Agents.Should().OnlyContain(agent => agent.Kind == SphericalAgentKind.Stationary);
    }

    [Fact]
    public void NearAgentIncident_DrainsAndEmitsResolutionMessage()
    {
        var world = new SphericalWorld(new SphericalWorldOptions
        {
            Resolution = 2,
            Radius = 1_000,
            SensorRange = 250,
            CommunicationRange = 250,
            DrainPerHelper = 0.5
        }, seed: 7);
        var host = world.AddAgent(world.Mesh.Faces[0].Center, SphericalAgentKind.Stationary, sensorRange: 250);
        var incident = world.AddIncidentNear(host, host.SensorRange);

        for (var i = 0; i < 5; i++) world.Tick();

        incident.IsDepleted.Should().BeTrue();
        world.GetMetrics().DepletedIncidents.Should().Be(1);
        world.Messages.Should().Contain(message =>
            message.Kind == SphericalMessageKind.Resolved && message.IncidentId == incident.Id);
    }

    [Fact]
    public void ScheduledDirective_DeploysAgentsOnItsTargetTick()
    {
        var world = new SphericalWorld(new SphericalWorldOptions { Resolution = 1, Radius = 1_000, SensorRange = 100 }, seed: 1);
        world.AddDirective(new SphericalDirective(
            Tick: 2,
            Kind: SphericalDirectiveKind.DeployUniformAgents,
            Count: 3,
            AgentKind: SphericalAgentKind.Stationary));

        world.Tick();
        world.Tick();
        world.Agents.Should().BeEmpty();

        world.Tick();

        world.Agents.Should().HaveCount(3);
        world.Messages.Should().Contain(message => message.Kind == SphericalMessageKind.Directive);
    }

    [Fact]
    public void TickZeroDirectives_PopulateEmptyWorld()
    {
        var world = new SphericalWorld(new SphericalWorldOptions { Resolution = 2, Radius = 1_000, SensorRange = 100 }, seed: 3);
        world.AddDirective(new SphericalDirective(0, SphericalDirectiveKind.DeployUniformAgents, Count: 5, AgentKind: SphericalAgentKind.CooperativeSearch, SensorRange: 100, CommunicationRange: 200));
        world.AddDirective(new SphericalDirective(0, SphericalDirectiveKind.EnableCoverageWatcher));
        world.AddDirective(new SphericalDirective(0, SphericalDirectiveKind.DeployPeriodicIncidents, Count: 1, PeriodTicks: 20, IncidentType: "StationaryIncident"));

        world.Tick();

        world.Agents.Should().HaveCount(5);
        world.Agents.Should().OnlyContain(a => a.SensorRange == 100 && a.CommunicationRange == 200);
        world.Incidents.Should().HaveCount(1);
        world.CoverageSamples.Should().NotBeEmpty();
    }

    [Fact]
    public void ScenarioSerializer_RoundTripsWorldAndDirectives()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sphere.json");
        try
        {
            var document = new SphericalScenarioDocument
            {
                Title = "Round trip",
                Seed = 123,
                World = new SphericalWorldOptions
                {
                    Radius = 500,
                    Resolution = 3,
                    SensorRange = 25,
                    CommunicationRange = 50,
                    StepSize = 2
                },
                Directives =
                [
                    new SphericalDirective(0, SphericalDirectiveKind.DeployUniformAgents, 4,
                        SphericalAgentKind.CooperativeSearch, 25, 50, 2),
                    new SphericalDirective(20, SphericalDirectiveKind.DeployPeriodicIncidents, 1,
                        PeriodTicks: 20, IncidentType: "StationaryIncident")
                ]
            };

            SphericalScenarioSerializer.Save(path, document);
            var loaded = SphericalScenarioSerializer.Load(path);

            loaded.Should().BeEquivalentTo(document);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
