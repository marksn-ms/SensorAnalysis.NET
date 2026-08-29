using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Placement helpers used by deployers. Each method only chooses positions;
/// object creation is delegated to <see cref="DeployableRegistry"/>.
/// </summary>
public static class DeployableSpawner
{
    /// <summary>Places <paramref name="count"/> objects uniformly at random.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="context">Deployable registry and parameter bag.</param>
    /// <param name="count">Number of objects to place.</param>
    /// <param name="typeName">Deployable registry key.</param>
    /// <param name="labelPrefix">Prefix for generated object labels.</param>
    public static void SpawnUniform(
        SimWorld world,
        DeployContext context,
        int count,
        string typeName,
        string labelPrefix)
    {
        var rng = world.Random;
        var n = Math.Max(0, count);
        var deployableParams = context.ParametersFor(typeName);

        for (int i = 0; i < n; i++)
        {
            context.Deployables.Deploy(world, typeName, new DeployableSpawnRequest
            {
                Position = IncidentPlacementHelper.PlaceUniform(world, rng),
                Label = $"{labelPrefix}-{i}",
                Parameters = deployableParams,
                Rng = rng
            });
        }
    }

    /// <summary>Places <paramref name="count"/> objects in a Gaussian cloud about world center.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="context">Deployable registry and parameter bag.</param>
    /// <param name="count">Number of objects to place.</param>
    /// <param name="typeName">Deployable registry key.</param>
    /// <param name="deploySigma">Gaussian σ in world units.</param>
    /// <param name="labelPrefix">Prefix for generated object labels.</param>
    public static void SpawnGaussian(
        SimWorld world,
        DeployContext context,
        int count,
        string typeName,
        double deploySigma,
        string labelPrefix)
    {
        var rng = world.Random;
        var drop = new Vec2(world.WorldWidthPx / 2.0, world.WorldHeightPx / 2.0);
        var sigma = Math.Max(1.0, deploySigma);
        var n = Math.Max(0, count);
        var deployableParams = context.ParametersFor(typeName);

        for (int i = 0; i < n; i++)
        {
            context.Deployables.Deploy(world, typeName, new DeployableSpawnRequest
            {
                Position = SampleGaussian(rng, drop, sigma, world),
                Label = $"{labelPrefix}-{i}",
                Parameters = deployableParams,
                Rng = rng
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
        return world.Wrap(p);
    }
}
