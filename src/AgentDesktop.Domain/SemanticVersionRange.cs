using System.Text.RegularExpressions;

namespace AgentDesktop.Domain;

/// <summary>
/// Module-dependency range. Supports the four most useful operators:
/// <list type="bullet">
///   <item><c>1.2.3</c> — exactly that version.</item>
///   <item><c>^1.2.3</c> — compatible with that version (same major).</item>
///   <item><c>~1.2.3</c> — approximately that version (same minor).</item>
///   <item><c>&gt;=1.2.3</c> — at least that version.</item>
/// </list>
/// More elaborate range syntaxes (npm/cargo unions, exclusions) are
/// intentionally out of scope for MVP.
/// </summary>
public readonly record struct SemanticVersionRange(SemanticVersionRangeOperator Operator, SemanticVersion Version)
{
    private static readonly Regex Pattern = new(
        @"^(?<op>\^|~|>=|=)?(?<ver>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SemanticVersionRange Parse(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var trimmed = spec.Trim();
        var match = Pattern.Match(trimmed);
        if (!match.Success)
        {
            throw new FormatException($"Not a valid SemVer range: '{spec}'.");
        }

        var op = match.Groups["op"].Value switch
        {
            "^" => SemanticVersionRangeOperator.Compatible,
            "~" => SemanticVersionRangeOperator.Approximate,
            ">=" => SemanticVersionRangeOperator.AtLeast,
            "=" or "" => SemanticVersionRangeOperator.Exact,
            _ => throw new FormatException($"Unknown range operator in '{spec}'."),
        };

        var ver = SemanticVersion.Parse(match.Groups["ver"].Value);
        return new SemanticVersionRange(op, ver);
    }

    public bool Includes(SemanticVersion candidate) => Operator switch
    {
        SemanticVersionRangeOperator.Exact =>
            candidate.CompareTo(Version) == 0,
        SemanticVersionRangeOperator.Compatible =>
            candidate.Major == Version.Major && candidate >= Version,
        SemanticVersionRangeOperator.Approximate =>
            candidate.Major == Version.Major && candidate.Minor == Version.Minor && candidate >= Version,
        SemanticVersionRangeOperator.AtLeast =>
            candidate >= Version,
        _ => false,
    };

    public override string ToString() => Operator switch
    {
        SemanticVersionRangeOperator.Exact => Version.ToString(),
        SemanticVersionRangeOperator.Compatible => $"^{Version}",
        SemanticVersionRangeOperator.Approximate => $"~{Version}",
        SemanticVersionRangeOperator.AtLeast => $">={Version}",
        _ => Version.ToString(),
    };
}

public enum SemanticVersionRangeOperator
{
    Exact = 0,
    Compatible = 1,
    Approximate = 2,
    AtLeast = 3,
}
