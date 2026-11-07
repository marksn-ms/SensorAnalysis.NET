using System.Text.RegularExpressions;

namespace SensorAnalysis.Distribution;

/// <summary>
/// Product release tags. The id is followed by <c>-v</c> so <c>sensoranalysis</c>
/// cannot be mistaken for <c>sensoranalysis.space</c>.
/// </summary>
public static partial class ReleaseTags
{
    /// <summary>Builds the GitHub tag for one product version.</summary>
    public static string For(string productId, string version) => $"{productId}-v{version}";

    /// <summary>Splits a tag into its product id and version.</summary>
    public static bool TryParse(string? tag, out string productId, out ReleaseVersion version)
    {
        productId = "";
        version = default;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var match = TagPattern().Match(tag);
        if (!match.Success)
            return false;

        productId = match.Groups["id"].Value;
        return ReleaseVersion.TryParse(match.Groups["version"].Value, out version);
    }

    /// <summary>Velopack feed file for a channel, for example <c>releases.sensoranalysis.json</c>.</summary>
    public static string FeedFileName(string channel) => $"releases.{channel}.json";

    [GeneratedRegex(@"^(?<id>[a-z0-9]+(?:\.[a-z0-9]+)*)-v(?<version>\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
