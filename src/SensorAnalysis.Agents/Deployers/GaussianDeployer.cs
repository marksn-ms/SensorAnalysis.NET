using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Agents.Deployers;

/// <summary>
/// Honest agents: Gaussian footprint about a drop point (aircraft dispersal).
/// Adversaries: uniform random over the world (not co-dropped with the swarm).
/// Incidents: uniform by default so regimes share the same spatial difficulty;
/// optional near-adversary placement remains available as an explicit stress flag.
/// </summary>
public sealed class GaussianDeployer : IDeployer
{
    /// <inheritdoc />
    public string Name => "Gaussian";

    /// <inheritdoc />
    public string Description =>
        "Gaussian honest drop; uniform random adversaries; uniform incidents (optional near-adv).";

    /// <inheritdoc />
    public void Deploy(SimWorld world, DeployRequest request)
    {
        var rng = new Random(world.Seed);
        var w = world.WorldWidthPx;
        var h = world.WorldHeightPx;
        var drop = request.DropPoint ?? new Vec2(w / 2.0, h / 2.0);
        var sigma = Math.Max(1.0, request.DeploySigma);

        var adversaryCount = Math.Clamp(request.AdversaryCount, 0, request.AgentCount);
        var honestCount = request.AgentCount - adversaryCount;

        if (adversaryCount > 0 && request.AdversaryFactory is null)
            throw new InvalidOperationException("AdversaryFactory is required when AdversaryCount > 0.");

        for (int i = 0; i < honestCount; i++)
        {
            var agent = request.AgentFactory(request.AgentTypeName);
            agent.Position = SampleGaussian(rng, drop, sigma, world);
            agent.SensorRange = request.SensorRange;
            agent.Label = $"{agent.AgentType}-{i}";
            world.Add(agent);
        }

        var adversaries = new List<Agent>();
        for (int i = 0; i < adversaryCount; i++)
        {
            var agent = request.AdversaryFactory!(request.AgentTypeName);
            // Infiltrators are not part of the aircraft Gaussian drop.
            agent.Position = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            agent.SensorRange = request.SensorRange;
            agent.Label = $"{agent.AgentType}-adv-{i}";
            world.Add(agent);
            adversaries.Add(agent);
        }

        for (int i = 0; i < request.IncidentCount; i++)
        {
            Vec2 pos;
            if (request.PlaceIncidentsNearAdversaries && adversaries.Count > 0)
            {
                var host = adversaries[rng.Next(adversaries.Count)];
                var radius = host.SensorRange * (0.25 + 0.5 * rng.NextDouble());
                var angle = rng.NextDouble() * Math.PI * 2;
                pos = world.Clamp(new Vec2(
                    host.Position.X + Math.Cos(angle) * radius,
                    host.Position.Y + Math.Sin(angle) * radius));
            }
            else
            {
                pos = new Vec2(rng.NextDouble() * w, rng.NextDouble() * h);
            }

            world.Add(new Incident
            {
                Position = pos,
                Intensity = 1.0,
                Label = $"I-{i}"
            });
        }
    }

    private static Vec2 SampleGaussian(Random rng, Vec2 mean, double sigma, SimWorld world)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var r = Math.Sqrt(-2.0 * Math.Log(u1));
        var theta = 2.0 * Math.PI * u2;
        var p = new Vec2(mean.X + r * Math.Cos(theta) * sigma, mean.Y + r * Math.Sin(theta) * sigma);
        return world.Clamp(p);
    }
}
