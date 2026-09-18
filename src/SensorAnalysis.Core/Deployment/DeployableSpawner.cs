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
    /// <param name="minDistance">Minimum spacing from existing and co-wave objects (0 = disabled).</param>
    public static void SpawnUniform(
        SimWorld world,
        DeployContext context,
        int count,
        string typeName,
        string labelPrefix,
        double minDistance = 0) =>
        Spawn(
            world,
            context,
            count,
            typeName,
            labelPrefix,
            () => IncidentPlacementHelper.PlaceUniform(world, world.Random),
            minDistance);

    /// <summary>Places <paramref name="count"/> objects using <paramref name="candidateFactory"/>.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="context">Deployable registry and parameter bag.</param>
    /// <param name="count">Number of objects to place.</param>
    /// <param name="typeName">Deployable registry key.</param>
    /// <param name="labelPrefix">Prefix for generated object labels.</param>
    /// <param name="candidateFactory">Produces a candidate position each attempt.</param>
    /// <param name="minDistance">Minimum spacing from existing and co-wave objects (0 = disabled).</param>
    public static void Spawn(
        SimWorld world,
        DeployContext context,
        int count,
        string typeName,
        string labelPrefix,
        Func<Vec2> candidateFactory,
        double minDistance = 0)
    {
        var rng = world.Random;
        var n = Math.Max(0, count);
        var deployableParams = context.ParametersFor(typeName);
        var avoid = IncidentPlacementHelper.CollectOccupiedPositions(world);

        for (int i = 0; i < n; i++)
        {
            var position = IncidentPlacementHelper.PlaceWithMinSpacing(
                world,
                rng,
                candidateFactory,
                minDistance,
                avoid);

            context.Deployables.Deploy(world, typeName, new DeployableSpawnRequest
            {
                Position = position,
                Label = $"{labelPrefix}-{i}",
                Parameters = deployableParams,
                Rng = rng
            });
            avoid.Add(position);
        }
    }

    /// <summary>Places <paramref name="count"/> objects in a Gaussian cloud about world center.</summary>
    /// <param name="world">Target world.</param>
    /// <param name="context">Deployable registry and parameter bag.</param>
    /// <param name="count">Number of objects to place.</param>
    /// <param name="typeName">Deployable registry key.</param>
    /// <param name="deploySigma">Gaussian σ in world units.</param>
    /// <param name="labelPrefix">Prefix for generated object labels.</param>
    /// <param name="minDistance">Minimum spacing from existing and co-wave objects (0 = disabled).</param>
    public static void SpawnGaussian(
        SimWorld world,
        DeployContext context,
        int count,
        string typeName,
        double deploySigma,
        string labelPrefix,
        double minDistance = 0)
    {
        var rng = world.Random;
        var drop = new Vec2(world.WorldWidthPx / 2.0, world.WorldHeightPx / 2.0);
        var sigma = Math.Max(1.0, deploySigma);
        var n = Math.Max(0, count);
        var deployableParams = context.ParametersFor(typeName);
        var avoid = IncidentPlacementHelper.CollectOccupiedPositions(world);

        for (int i = 0; i < n; i++)
        {
            var position = IncidentPlacementHelper.PlaceWithMinSpacing(
                world,
                rng,
                () => SampleGaussian(rng, drop, sigma, world),
                minDistance,
                avoid);

            context.Deployables.Deploy(world, typeName, new DeployableSpawnRequest
            {
                Position = position,
                Label = $"{labelPrefix}-{i}",
                Parameters = deployableParams,
                Rng = rng
            });
            avoid.Add(position);
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
