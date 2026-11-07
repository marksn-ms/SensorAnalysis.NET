using System.Text.Json;
using FluentAssertions;
using SensorAnalysis.Distribution;
using Xunit;

namespace SensorAnalysis.Distribution.Tests;

/// <summary>Release planning keeps the three products independent.</summary>
public class ReleasePlannerTests
{
    /// <summary>Introducing the manifest must not publish the versions already in the tree.</summary>
    [Fact]
    public void Plan_WhenManifestIsNew_PublishesNothing()
    {
        var current = SampleCatalog();

        ReleasePlanner.Plan(null, current).Should().BeEmpty();
    }

    /// <summary>A version increase publishes only that product.</summary>
    [Fact]
    public void Plan_WhenOneVersionIncreases_PublishesOnlyThatProduct()
    {
        var previous = SampleCatalog();
        var current = SampleCatalog();
        current.Products[1] = Copy(current.Products[1], version: "0.3.0");

        var releases = ReleasePlanner.Plan(previous, current);

        releases.Should().ContainSingle();
        releases[0].Id.Should().Be(ProductIds.Space);
        releases[0].Tag.Should().Be("sensoranalysis.space-v0.3.0");
        releases[0].Channel.Should().Be(ProductIds.Space);
        releases[0].PackId.Should().Be(ProductIds.Space);
    }

    /// <summary>An unchanged manifest is an ordinary commit.</summary>
    [Fact]
    public void Plan_WhenVersionsAreUnchanged_PublishesNothing()
    {
        var catalog = SampleCatalog();

        ReleasePlanner.Plan(catalog, catalog).Should().BeEmpty();
    }

    /// <summary>A product added after the manifest exists is a deliberate first release.</summary>
    [Fact]
    public void Plan_WhenProductIsAdded_PublishesTheNewProductOnly()
    {
        var previous = SampleCatalog();
        previous.Products.RemoveAt(2);
        var current = SampleCatalog();

        var releases = ReleasePlanner.Plan(previous, current);

        releases.Select(release => release.Id).Should().Equal(ProductIds.Sphere);
    }

    /// <summary>A manual run can publish a version whose tag was never created.</summary>
    [Fact]
    public void Unpublished_SkipsProductsThatAlreadyHaveATag()
    {
        var catalog = SampleCatalog();
        var tagged = ReleaseTags.For(catalog.Products[0].Id, catalog.Products[0].Version);
        var releases = new[] { Release(tagged, draft: true) };

        var pending = ReleasePlanner.Unpublished(catalog, releases);

        pending.Select(release => release.Id).Should().Equal(ProductIds.Space, ProductIds.Sphere);
    }

    /// <summary>A lower version is a mistake, not a release.</summary>
    [Fact]
    public void Plan_WhenVersionDecreases_Fails()
    {
        var previous = SampleCatalog();
        var current = SampleCatalog();
        current.Products[0] = Copy(current.Products[0], version: "0.1.0");

        var act = () => ReleasePlanner.Plan(previous, current);

        act.Should().Throw<InvalidDataException>();
    }

    /// <summary>Tag parsing keeps the dotted product ids distinct.</summary>
    [Fact]
    public void PreviousStableTag_DoesNotConfuseProductPrefixes()
    {
        var releases = new[]
        {
            Release("sensoranalysis-v0.2.0"),
            Release("sensoranalysis-v0.9.0"),
            Release("sensoranalysis.space-v1.4.0"),
            Release("sensoranalysis.sphere-v0.8.0"),
            Release("sensoranalysis-v0.4.0-beta", prerelease: true),
            Release("sensoranalysis-v0.3.0", draft: true)
        };

        ReleasePlanner.PreviousStableTag(releases, ProductIds.SensorAnalysis, "1.0.0")
            .Should().Be("sensoranalysis-v0.9.0");
        ReleasePlanner.PreviousStableTag(releases, ProductIds.Space, "1.5.0")
            .Should().Be("sensoranalysis.space-v1.4.0");
        ReleasePlanner.PreviousStableTag(releases, ProductIds.Sphere, "0.8.0")
            .Should().BeNull();
    }

    /// <summary>The checked-in manifest matches project versions and the pinned Velopack tool.</summary>
    [Fact]
    public void RepositoryManifest_MatchesProjectVersionsAndVelopackPin()
    {
        var root = FindRepositoryRoot();
        var catalog = ProductCatalog.Load(Path.Combine(root, "distribution", "products.json"));
        catalog.Should().NotBeNull();
        catalog!.Products.Select(product => product.Id).Should().Equal(
            ProductIds.SensorAnalysis,
            ProductIds.Space,
            ProductIds.Sphere);

        foreach (var product in catalog.Products)
        {
            var project = Path.Combine(root, product.Project.Replace('/', Path.DirectorySeparatorChar));
            VersionElement(project).Should().Be(product.Version);
            PackageVersion(project, "Velopack").Should().Be(catalog.VelopackVersion);
            File.Exists(Path.Combine(root, product.TestProject.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
        }

        PackageVersion(Path.Combine(root, "src", "SensorAnalysis.Updates", "SensorAnalysis.Updates.csproj"), "Velopack")
            .Should().Be(catalog.VelopackVersion);

        using var tools = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dotnet-tools.json")));
        tools.RootElement.GetProperty("tools").GetProperty("vpk").GetProperty("version").GetString()
            .Should().Be(catalog.VelopackVersion);
    }

    private static ProductCatalog SampleCatalog()
    {
        var catalog = ProductCatalog.Load(Path.Combine(FindRepositoryRoot(), "distribution", "products.json"));
        return catalog ?? throw new InvalidOperationException("Sample manifest is missing.");
    }

    private static ProductEntry Copy(ProductEntry product, string version) => new()
    {
        Id = product.Id,
        Title = product.Title,
        Version = version,
        Project = product.Project,
        AssemblyName = product.AssemblyName,
        PackId = product.PackId,
        Channel = product.Channel,
        TestProject = product.TestProject
    };

    private static PublishedRelease Release(string tag, bool draft = false, bool prerelease = false) => new()
    {
        TagName = tag,
        IsDraft = draft,
        IsPrerelease = prerelease
    };

    private static string VersionElement(string projectPath)
    {
        var versions = System.Xml.Linq.XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "Version")
            .Select(element => element.Value.Trim())
            .ToList();
        versions.Should().ContainSingle();
        return versions[0];
    }

    private static string PackageVersion(string projectPath, string packageId)
    {
        var versions = System.Xml.Linq.XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference" &&
                              element.Attribute("Include")?.Value == packageId)
            .Select(element => element.Attribute("Version")?.Value)
            .ToList();
        versions.Should().ContainSingle();
        return versions[0] ?? "";
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "distribution", "products.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find distribution/products.json.");
    }
}
