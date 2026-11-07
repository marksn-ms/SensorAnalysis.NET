using System.Globalization;

namespace SensorAnalysis.Distribution;

/// <summary>A three-part release version. Velopack rejects four-part assembly versions.</summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    /// <summary>Parses <paramref name="text"/> as <c>MAJOR.MINOR.PATCH</c>.</summary>
    public static ReleaseVersion Parse(string text)
    {
        if (!TryParse(text, out var version))
            throw new FormatException($"'{text}' is not a MAJOR.MINOR.PATCH version.");
        return version;
    }

    /// <summary>Parses <paramref name="text"/> without throwing.</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('.');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        if (major < 0 || minor < 0 || patch < 0)
            return false;

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
