namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Time source. All non-test code MUST inject this rather than calling
/// <see cref="DateTimeOffset.UtcNow"/> directly — keeps tests deterministic
/// and lets the Application layer remain free of clock plumbing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>System clock — registered as the default <see cref="IClock"/> in production composition.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
