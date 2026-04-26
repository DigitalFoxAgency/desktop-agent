namespace AgentDesktop.Domain.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0, null)]
    [InlineData("0.1.0", 0, 1, 0, null)]
    [InlineData("12.34.56", 12, 34, 56, null)]
    [InlineData("1.0.0-alpha", 1, 0, 0, "alpha")]
    [InlineData("1.2.3-rc.1", 1, 2, 3, "rc.1")]
    public void Parse_accepts_well_formed_semver(string input, int major, int minor, int patch, string? pre)
    {
        var version = SemanticVersion.Parse(input);
        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.Prerelease.Should().Be(pre);
        version.ToString().Should().Be(input);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("not-a-version")]
    [InlineData("")]
    public void Parse_rejects_malformed_input(string input)
    {
        var act = () => SemanticVersion.Parse(input);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_returns_false_on_bad_input()
    {
        SemanticVersion.TryParse("not-a-version", out var v).Should().BeFalse();
        v.Should().Be(default(SemanticVersion));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.2.0", "1.1.99", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]      // prerelease < release
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)] // ordinal compare
    public void CompareTo_orders_by_semver_precedence(string a, string b, int expectedSign)
    {
        var av = SemanticVersion.Parse(a);
        var bv = SemanticVersion.Parse(b);
        Math.Sign(av.CompareTo(bv)).Should().Be(expectedSign);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.4", false)]
    [InlineData("^1.2.3", "1.5.0", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^1.2.3", "1.2.0", false)]   // older patch within same minor still rejected since < base
    [InlineData("~1.2.3", "1.2.99", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData(">=1.2.3", "9.0.0", true)]
    [InlineData(">=1.2.3", "1.2.0", false)]
    public void Range_includes_returns_correct_result(string range, string candidate, bool expected)
    {
        var r = SemanticVersionRange.Parse(range);
        r.Includes(SemanticVersion.Parse(candidate)).Should().Be(expected);
    }

    [Fact]
    public void Range_round_trips_via_ToString()
    {
        SemanticVersionRange.Parse("^1.2.3").ToString().Should().Be("^1.2.3");
        SemanticVersionRange.Parse("~1.2.3").ToString().Should().Be("~1.2.3");
        SemanticVersionRange.Parse(">=1.2.3").ToString().Should().Be(">=1.2.3");
        SemanticVersionRange.Parse("1.2.3").ToString().Should().Be("1.2.3");
    }
}
