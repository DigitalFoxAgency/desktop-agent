using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Common;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
