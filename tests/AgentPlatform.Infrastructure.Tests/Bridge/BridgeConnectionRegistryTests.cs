using AgentPlatform.Infrastructure.Bridge;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Bridge;

public sealed class BridgeConnectionRegistryTests
{
    [Fact]
    public void IssueConnectToken_round_trips_phase_and_tenant_ids()
    {
        var registry = new BridgeConnectionRegistry(NullLoggerFactory.Instance);
        var phaseRunId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = registry.IssueConnectToken(phaseRunId, tenantId);
        var ok = registry.TryConsumeToken(token, out var resolvedPhase, out var resolvedTenant);

        ok.Should().BeTrue();
        resolvedPhase.Should().Be(phaseRunId);
        resolvedTenant.Should().Be(tenantId);
    }

    [Fact]
    public void TryConsumeToken_returns_false_for_unknown_token()
    {
        var registry = new BridgeConnectionRegistry(NullLoggerFactory.Instance);

        var ok = registry.TryConsumeToken("not-a-real-token", out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryConsumeToken_can_only_consume_a_token_once()
    {
        var registry = new BridgeConnectionRegistry(NullLoggerFactory.Instance);
        var token = registry.IssueConnectToken(Guid.NewGuid(), Guid.NewGuid());

        registry.TryConsumeToken(token, out _, out _).Should().BeTrue();
        registry.TryConsumeToken(token, out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task WaitForConnectionAsync_times_out_when_bridge_never_connects()
    {
        var registry = new BridgeConnectionRegistry(NullLoggerFactory.Instance);

        var act = async () => await registry.WaitForConnectionAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(50), default);

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
