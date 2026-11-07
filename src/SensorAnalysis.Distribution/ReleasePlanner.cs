namespace SensorAnalysis.Distribution;

/// <summary>A product version that should be published.</summary>
public sealed record PlannedRelease
{
    /// <summary>Product id.</summary>
    public required string Id { get; init; }

    /// <summary>Shortcut title.</summary>
    public required string Title { get; init; }

    /// <summary>Version to publish.</summary>
    public required string Version { get; init; }

    /// <summary>GitHub tag, for example <c>sensoranalysis-v0.3.0</c>.</summary>
    public required string Tag { get; init; }

    /// <summary>Application project path.</summary>
    public required string Project { get; init; }

    /// <summary>Executable name without <c>.exe</c>.</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Velopack package id.</summary>
    public required string PackId { get; init; }

    /// <summary>Velopack channel.</summary>
    public required string Channel { get; init; }

    /// <summary>Test project that gates the release.</summary>
    public required string TestProject { get; init; }
}

/// <summary>One GitHub release, as returned by <c>gh release list --json</c>.</summary>
public sealed class PublishedRelease
{
    /// <summary>Git tag.</summary>
    public string TagName { get; init; } = "";

    /// <summary>Draft releases are not update sources.</summary>
    public bool IsDraft { get; init; }

    /// <summary>Prereleases are not offered as stable updates.</summary>
    public bool IsPrerelease { get; init; }
}

/// <summary>
/// Decides which products a push may publish. Adding the manifest does not publish anything.
/// A later version increase, or a product added to an existing manifest, does.
/// </summary>
public static class ReleasePlanner
{
    /// <summary>
    /// Compares two manifests. <paramref name="previous"/> is null when the manifest did not yet exist.
    /// </summary>
    public static IReadOnlyList<PlannedRelease> Plan(ProductCatalog? previous, ProductCatalog current)
    {
        current.Validate();
        if (previous is null)
            return [];

        previous.Validate();
        var prior = previous.Products.ToDictionary(product => product.Id, StringComparer.Ordinal);
        var releases = new List<PlannedRelease>();

        foreach (var product in current.Products)
        {
            if (!prior.TryGetValue(product.Id, out var old))
            {
                releases.Add(ToRelease(product));
                continue;
            }

            if (old.PackId != product.PackId || old.Channel != product.Channel || old.Project != product.Project)
                throw new InvalidDataException(
                    $"Product '{product.Id}' changed pack id, channel, or project without a version increase.");

            var comparison = ReleaseVersion.Parse(product.Version).CompareTo(ReleaseVersion.Parse(old.Version));
            if (comparison < 0)
                throw new InvalidDataException(
                    $"Product '{product.Id}' version {product.Version} is lower than published manifest version {old.Version}.");
            if (comparison > 0)
                releases.Add(ToRelease(product));
        }

        return releases;
    }

    /// <summary>
    /// Products whose current manifest version has no GitHub tag yet.
    /// Used to recover a release that failed before the tag was created.
    /// </summary>
    public static IReadOnlyList<PlannedRelease> Unpublished(ProductCatalog current, IEnumerable<PublishedRelease> releases)
    {
        current.Validate();
        var tags = new HashSet<string>(releases.Select(release => release.TagName), StringComparer.OrdinalIgnoreCase);
        return current.Products
            .Where(product => !tags.Contains(ReleaseTags.For(product.Id, product.Version)))
            .Select(ToRelease)
            .ToList();
    }

    /// <summary>Selects the highest stable tag for this product that is older than <paramref name="version"/>.</summary>
    public static string? PreviousStableTag(IEnumerable<PublishedRelease> releases, string productId, string version)
    {
        if (!ReleaseVersion.TryParse(version, out var target))
            throw new FormatException($"'{version}' is not a MAJOR.MINOR.PATCH version.");

        ReleaseVersion? best = null;
        string? bestTag = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || release.IsPrerelease)
                continue;
            if (!ReleaseTags.TryParse(release.TagName, out var id, out var parsed) || id != productId)
                continue;
            if (parsed.CompareTo(target) >= 0)
                continue;
            if (best is not null && parsed.CompareTo(best.Value) <= 0)
                continue;

            best = parsed;
            bestTag = release.TagName;
        }

        return bestTag;
    }

    private static PlannedRelease ToRelease(ProductEntry product) => new()
    {
        Id = product.Id,
        Title = product.Title,
        Version = product.Version,
        Tag = ReleaseTags.For(product.Id, product.Version),
        Project = product.Project,
        AssemblyName = product.AssemblyName,
        PackId = product.PackId,
        Channel = product.Channel,
        TestProject = product.TestProject
    };
}
