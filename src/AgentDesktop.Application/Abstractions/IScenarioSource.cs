namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Stream of raw scenario definitions (YAML or JSON). The
/// <c>ScenarioRegistry</c> validates each one and produces a
/// <see cref="Domain.Scenarios.Scenario"/>.
/// </summary>
public interface IScenarioSource
{
    IAsyncEnumerable<RawScenarioDefinition> EnumerateAsync(CancellationToken ct);
}

/// <summary>One scenario file's text plus the path it came from (for error messages).</summary>
public sealed record RawScenarioDefinition(
    string Path,
    string Text,
    ScenarioFormat Format);

public enum ScenarioFormat
{
    Yaml = 0,
    Json = 1,
}
