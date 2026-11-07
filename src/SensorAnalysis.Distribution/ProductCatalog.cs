using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SensorAnalysis.Distribution;

/// <summary>The machine-readable list of independently released desktop products.</summary>
public sealed partial class ProductCatalog
{
    /// <summary>GitHub repository that hosts releases, without a <c>.git</c> suffix.</summary>
    public string Repository { get; init; } = "";

    /// <summary>Runtime identifier used for every product package.</summary>
    public string Runtime { get; init; } = "";

    /// <summary>Velopack NuGet and <c>vpk</c> version. The two must stay identical.</summary>
    public string VelopackVersion { get; init; } = "";

    /// <summary>Products that can be installed side by side.</summary>
    public List<ProductEntry> Products { get; init; } = [];

    /// <summary>JSON options used for the manifest and planner output.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Reads and validates a manifest. A file whose text is <c>null</c> means there is no previous manifest.</summary>
    public static ProductCatalog? Load(string path)
    {
        var text = File.ReadAllText(path).Trim().TrimStart('\uFEFF');
        if (text.Length == 0 || text == "null")
            return null;

        var catalog = JsonSerializer.Deserialize<ProductCatalog>(text, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not contain a product manifest.");
        catalog.Validate();
        return catalog;
    }

    /// <summary>Returns the product with this id.</summary>
    public ProductEntry Require(string productId) =>
        Products.FirstOrDefault(product => product.Id == productId)
        ?? throw new InvalidDataException($"Product '{productId}' is not in the manifest.");

    /// <summary>Checks identifiers, versions, and paths that the release workflow relies on.</summary>
    public void Validate()
    {
        if (!RepositoryPattern().IsMatch(Repository))
            throw new InvalidDataException($"Repository '{Repository}' must be an https://github.com/owner/name URL.");

        if (Runtime != "win-x64")
            throw new InvalidDataException($"Runtime '{Runtime}' is not supported. The first distribution target is win-x64.");

        if (!ReleaseVersion.TryParse(VelopackVersion, out _) && !ToolVersionPattern().IsMatch(VelopackVersion))
            throw new InvalidDataException($"Velopack version '{VelopackVersion}' is not a version.");

        if (Products.Count == 0)
            throw new InvalidDataException("The manifest does not list any products.");

        RequireDistinct(Products.Select(product => product.Id), "product id");
        RequireDistinct(Products.Select(product => product.PackId), "pack id");
        RequireDistinct(Products.Select(product => product.Channel), "channel");

        foreach (var product in Products)
            product.Validate();
    }

    private static void RequireDistinct(IEnumerable<string> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value))
                throw new InvalidDataException($"Duplicate {label} '{value}'.");
        }
    }

    [GeneratedRegex(@"^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolVersionPattern();
}

/// <summary>One installable desktop product.</summary>
public sealed partial class ProductEntry
{
    /// <summary>Stable id, also the GitHub tag prefix before <c>-v</c>.</summary>
    public string Id { get; init; } = "";

    /// <summary>Name shown in shortcuts and dialogs.</summary>
    public string Title { get; init; } = "";

    /// <summary>Release version. Increasing this on the default branch publishes a release.</summary>
    public string Version { get; init; } = "";

    /// <summary>Application project, relative to the repository root.</summary>
    public string Project { get; init; } = "";

    /// <summary>Assembly and executable name, without <c>.exe</c>.</summary>
    public string AssemblyName { get; init; } = "";

    /// <summary>Velopack package id. Install folders are separated by this value.</summary>
    public string PackId { get; init; } = "";

    /// <summary>Velopack channel. The update feed file is <c>releases.{channel}.json</c>.</summary>
    public string Channel { get; init; } = "";

    /// <summary>Test project that must pass before this product is published.</summary>
    public string TestProject { get; init; } = "";

    /// <summary>Checks one product entry.</summary>
    public void Validate()
    {
        if (!IdentifierPattern().IsMatch(Id))
            throw new InvalidDataException($"Product id '{Id}' must be lowercase words separated by dots.");
        if (string.IsNullOrWhiteSpace(Title))
            throw new InvalidDataException($"Product '{Id}' needs a title.");
        if (!ReleaseVersion.TryParse(Version, out _))
            throw new InvalidDataException($"Product '{Id}' version '{Version}' must be MAJOR.MINOR.PATCH.");
        if (PackId != Id)
            throw new InvalidDataException($"Product '{Id}' pack id must match the product id.");
        if (Channel != Id)
            throw new InvalidDataException($"Product '{Id}' channel must match the product id.");
        RequireRelativePath(Project, "project");
        RequireRelativePath(TestProject, "test project");
        if (!AssemblyPattern().IsMatch(AssemblyName))
            throw new InvalidDataException($"Product '{Id}' has an invalid assembly name '{AssemblyName}'.");
    }

    private void RequireRelativePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Split(['/', '\\']).Contains(".."))
            throw new InvalidDataException($"Product '{Id}' {label} '{path}' must be a relative repository path.");
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:\.[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z0-9.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyPattern();
}
