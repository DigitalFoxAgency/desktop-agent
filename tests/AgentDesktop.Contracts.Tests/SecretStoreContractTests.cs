using AgentDesktop.Application.Secrets;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;

namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Contract tests for <see cref="ISecretStore"/>. Run against
/// the in-memory fake here; the same suite runs against the
/// real <c>EncryptedFileSecretStore</c> in
/// <c>AgentDesktop.Infrastructure.Tests</c>.
/// </summary>
public abstract class SecretStoreContractTests
{
    /// <summary>Concrete fixtures override this to supply their store.</summary>
    protected abstract ISecretStore CreateStore();

    [Fact]
    public async Task Get_returns_null_for_missing_key()
    {
        var store = CreateStore();
        var value = await store.GetAsync(Key("ns", "missing"), CancellationToken.None);
        value.Should().BeNull();
    }

    [Fact]
    public async Task Set_then_Get_round_trips_the_value()
    {
        var store = CreateStore();
        var key = Key("subscription", "session-token");

        await store.SetAsync(key, "secret-token-123", CancellationToken.None);
        var roundTrip = await store.GetAsync(key, CancellationToken.None);

        roundTrip.Should().Be("secret-token-123");
    }

    [Fact]
    public async Task Set_overwrites_existing_value()
    {
        var store = CreateStore();
        var key = Key("subscription", "session-token");

        await store.SetAsync(key, "first", CancellationToken.None);
        await store.SetAsync(key, "second", CancellationToken.None);

        (await store.GetAsync(key, CancellationToken.None)).Should().Be("second");
    }

    [Fact]
    public async Task Delete_removes_the_value()
    {
        var store = CreateStore();
        var key = Key("subscription", "session-token");

        await store.SetAsync(key, "value", CancellationToken.None);
        await store.DeleteAsync(key, CancellationToken.None);

        (await store.GetAsync(key, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_of_missing_key_does_not_throw()
    {
        var store = CreateStore();
        var act = () => store.DeleteAsync(Key("ns", "missing"), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Distinct_namespaces_do_not_collide()
    {
        var store = CreateStore();

        await store.SetAsync(Key("a", "k"), "value-a", CancellationToken.None);
        await store.SetAsync(Key("b", "k"), "value-b", CancellationToken.None);

        (await store.GetAsync(Key("a", "k"), CancellationToken.None)).Should().Be("value-a");
        (await store.GetAsync(Key("b", "k"), CancellationToken.None)).Should().Be("value-b");
    }

    [Fact]
    public async Task Concurrent_writes_leave_store_consistent()
    {
        var store = CreateStore();
        const int Iterations = 50;

        var setters = Enumerable.Range(0, Iterations).Select(async i =>
        {
            await store.SetAsync(Key("concurrency", $"k{i}"), $"v{i}", CancellationToken.None);
        }).ToArray();

        await Task.WhenAll(setters);

        for (var i = 0; i < Iterations; i++)
        {
            var value = await store.GetAsync(Key("concurrency", $"k{i}"), CancellationToken.None);
            value.Should().Be($"v{i}");
        }
    }

    private static SecretKey Key(string ns, string name) => new(ns, name);
}

/// <summary>Run the contract suite against the in-memory fake.</summary>
public sealed class FakeSecretStoreContractTests : SecretStoreContractTests
{
    protected override ISecretStore CreateStore() => new FakeSecretStore();
}
