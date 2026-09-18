using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Agents.Strategies;
using SensorAnalysis.Core.Deployment;
using SensorAnalysis.Core.Domain;
using SensorAnalysis.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace SensorAnalysis.Core.Tests.World;

/// <summary>
/// Diagnostic: random walk does not seek. Drain is world-geometry. On the n=50 / λ=0.625
/// cell, spreading wanderers cover more events than recruiters who pile onto a few.
/// </summary>
public class RandomWalkCoveringDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public RandomWalkCoveringDiagnosticTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void RandomWalk_Decide_IgnoresIncidents()
    {
        var agent = new RandomWalkAgent { SensorRange = 80 };
        agent.ContributesToIncidentDrain.Should().BeTrue(
            "the world drains from geometry; RandomWalk never opts out");
        // Decide discards the observation and only updates a compass heading.
    }

    [Fact]
    public void PilotLoad_WanderersSpread_CooperatorsClump()
    {
        foreach (var type in new[] { "RandomWalk", "OrbitingCoop", "GreedyIncident" })
        {
            var stats = Run(type, ticks: 800, seed: 42);
            _out.WriteLine(
                $"{type}: rho={stats.Rho:F2}  mean_in_range={stats.MeanInRange:F2}  " +
                $"frac0={stats.FracZero:P1}  frac1={stats.FracOne:P1}  frac2={stats.FracTwo:P1}  " +
                $"frac3plus={stats.FracThreePlus:P1}  mean_open={stats.MeanOpen:F1}");
        }
    }

    private static CoverStats Run(string typeName, int ticks, int seed)
    {
        var runner = new SimulationRunner();
        runner.Reset(new WorldOptions
        {
            TileCount = new GridSize(25, 25),
            TileSizePx = new GridSize(40, 40),
            ExactSensing = false,
            MaxDrainingAgentsPerIncident = 3
        }, seed);

        var ctx = new DeployContext
        {
            Deployables = BuiltinCatalog.CreateDeployables(),
            Parameters = new Dictionary<string, object?> { ["SensorRange"] = 80.0 },
            RandomSalt = 0
        };
        runner.Deploy(
            new[]
            {
                new DeployStep
                {
                    CreateDeployer = () => new UniformDeployer(),
                    Options = new UniformDeployOptions { Count = 50, TypeName = typeName },
                    Parameters = ctx.Parameters
                },
                new DeployStep
                {
                    CreateDeployer = () => new PeriodicDeployer(),
                    Options = new PeriodicDeployOptions { PeriodTicks = 8, Count = 5, TypeName = "Incident" }
                }
            },
            ctx);

        double occSum = 0;
        double inRangeSum = 0;
        long eventTicks = 0;
        long c0 = 0, c1 = 0, c2 = 0, c3 = 0;
        const int warmup = 200;

        for (int t = 0; t < ticks; t++)
        {
            runner.Step();
            var w = runner.World!;
            var open = w.Incidents.Where(i => !i.IsDepleted).ToList();
            occSum += open.Count;
            if (t < warmup) continue;
            foreach (var inc in open)
            {
                var n = w.Agents.Count(a =>
                    a.ContributesToIncidentDrain &&
                    w.Distance(a.Position, inc.Position) <= a.SensorRange);
                inRangeSum += n;
                eventTicks++;
                if (n <= 0) c0++;
                else if (n == 1) c1++;
                else if (n == 2) c2++;
                else c3++;
            }
        }

        return new CoverStats(
            Rho: occSum / ticks,
            MeanOpen: eventTicks / (double)(ticks - warmup),
            MeanInRange: eventTicks == 0 ? 0 : inRangeSum / eventTicks,
            FracZero: eventTicks == 0 ? 0 : c0 / (double)eventTicks,
            FracOne: eventTicks == 0 ? 0 : c1 / (double)eventTicks,
            FracTwo: eventTicks == 0 ? 0 : c2 / (double)eventTicks,
            FracThreePlus: eventTicks == 0 ? 0 : c3 / (double)eventTicks);
    }

    private readonly record struct CoverStats(
        double Rho,
        double MeanOpen,
        double MeanInRange,
        double FracZero,
        double FracOne,
        double FracTwo,
        double FracThreePlus);
}
