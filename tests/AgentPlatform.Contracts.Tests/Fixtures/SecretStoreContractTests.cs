using AgentPlatform.Application.Secrets;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Contracts.Tests.Fixtures;

public abstract class SecretStoreContractTests
{
    private static readonly string[] ExpectedKeys = { "K1", "K2" };

    protected abstract ISecretStore CreateStore();

    [Fact]
    public async Task Set_and_Get_round_trip_within_tenant()
    {
        var store = CreateStore();
        var tenantId = Guid.NewGuid();

        await store.SetAsync(tenantId, "ANTHROPIC_API_KEY", "sk-test", description: "test", CancellationToken.None);
        var v = await store.GetAsync(tenantId, "ANTHROPIC_API_KEY", CancellationToken.None);

        v.Should().Be("sk-test");
    }

    [Fact]
    public async Task Tenants_cannot_read_each_others_secrets()
    {
        var store = CreateStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await store.SetAsync(a, "K", "value-a", null, CancellationToken.None);
        await store.SetAsync(b, "K", "value-b", null, CancellationToken.None);

        (await store.GetAsync(a, "K", CancellationToken.None)).Should().Be("value-a");
        (await store.GetAsync(b, "K", CancellationToken.None)).Should().Be("value-b");
    }

    [Fact]
    public async Task Delete_removes_key()
    {
        var store = CreateStore();
        var t = Guid.NewGuid();
        await store.SetAsync(t, "K", "v", null, CancellationToken.None);
        await store.DeleteAsync(t, "K", CancellationToken.None);

        (await store.GetAsync(t, "K", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ListKeys_returns_only_tenant_keys()
    {
        var store = CreateStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await store.SetAsync(a, "K1", "1", null, CancellationToken.None);
        await store.SetAsync(a, "K2", "2", null, CancellationToken.None);
        await store.SetAsync(b, "OTHER", "x", null, CancellationToken.None);

        var keys = await store.ListKeysAsync(a, CancellationToken.None);
        keys.Should().BeEquivalentTo(ExpectedKeys);
    }
}
