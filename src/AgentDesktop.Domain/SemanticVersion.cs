using System.Text.RegularExpressions;

namespace AgentDesktop.Domain;

/// <summary>
/// Minimal SemVer 2.0 representation: MAJOR.MINOR.PATCH with an optional
/// prerelease tag. Build metadata is intentionally not modelled — module
/// manifests do not use it at MVP.
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses a SemVer string. Throws <see cref="FormatException"/> on invalid input.</summary>
    public static SemanticVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var match = Pattern.Match(value);
        if (!match.Success)
        {
            throw new FormatException($"Not a valid SemVer 2.0 string: '{value}'.");
        }

        var major = int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var patch = int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var pre = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        return new SemanticVersion(major, minor, patch, pre);
    }

    /// <summary>Tries to parse a SemVer string. Returns <c>false</c> on invalid input.</summary>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            version = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public int CompareTo(SemanticVersion other)
    {
        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0)
        {
            return cmp;
        }

        // Per SemVer 2.0: a version with prerelease is lower-precedence than the same without.
        return (Prerelease, other.Prerelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } a, { } b) => string.CompareOrdinal(a, b),
        };
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => Prerelease is null
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
