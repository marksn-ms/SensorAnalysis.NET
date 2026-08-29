using SensorAnalysis.Sphere.Core.Geometry;

namespace SensorAnalysis.Sphere.Core.Simulation;

/// <summary>Built-in continuous surface navigation behavior.</summary>
public enum SphericalAgentKind
{
    /// <summary>Randomly steers in the local tangent plane and walks continuously.</summary>
    RandomWalk,

    /// <summary>Remains at its current continuous surface position.</summary>
    Stationary,

    /// <summary>Senses, relays, and steers toward known active incident positions.</summary>
    CooperativeSearch
}

/// <summary>Kind of recorded world message shown in the UI Messages panel.</summary>
public enum SphericalMessageKind
{
    /// <summary>Range-limited incident position relay between agents.</summary>
    Relay,

    /// <summary>Incident intensity reached zero.</summary>
    Resolved,

    /// <summary>A scheduled deployment directive was applied.</summary>
    Directive
}

/// <summary>Parameters used to construct a spherical geodesic world.</summary>
public sealed record SphericalWorldOptions
{
    /// <summary>Physical sphere radius.</summary>
    public double Radius { get; init; } = 1_000;

    /// <summary>Geodesic resolution n for the internal 20n²-face spatial index.</summary>
    public int Resolution { get; init; } = 4;

    /// <summary>Default absolute sensor range for newly created agents.</summary>
    public double SensorRange { get; init; } = 100;

    /// <summary>Default absolute communication range for newly created agents.</summary>
    public double CommunicationRange { get; init; } = 100;

    /// <summary>Default continuous step distance in world units.</summary>
    /// <remarks>TODO: rename to <c>Velocity</c> for parity with flat <see cref="SensorAnalysis.Core.Domain.Agent"/>.</remarks>
    public double StepSize { get; init; } = 1;

    /// <summary>Intensity removed per helping agent per tick.</summary>
    public double DrainPerHelper { get; init; } = 0.2;
}

/// <summary>A continuous-surface agent indexed onto an internal mesh face for queries.</summary>
public sealed class SphericalAgent
{
    /// <summary>Creates an agent at a continuous surface position.</summary>
    internal SphericalAgent(
        int id,
        SphereVector position,
        SphereVector heading,
        int faceId,
        SphericalAgentKind kind,
        double sensorRange,
        double communicationRange,
        double stepSize)
    {
        Id = id;
        Position = position;
        Heading = heading;
        FaceId = faceId;
        Kind = kind;
        SensorRange = sensorRange;
        CommunicationRange = communicationRange;
        StepSize = stepSize;
    }

    /// <summary>Stable agent id.</summary>
    public int Id { get; }

    /// <summary>Continuous unit-sphere position.</summary>
    public SphereVector Position { get; internal set; }

    /// <summary>Unit tangent heading used for continuous geodesic steps.</summary>
    public SphereVector Heading { get; internal set; }

    /// <summary>Containing face used only by the world spatial index.</summary>
    internal int FaceId { get; set; }

    /// <summary>Navigation behavior assigned at deployment.</summary>
    public SphericalAgentKind Kind { get; }

    /// <summary>Absolute sensor range for this agent.</summary>
    public double SensorRange { get; set; }

    /// <summary>Absolute communication range for this agent.</summary>
    public double CommunicationRange { get; set; }

    /// <summary>Continuous step distance in world units.</summary>
    /// <remarks>TODO: rename to <c>Velocity</c> for parity with flat <see cref="SensorAnalysis.Core.Domain.Agent"/>.</remarks>
    public double StepSize { get; set; }

    /// <summary>Base ticks between periodic actions such as radio relays.</summary>
    public int ActionIntervalBase { get; set; } = 10;

    /// <summary>Per-instance interval sampled around <see cref="ActionIntervalBase"/>.</summary>
    public int ActionInterval { get; internal set; }

    /// <summary>TTL for recorded relay messages shown in the UI.</summary>
    public int MessageEchoTtl { get; set; } = 50;

