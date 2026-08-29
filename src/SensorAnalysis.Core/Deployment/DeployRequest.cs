using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Legacy flat parameter bag used by the headless CLI and older call sites.
/// Prefer deployer-specific options types annotated with <see cref="DeployerParamAttribute"/>.
/// </summary>
public sealed class DeployRequest
{
    /// <summary>Number of agents to create (honest + adversaries).</summary>
    public required int AgentCount { get; init; }

    /// <summary>Honest agent type name (factory key).</summary>
    public required string AgentTypeName { get; init; }

    /// <summary>Factory for honest agents.</summary>
    public required Func<string, Domain.Agent> AgentFactory { get; init; }

    /// <summary>Initial incident count for immediate deployers.</summary>
    public int IncidentCount { get; init; }

    /// <summary>Default sensor range for created agents.</summary>
    public double SensorRange { get; init; } = 100;

    /// <summary>Number of adversary agents mixed into the deployment.</summary>
    public int AdversaryCount { get; init; }

    /// <summary>Factory for adversary agents when <see cref="AdversaryCount"/> &gt; 0.</summary>
    public Func<string, Domain.Agent>? AdversaryFactory { get; init; }

    /// <summary>Adversary factory type name (when using options conversion).</summary>
    public string AdversaryTypeName { get; init; } = "HerdingDeceptive";

    /// <summary>Gaussian drop sigma for honest agents.</summary>
    public double DeploySigma { get; init; } = 80;

    /// <summary>Optional drop point; world center when null.</summary>
    public Vec2? DropPoint { get; init; }

    /// <summary>Prefer living non-draining agents for incident placement.</summary>
    public bool PlaceIncidentsNearAdversaries { get; init; }

    /// <summary>Salt mixed into deployer RNG.</summary>
    public int RandomSalt { get; init; }

    /// <summary>When &gt; 0, CLI also attaches a <see cref="PeriodicDeployer"/>.</summary>
    public int IncidentPeriodTicks { get; init; }

    /// <summary>Incidents created on each periodic wave.</summary>
    public int IncidentsPerWave { get; init; } = 1;

    /// <summary>Builds a <see cref="DeployContext"/> from this request's factories.</summary>
    public DeployContext ToContext(DeployableRegistry deployables)
    {
        return new DeployContext
        {
            Deployables = deployables,
            RandomSalt = RandomSalt,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["SensorRange"] = SensorRange
            }
        };
    }
}
