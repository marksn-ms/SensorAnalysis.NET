namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Maps legacy deployer names and parameter keys to the unified deployers.
/// </summary>
public static class DeployerRecipeCompat
{
    private static readonly HashSet<string> LegacyIncidentKinds =
        new(StringComparer.OrdinalIgnoreCase) { "Incident", "Hotspot", "Distress" };

    /// <summary>Legacy deployer names that still appear in saved recipes.</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyNameMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Random"] = "Uniform",
            ["UniformIncidents"] = "Uniform",
            ["PeriodicIncidents"] = "Periodic"
        };

    /// <summary>Resolves a deployer factory, accepting legacy names.</summary>
    /// <param name="deployerName">Name from a recipe directive.</param>
    /// <param name="deployers">Registered deployer map.</param>
    /// <param name="factory">Resolved factory when successful.</param>
    /// <returns><c>true</c> when <paramref name="factory"/> was resolved.</returns>
    public static bool TryResolveFactory(
        string deployerName,
        IReadOnlyDictionary<string, Func<IDeployer>> deployers,
        out Func<IDeployer> factory)
    {
        if (deployers.TryGetValue(deployerName, out factory!))
            return true;

        if (LegacyNameMap.TryGetValue(deployerName, out var canonical) &&
            deployers.TryGetValue(canonical, out factory!))
            return true;

        factory = null!;
        return false;
    }

    /// <summary>
    /// Normalizes legacy parameter bags before <see cref="DeployerParameterCatalog.Materialize"/>.
    /// </summary>
    /// <param name="deployerName">Name from a recipe directive (may be legacy).</param>
    /// <param name="optionsType">Target deployer options CLR type.</param>
    /// <param name="raw">Raw parameter bag from the recipe.</param>
    /// <returns>Normalized copy safe for materialization.</returns>
    public static Dictionary<string, object?> NormalizeParameters(
        string deployerName,
        Type optionsType,
        IReadOnlyDictionary<string, object?> raw)
    {
        var map = new Dictionary<string, object?>(raw, StringComparer.Ordinal);

        if (!map.ContainsKey("TypeName") && map.TryGetValue("IncidentType", out var legacyIncident))
            map["TypeName"] = legacyIncident;

        var typeName = map.TryGetValue("TypeName", out var typeRaw) ? typeRaw?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(typeName) &&
            !string.Equals(typeName, "Incident", StringComparison.OrdinalIgnoreCase) &&
            LegacyIncidentKinds.Contains(typeName))
        {
            map["TypeName"] = "Incident";
            if (!map.ContainsKey("Kind"))
                map["Kind"] = typeName;
        }

        switch (deployerName.ToUpperInvariant())
        {
            case "UNIFORMINCIDENTS":
            case "PERIODICINCIDENTS":
                if (!map.ContainsKey("TypeName"))
                    map["TypeName"] = "Incident";
                break;

            case "PLACENEAR":
                if (!map.ContainsKey("TypeName"))
                    map["TypeName"] = "Incident";
                break;
        }

        return map;
    }
}