    /// <summary>Ticks to persist a random-walk heading before resampling.</summary>
    public int HeadingChangeInterval { get; set; } = 8;

    /// <summary>Ticks since the last relay transmission.</summary>
    internal int TicksSinceRelay { get; set; }

    /// <summary>Ticks remaining on the current random-walk heading.</summary>
    internal int HeadingTicksRemaining { get; set; }

    /// <summary>Incident positions learned through sensing or radio, keyed by incident id.</summary>
    public Dictionary<int, SphereVector> KnownIncidentPositions { get; } = [];
}

/// <summary>A recorded message or status event for the Messages panel.</summary>
public sealed record SphericalMessage(
    long Tick,
    SphericalMessageKind Kind,
    int? FromAgentId,
    int? ToAgentId,
    int IncidentId,
    SphereVector IncidentPosition,
    string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// Continuous spherical simulation with an internal geodesic mesh used only for indexing,
/// hop-limited candidate queries, and area-weighted coverage.
/// </summary>
public sealed class SphericalWorld
{
    private readonly List<SphericalAgent> _agents = [];
    private readonly List<SphericalIncident> _incidents = [];
    private readonly List<ISphericalDeployer> _deployers = [];
    private readonly List<SphericalDirective> _directives = [];
    private readonly List<SphericalMessage> _messages = [];
    private readonly Dictionary<int, long> _faceLastCoveredTick = [];
    private readonly List<SphericalMetricSample> _coverageSamples = [];
    private readonly Random _random;
    private double _lastDrain;
    private bool _coverageWatcherEnabled;

    /// <summary>Creates an empty spherical world from options and deterministic seed.</summary>
    public SphericalWorld(SphericalWorldOptions options, int seed)
    {
        if (options.Radius <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Resolution < 1) throw new ArgumentOutOfRangeException(nameof(options));
        Options = options;
        Mesh = GeodesicMesh.Create(options.Resolution);
        Seed = seed;
        _random = new Random(seed);
        ValidateRange(options.SensorRange, "Sensor range");
        ValidateRange(options.CommunicationRange, "Communication range");
    }

    /// <summary>World parameters.</summary>
    public SphericalWorldOptions Options { get; }

    /// <summary>Internal geodesic spatial index.</summary>
    public GeodesicMesh Mesh { get; }

    /// <summary>Live agents.</summary>
    public IReadOnlyList<SphericalAgent> Agents => _agents;

    /// <summary>Live and depleted incidents.</summary>
    public IReadOnlyList<SphericalIncident> Incidents => _incidents;

    /// <summary>Registered ongoing deployers.</summary>
    public IReadOnlyList<ISphericalDeployer> Deployers => _deployers;

    /// <summary>Queued one-shot and periodic directives.</summary>
    public IReadOnlyList<SphericalDirective> Directives => _directives;

    /// <summary>Range-limited radio relays and status events.</summary>
    public IReadOnlyList<SphericalMessage> Messages => _messages;

    /// <summary>Coverage samples recorded by an enabled coverage watcher.</summary>
    public IReadOnlyList<SphericalMetricSample> CoverageSamples => _coverageSamples;

    /// <summary>Last tick each internal face was occupied, keyed by face id.</summary>
    public IReadOnlyDictionary<int, long> FaceLastCoveredTick => _faceLastCoveredTick;

    /// <summary>Current simulation tick.</summary>
    public long CurrentTick { get; private set; }

    /// <summary>Deterministic random seed used to construct this world.</summary>
    public int Seed { get; }

    /// <summary>Shared deterministic RNG for the world and everything it hosts.</summary>
    public Random Random => _random;

    /// <summary>Physical characteristic edge length ℓ(n).</summary>
    public double CharacteristicEdgeLength => Options.Radius * Mesh.CharacteristicEdgeRadians;

    /// <summary>Area-weighted fraction of faces covered at least once.</summary>
    public double CoverageFraction =>
        Mesh.Faces.Where(face => _faceLastCoveredTick.ContainsKey(face.Id))
            .Sum(face => face.AreaSteradians) / (4 * Math.PI);

    /// <summary>Estimates dual-graph hops for a range on a resolution-n sphere of radius R.</summary>
    public static int EstimateRequiredHops(double radius, int resolution, double range)
    {
        if (range <= 0) return 0;
        var mesh = GeodesicMesh.Create(Math.Max(1, resolution));
        return (int)Math.Ceiling(range / (radius * mesh.CharacteristicEdgeRadians));
    }

    /// <summary>Adds an agent at a continuous sphere position.</summary>
    public SphericalAgent AddAgent(
        SphereVector position,
        SphericalAgentKind kind = SphericalAgentKind.CooperativeSearch,
        double? sensorRange = null,
        double? communicationRange = null,
        double? stepSize = null)
    {
        var unit = position.Normalize();
        var faceId = Mesh.FindNearestFace(unit);
        var heading = RandomTangent(unit);
        var agent = new SphericalAgent(
            _agents.Count,
            unit,
            heading,
            faceId,
            kind,
            sensorRange ?? Options.SensorRange,
            communicationRange ?? Options.CommunicationRange,
            stepSize ?? Options.StepSize);
        agent.ActionInterval = SampleGaussianInterval(agent.ActionIntervalBase);
        agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
        agent.TicksSinceRelay = _random.Next(0, Math.Max(1, agent.ActionInterval));
        _agents.Add(agent);
        _faceLastCoveredTick[faceId] = CurrentTick;
        return agent;
    }

    /// <summary>Adds an agent at a uniformly sampled surface position.</summary>
    public SphericalAgent AddRandomAgent(
        SphericalAgentKind kind = SphericalAgentKind.CooperativeSearch,
        double? sensorRange = null,
        double? communicationRange = null,
        double? stepSize = null) =>
        AddAgent(RandomUnitPosition(), kind, sensorRange, communicationRange, stepSize);

    /// <summary>Adds an incident at a continuous sphere position.</summary>
    public SphericalIncident AddIncident(SphereVector position, string incidentType = "StationaryIncident")
    {
        var unit = position.Normalize();
        var faceId = Mesh.FindNearestFace(unit);
        var incident = new SphericalIncident(_incidents.Count, unit, faceId, incidentType);
        _incidents.Add(incident);
        return incident;
    }

    /// <summary>Adds an incident at a uniformly sampled surface position.</summary>
    public SphericalIncident AddRandomIncident(string incidentType = "StationaryIncident") =>
        AddIncident(RandomUnitPosition(), incidentType);

    /// <summary>Places an incident within <paramref name="range"/> of <paramref name="host"/>.</summary>
    public SphericalIncident AddIncidentNear(SphericalAgent host, double range, string incidentType = "StationaryIncident")
    {
        var hops = Math.Max(0, RequiredHops(range, host.SensorRange));
        var candidates = Mesh.FacesWithinHops(host.FaceId, hops)
            .Select(id => Mesh.Faces[id].Center)
            .Where(position => SphereVector.GeodesicDistance(host.Position, position, Options.Radius) <= range)
            .ToArray();
        var position = candidates.Length == 0 ? host.Position : candidates[_random.Next(candidates.Length)];
        return AddIncident(position, incidentType);
    }

    /// <summary>Registers an ongoing deployer.</summary>
    public void AddDeployer(ISphericalDeployer deployer) => _deployers.Add(deployer);

    /// <summary>Removes all ongoing deployers.</summary>
    public void ClearDeployers() => _deployers.Clear();

    /// <summary>Queues a directive. Periodic directives re-queue themselves after firing.</summary>
    public void AddDirective(SphericalDirective directive)
    {
        if (directive.Tick < CurrentTick)
            throw new ArgumentOutOfRangeException(nameof(directive), "A directive cannot be scheduled in the past.");
        _directives.Add(directive);
    }

    /// <summary>Removes a queued directive by identity.</summary>
    public bool RemoveDirective(SphericalDirective directive) => _directives.Remove(directive);

    /// <summary>Applies a directive immediately without waiting for its tick.</summary>
    public void ApplyDirectiveNow(SphericalDirective directive)
    {
        ApplyDirective(directive with { Tick = CurrentTick });
        _directives.Remove(directive);
    }

    /// <summary>Runs one simulation tick: directives, deployers, sense, drain, continuous move, cover.</summary>
    public void Tick()
    {
        ApplyDueDirectives();
        foreach (var deployer in _deployers) deployer.Tick(this);

        foreach (var agent in _agents)
        {
            if (agent.Kind != SphericalAgentKind.CooperativeSearch) continue;
            foreach (var incident in ActiveIncidentsInRange(agent, agent.SensorRange))
                agent.KnownIncidentPositions[incident.Id] = incident.Position;

            agent.TicksSinceRelay++;
            if (agent.TicksSinceRelay < agent.ActionInterval) continue;

            foreach (var incident in ActiveIncidentsInRange(agent, agent.SensorRange))
            {
                foreach (var receiver in AgentsInRange(agent, agent.CommunicationRange))
                {
                    receiver.KnownIncidentPositions[incident.Id] = incident.Position;
                    _messages.Add(new SphericalMessage(
                        CurrentTick,
                        SphericalMessageKind.Relay,
                        agent.Id,
                        receiver.Id,
                        incident.Id,
                        incident.Position,
                        $"t{CurrentTick}: agent {agent.Id} → {receiver.Id}, incident {incident.Id}"));
                }
            }

            agent.TicksSinceRelay = 0;
        }

        DrainIncidents();

        foreach (var agent in _agents)
        {
            foreach (var incidentId in agent.KnownIncidentPositions.Keys
                .Where(id => _incidents.All(incident => incident.Id != id || incident.IsDepleted))
                .ToArray())
                agent.KnownIncidentPositions.Remove(incidentId);

            MoveAgent(agent);
            agent.FaceId = Mesh.FindNearestFace(agent.Position);
            _faceLastCoveredTick[agent.FaceId] = CurrentTick;
        }

        if (_coverageWatcherEnabled)
        {
            _coverageSamples.Add(new SphericalMetricSample(
                CurrentTick,
                CoverageFraction,
                _agents.Count,
                _incidents.Count(i => !i.IsDepleted),
                _lastDrain));
        }

        CurrentTick++;
    }

    /// <summary>Alias retained for older call sites.</summary>
    public void TickRandomWalk() => Tick();

    /// <summary>Calculates hop requirement for a range.</summary>
    public int RequiredHops(double range) => RequiredHops(range, Options.SensorRange);

    /// <summary>Returns a guardrail message when a range needs more than two hops.</summary>
    public string? RangeWarning(double range)
    {
        var hops = RequiredHops(range);
        return hops > 2 ? $"{hops} dual-graph hops required; keep ranges local for efficient queries." : null;
    }

    /// <summary>Gets a current metrics snapshot suitable for live UI display.</summary>
    public SphericalMetrics GetMetrics() => new(
        CurrentTick,
        CoverageFraction,
        _agents.Count,
        _incidents.Count(incident => !incident.IsDepleted),
        _incidents.Count(incident => incident.IsDepleted),
        _incidents.Count,
        _messages.Count,
        _lastDrain);

    private int RequiredHops(double range, double _) =>
        range <= 0 ? 0 : (int)Math.Ceiling(range / CharacteristicEdgeLength);

    private void MoveAgent(SphericalAgent agent)
    {
        if (agent.Kind == SphericalAgentKind.Stationary || agent.StepSize <= 0) return;

        if (agent.Kind == SphericalAgentKind.RandomWalk)
        {
            if (agent.HeadingTicksRemaining <= 0)
            {
                var turn = (_random.NextDouble() - 0.5) * Math.PI;
                agent.Heading = RotateHeading(agent.Position, agent.Heading, turn);
                agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
            }
            else
            {
                agent.HeadingTicksRemaining--;
            }
        }
        else
        {
            var targets = _incidents
                .Where(incident => !incident.IsDepleted && agent.KnownIncidentPositions.ContainsKey(incident.Id))
                .ToArray();
            if (targets.Length == 0)
            {
                if (agent.HeadingTicksRemaining <= 0)
                {
                    var turn = (_random.NextDouble() - 0.5) * Math.PI;
                    agent.Heading = RotateHeading(agent.Position, agent.Heading, turn);
                    agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
                }
                else
                {
                    agent.HeadingTicksRemaining--;
                }
            }
            else
            {
                var goal = targets
                    .OrderBy(incident => SphereVector.GeodesicDistance(agent.Position, incident.Position, Options.Radius))
                    .First();
                agent.Heading = SphereVector.TangentAt(agent.Position, goal.Position - agent.Position);
                if (agent.Heading.Length < 1e-9) agent.Heading = RandomTangent(agent.Position);
                agent.HeadingTicksRemaining = agent.HeadingChangeInterval;
            }
        }

        var radians = agent.StepSize / Options.Radius;
        agent.Position = SphereVector.Step(agent.Position, agent.Heading, radians);
        agent.Heading = SphereVector.TangentAt(agent.Position, agent.Heading);
        if (agent.Heading.Length < 1e-9) agent.Heading = RandomTangent(agent.Position);
    }

    private IReadOnlyList<SphericalAgent> AgentsInRange(SphericalAgent source, double range)
    {
        ValidateRange(range, "Range");
        var candidateFaces = Mesh.FacesWithinHops(source.FaceId, RequiredHops(range) + 1).ToHashSet();
        return _agents
            .Where(agent => agent.Id != source.Id && candidateFaces.Contains(agent.FaceId))
            .Where(agent => SphereVector.GeodesicDistance(source.Position, agent.Position, Options.Radius) <= range)
            .ToArray();
    }

    private IReadOnlyList<SphericalIncident> ActiveIncidentsInRange(SphericalAgent source, double range)
    {
        var candidateFaces = Mesh.FacesWithinHops(source.FaceId, RequiredHops(range) + 1).ToHashSet();
        return _incidents
            .Where(incident => !incident.IsDepleted && candidateFaces.Contains(incident.FaceId))
            .Where(incident => SphereVector.GeodesicDistance(source.Position, incident.Position, Options.Radius) <= range)
            .ToArray();
    }

    private void ValidateRange(double range, string label)
    {
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        var hops = RequiredHops(range);
        if (hops > 5)
            throw new ArgumentOutOfRangeException(nameof(range),
                $"{label} requires {hops} hops; the spherical world permits at most five.");
    }

    private void DrainIncidents()
    {
        _lastDrain = 0;
        foreach (var incident in _incidents.Where(incident => !incident.IsDepleted))
        {
            var candidateFaces = Mesh.FacesWithinHops(incident.FaceId, RequiredHops(Options.SensorRange) + 1).ToHashSet();
            var helpers = _agents.Count(agent =>
                candidateFaces.Contains(agent.FaceId)
                && SphereVector.GeodesicDistance(incident.Position, agent.Position, Options.Radius) <= agent.SensorRange);
            if (helpers == 0) continue;
            var drain = helpers * Options.DrainPerHelper;
            incident.Intensity = Math.Max(0, incident.Intensity - drain);
            _lastDrain += drain;
            if (incident.IsDepleted)
            {
                _messages.Add(new SphericalMessage(
                    CurrentTick,
                    SphericalMessageKind.Resolved,
                    null,
                    null,
                    incident.Id,
                    incident.Position,
                    $"t{CurrentTick}: incident {incident.Id} resolved"));
            }
        }
    }

    private void ApplyDueDirectives()
    {
        foreach (var directive in _directives.Where(directive => directive.Tick == CurrentTick).ToArray())
            ApplyDirective(directive);
    }

    private void ApplyDirective(SphericalDirective directive)
    {
        switch (directive.Kind)
        {
            case SphericalDirectiveKind.DeployUniformAgents:
                for (var i = 0; i < Math.Max(1, directive.Count); i++)
                    AddRandomAgent(directive.AgentKind, directive.SensorRange, directive.CommunicationRange, directive.StepSize);
                break;
            case SphericalDirectiveKind.DeployNearHost:
                if (_agents.Count > 0)
                {
                    var host = _agents[0];
                    var range = Math.Max(0, (directive.SensorRange ?? host.SensorRange) * (directive.RangeFactor ?? 1));
                    var hops = RequiredHops(range) + 1;
                    var candidates = Mesh.FacesWithinHops(host.FaceId, hops);
                    for (var i = 0; i < Math.Max(1, directive.Count); i++)
                    {
                        var face = Mesh.Faces[candidates[(host.Id + i * 17) % candidates.Count]];
                        AddAgent(face.Center, directive.AgentKind, directive.SensorRange, directive.CommunicationRange, directive.StepSize);
                    }
                }
                break;
            case SphericalDirectiveKind.DeployPeriodicIncidents:
                AddDeployer(new PeriodicIncidentDeployer(new PeriodicIncidentOptions
                {
                    PeriodTicks = Math.Max(1, directive.PeriodTicks ?? 20),
                    Count = Math.Max(1, directive.Count),
                    IncidentType = directive.IncidentType ?? "StationaryIncident"
                }));
                break;
            case SphericalDirectiveKind.EnableCoverageWatcher:
                _coverageWatcherEnabled = true;
                break;
        }

        _messages.Add(new SphericalMessage(
            CurrentTick,
            SphericalMessageKind.Directive,
            null,
            null,
            -1,
            SphereVector.Zero,
            $"t{CurrentTick}: {directive.Kind} applied"));

        _directives.Remove(directive);
        if (directive.Kind is SphericalDirectiveKind.DeployUniformAgents or SphericalDirectiveKind.DeployNearHost
            && directive.PeriodTicks is > 0)
        {
            _directives.Add(directive with { Tick = CurrentTick + directive.PeriodTicks.Value });
        }
    }

    private SphereVector RandomUnitPosition()
    {
        // Marsaglia method for uniform points on the unit sphere.
        double x1, x2, s;
        do
        {
            x1 = 2 * _random.NextDouble() - 1;
            x2 = 2 * _random.NextDouble() - 1;
            s = x1 * x1 + x2 * x2;
        } while (s >= 1 || s < 1e-12);
        var root = Math.Sqrt(1 - s);
        return new SphereVector(2 * x1 * root, 2 * x2 * root, 1 - 2 * s).Normalize();
    }

    private SphereVector RandomTangent(SphereVector position)
    {
        var candidate = new SphereVector(_random.NextDouble() - 0.5, _random.NextDouble() - 0.5, _random.NextDouble() - 0.5);
        var tangent = SphereVector.TangentAt(position, candidate);
        return tangent.Length < 1e-9 ? SphereVector.TangentAt(position, new SphereVector(0, 1, 0)) : tangent;
    }

    private static SphereVector RotateHeading(SphereVector position, SphereVector heading, double radians)
    {
        var normal = position.Normalize();
        var side = SphereVector.Cross(normal, heading).Normalize();
        if (side.Length < 1e-9) return heading;
        return (heading * Math.Cos(radians) + side * Math.Sin(radians)).Normalize();
    }

    private int SampleGaussianInterval(int baseInterval, double stdDev = 1.5)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Max(1, (int)Math.Round(baseInterval + z * stdDev));
    }
}

/// <summary>Live aggregate metrics for a spherical simulation tick.</summary>
public sealed record SphericalMetrics(
    long Tick,
    double Coverage,
    int Agents,
    int ActiveIncidents,
    int DepletedIncidents,
    int TotalIncidents,
    int Messages,
    double LastDrain);

/// <summary>One coverage-watcher sample.</summary>
public sealed record SphericalMetricSample(
    long Tick,
    double Coverage,
    int Agents,
    int ActiveIncidents,
    double LastDrain);
