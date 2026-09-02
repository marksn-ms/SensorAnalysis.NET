using SensorAnalysis.Core.Deployment;

namespace SensorAnalysis.App.Services;

/// <summary>Builds one-line summaries for environment directives shown in the UI.</summary>
public static class DirectiveSummaryFormatter
{
    /// <summary>
    /// Formats a directive using deployer catalog order plus deployable fields for the selected type.
    /// </summary>
    public static string Format(
        long atTick,
        string deployerName,
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, Func<IDeployer>> deployerFactories,
        DeployableRegistry deployables)
    {
        var parts = new List<string>();

        if (DeployerRecipeCompat.TryResolveFactory(deployerName, deployerFactories, out var factory))
        {
            var prototype = factory();
            var normalized = DeployerRecipeCompat.NormalizeParameters(
                deployerName, prototype.OptionsType, parameters);
            var options = DeployerParameterCatalog.Materialize(prototype.OptionsType, normalized);

            foreach (var info in DeployerParameterCatalog.Describe(prototype.OptionsType, options))
            {
                var value = normalized.TryGetValue(info.Name, out var raw)
                    ? raw
                    : info.Property.GetValue(options);
                parts.Add($"{info.Label}={FormatValue(value)}");
            }

            var typeName = DeployerParameterCatalog.GetTypeName(options);
            if (!string.IsNullOrWhiteSpace(typeName) &&
                deployables.Names.Any(n => string.Equals(n, typeName, StringComparison.OrdinalIgnoreCase)))
            {
                var deployableFactory = deployables.Get(typeName);
                if (deployableFactory.ConfigType is { } configType)
                {
                    var config = Activator.CreateInstance(configType)!;
                    DeployableParameterCatalog.Apply(config, normalized);
                    foreach (var info in DeployableParameterCatalog.Describe(configType))
                    {
                        var value = normalized.TryGetValue(info.Name, out var raw)
                            ? raw
                            : info.Property.GetValue(config);
                        parts.Add($"{info.Label}={FormatValue(value)}");
                    }
                }
            }
        }
        else
        {
            foreach (var kv in parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                parts.Add($"{kv.Key}={FormatValue(kv.Value)}");
        }

        return parts.Count == 0
            ? $"t={atTick}: {deployerName}"
            : $"t={atTick}: {deployerName} ({string.Join(", ", parts)})";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString("0.###"),
        float f => f.ToString("0.###"),
        _ => value.ToString() ?? ""
    };
}
