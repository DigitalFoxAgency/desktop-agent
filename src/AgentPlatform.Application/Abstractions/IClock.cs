namespace AgentPlatform.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
