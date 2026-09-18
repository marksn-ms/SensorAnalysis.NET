using FluentAssertions;
using SensorAnalysis.Agents;
using SensorAnalysis.Core.Serialization;
using Xunit;

namespace SensorAnalysis.Core.Tests.Serialization;

public class ExperimentRecipeTests
{
    [Fact]
    public void TemplateText_ParsesAndCompiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sa-recipe-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, ExperimentRecipe.TemplateText());
            var recipe = RecipeSerializer.Load(path);
            recipe.Trials.Should().Be(5);
            recipe.World.TileColumns.Should().Be(10);
            recipe.Directives.Should().HaveCount(2);
            recipe.Directives[0].Deployer.Should().Be("Gaussian");
            recipe.Directives[0].Parameters["TypeName"]!.ToString().Should().Be("OrbitingCoop");

            var steps = RecipeCompiler.CompileSteps(recipe, BuiltinCatalog.CreateDeployers());
            steps.Should().HaveCount(2);
            steps[0].AtTick.Should().Be(0);
            steps[0].RandomSalt.Should().Be(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RoundTrip_PreservesPeriodicNearHostFlags()
    {
        var recipe = new ExperimentRecipe
        {
            ExperimentName = "deceptive",
            Trials = 3,
            Ticks = 100,
            Seed = 7,
            World = new RecipeWorld { TileColumns = 10, TileRows = 10, ExactSensing = true },
            Directives =
            [
                new RecipeDirective
                {
                    AtTick = 0,
                    Deployer = "Periodic",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["Count"] = 3,
                        ["NearTypeName"] = "HerdingDeceptive",
                        ["RangeFactor"] = 0.5,
                        ["PeriodTicks"] = 30,
                        ["RoundRobinHosts"] = true
                    }
                }
            ]
        };

        var path = Path.Combine(Path.GetTempPath(), $"sa-recipe-{Guid.NewGuid():N}.json");
        try
        {
            RecipeSerializer.Save(path, recipe);
            var loaded = RecipeSerializer.Load(path);
            var step = RecipeCompiler.CompileSteps(loaded, BuiltinCatalog.CreateDeployers()).Single();
            var options = (SensorAnalysis.Core.Deployment.PeriodicDeployOptions)step.Options;
            options.Count.Should().Be(3);
            options.NearTypeName.Should().Be("HerdingDeceptive");
            options.RoundRobinHosts.Should().BeTrue();
            options.PeriodTicks.Should().Be(30);
            loaded.World.ExactSensing.Should().BeTrue();
            loaded.World.DrainPerAgentPerTick.Should().Be(0.01);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
