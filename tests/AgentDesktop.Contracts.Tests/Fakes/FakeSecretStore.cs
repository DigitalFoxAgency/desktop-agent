using System.Collections.Concurrent;
using AgentDesktop.Application.Secrets;
using AgentDesktop.Domain;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>In-memory secret store. Concurrency-safe; no encryption (tests only).</summary>
public sealed class FakeSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<SecretKey, string> _entries = new();

    public Task<string?> GetAsync(SecretKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _entries.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task SetAsync(SecretKey key, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        ct.ThrowIfCancellationRequested();
        _entries[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SecretKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
