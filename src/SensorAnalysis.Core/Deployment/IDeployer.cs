using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Places agents (and optionally incidents) into a world at the start of a trial.
/// Classic SensorAnalysis "Deployer" concept.
/// </summary>
public interface IDeployer
{
    /// <summary>
    /// Gets the unique name of this deployer implementation. Used for display and selection in UIs and configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a short human-readable description of what this deployer does.
    /// </summary>
    string Description { get; }

    /// <summary>Populate <paramref name="world"/> with agents / incidents for this trial.</summary>
    /// <param name="world">The world instance to populate.</param>
    /// <param name="request">Parameters controlling how agents and incidents should be deployed.</param>
    void Deploy(SimWorld world, DeployRequest request);
}

/// <summary>
/// Parameters describing how to deploy agents and incidents into a world for a trial.
/// </summary>
public sealed class DeployRequest
{
    /// <summary>
    /// Number of agents to create for the trial.
    /// </summary>
    public required int AgentCount { get; init; }

    /// <summary>
    /// Name of the agent type to create (used with <see cref="AgentFactory"/> or other registries).
    /// </summary>
    public required string AgentTypeName { get; init; }

    /// <summary>
    /// Factory delegate used to create an <see cref="Domain.Agent"/> given an optional label.
    /// </summary>
    public required Func<string, Domain.Agent> AgentFactory { get; init; }

    /// <summary>
    /// Number of incidents to create for the trial (default: 0).
    /// </summary>
    public int IncidentCount { get; init; } = 0;

    /// <summary>
    /// Default sensor range to assign to created agents (domain units, default: 100).
    /// </summary>
    public double SensorRange { get; init; } = 100;

    /// <summary>
    /// Number of adversary agents to mix into the deployment (default: 0).
    /// When greater than zero, <see cref="AdversaryFactory"/> must be supplied.
    /// </summary>
    public int AdversaryCount { get; init; } = 0;

    /// <summary>
    /// Factory used to create adversary agents when <see cref="AdversaryCount"/> &gt; 0.
    /// </summary>
    public Func<string, Domain.Agent>? AdversaryFactory { get; init; }

    /// <summary>
    /// Gaussian deployment sigma in world units (used by Gaussian deployers). Default: 80.
    /// </summary>
    public double DeploySigma { get; init; } = 80;

    /// <summary>
    /// Optional drop-point for Gaussian agent deployment. When null, world center is used.
    /// </summary>
    public Domain.Vec2? DropPoint { get; init; }

    /// <summary>
    /// When true and adversaries are present, place each incident within an adversary's
    /// sensor range so infiltrators can act on real events (informed-adversary stress test).
    /// Ignored when <see cref="AdversaryCount"/> is 0.
    /// </summary>
    public bool PlaceIncidentsNearAdversaries { get; init; }
}
