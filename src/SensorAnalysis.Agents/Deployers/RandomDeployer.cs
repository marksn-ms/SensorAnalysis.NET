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
        var rng = new Random(unchecked(world.Seed ^ request.RandomSalt));
        var w = world.WorldWidthPx;
        var h = world.WorldHeightPx;

        var adversaryCount = Math.Clamp(request.AdversaryCount, 0, request.AgentCount);
        var honestCount = request.AgentCount - adversaryCount;
        if (adversaryCount > 0 && request.AdversaryFactory is null)
            throw new InvalidOperationException("AdversaryFactory is required when AdversaryCount > 0.");

        for (int i = 0; i < honestCount; i++)
        {
            var agent = request.AgentFactory(request.AgentTypeName);
            agent.Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            agent.SensorRange = request.SensorRange;
            agent.Label = $"{agent.AgentType}-{i}";
            world.Add(agent);
        }

        for (int i = 0; i < adversaryCount; i++)
        {
            var agent = request.AdversaryFactory!(request.AgentTypeName);
            agent.Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            agent.SensorRange = request.SensorRange;
            agent.Label = $"{agent.AgentType}-adv-{i}";
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
