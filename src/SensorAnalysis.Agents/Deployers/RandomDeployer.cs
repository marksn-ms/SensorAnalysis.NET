using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployers;

/// <summary>Places agents and incidents at uniformly random positions.</summary>
public sealed class RandomDeployer : IDeployer
{
    /// <inheritdoc />
    public string Name => "Random";

    /// <inheritdoc />
    public string Description => "Uniform random placement of agents and incidents.";

    /// <inheritdoc />
    public void Deploy(SimWorld world, DeployRequest request)
    {
        var rng = new Random(world.Seed);
        var w = world.WorldWidthPx;
        var h = world.WorldHeightPx;

        for (int i = 0; i < request.AgentCount; i++)
        {
            var agent = request.AgentFactory(request.AgentTypeName);
            agent.Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            agent.SensorRange = request.SensorRange;
            agent.Label = $"{agent.AgentType}-{i}";
            world.Add(agent);
        }

        for (int i = 0; i < request.IncidentCount; i++)
        {
            world.Add(new Incident
            {
                Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h),
                Intensity = 1.0,
                Label = $"I-{i}"
            });
        }
    }
}
