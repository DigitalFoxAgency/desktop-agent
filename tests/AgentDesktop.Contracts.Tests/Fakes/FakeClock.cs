using AgentDesktop.Application.Abstractions;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>Deterministic clock used by tests. Advance time explicitly.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public void SetTo(DateTimeOffset moment) => UtcNow = moment;
}
