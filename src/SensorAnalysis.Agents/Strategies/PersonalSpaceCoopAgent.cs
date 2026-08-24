using SensorAnalysis.Core.Agents;
using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Agents.Strategies;

/// <summary>
/// First-principles naïve cooperator: no radio. Seeks the nearest sensed event;
/// otherwise explores while yielding personal space (r/2) to every nearby peer.
/// </summary>
public class PersonalSpaceCoopAgent : Agent
{
    private readonly Random _rng;
    private AgentAction _exploreHeading = AgentAction.Stay;
    private int _explorePersist;

    /// <summary>Creates a personal-space cooperative agent.</summary>
    public PersonalSpaceCoopAgent(int? seed = null)
    {
        AgentType = "PersonalSpaceCoop";
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        StepSize = 5;
        FilterToAddressedTraffic = true;
        ContributesToIncidentDrain = true;
    }

    /// <summary>Protected RNG for subclasses.</summary>
    protected Random Rng => _rng;

    /// <summary>Personal-space radius used while exploring (default half sensor range).</summary>
    protected double PersonalSpaceRadius => SensorRange * 0.5;

    /// <inheritdoc />
    public override AgentAction Decide(AgentObservation observation)
    {
        if (observation.NearbyIncidents.Count > 0)
        {
            var nearest = observation.NearbyIncidents
                .OrderBy(i => observation.DistanceTo(i.Position))
                .First();
            // Sit once close enough to drain; otherwise approach.
            if (observation.DistanceTo(nearest.Position) <= SensorRange * 0.35)
                return AgentAction.Stay;
            return Steering.DirectionToward(observation.DisplacementTo(nearest.Position));
        }

        return Explore(observation, NeighborsForPersonalSpace(observation));
    }

    /// <summary>Peers that should trigger personal-space yielding (all peers for naïve).</summary>
    protected virtual IReadOnlyList<AgentInfo> NeighborsForPersonalSpace(AgentObservation observation) =>
        observation.NearbyAgents;

    /// <summary>Personal-space exploration with the given neighbor set.</summary>
    protected AgentAction Explore(AgentObservation observation, IReadOnlyList<AgentInfo> neighbors) =>
        Steering.ExploreWithPersonalSpace(
            observation.SelfPosition,
            _exploreHeading,
            _explorePersist,
            neighbors,
            PersonalSpaceRadius,
            observation.WorldWidth,
            observation.WorldHeight,
            _rng,
            out _exploreHeading,
            out _explorePersist);
}

/// <summary>Factory for <see cref="PersonalSpaceCoopAgent"/>.</summary>
public sealed class PersonalSpaceCoopAgentFactory : IAgentFactory
{
    /// <inheritdoc />
    public string Name => "PersonalSpaceCoop";

    /// <inheritdoc />
    public string Description =>
        "No radio: seek nearest event; else explore yielding personal space to all peers.";

    /// <inheritdoc />
    public Agent Create(string? label = null) =>
        new PersonalSpaceCoopAgent { Label = label ?? "PS" };
}
