using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Secrets;

namespace AgentPlatform.Infrastructure.Secrets;

public sealed class FileEncryptedSecretStoreOptions
{
    public string VaultDirectory { get; set; } = "/var/lib/agency/vault";
    public string MasterKeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// AES-GCM encrypted per-tenant secret store. One JSON file per tenant under VaultDirectory.
/// </summary>
public sealed class FileEncryptedSecretStore : ISecretStore, IDisposable
{
    private readonly FileEncryptedSecretStoreOptions _options;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public FileEncryptedSecretStore(FileEncryptedSecretStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.MasterKeyBase64))
        {
            throw new InvalidOperationException("MasterKeyBase64 must be configured.");
        }
        _options = options;
        _key = Convert.FromBase64String(options.MasterKeyBase64);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("MasterKey must decode to 32 bytes (AES-256).");
        }
        Directory.CreateDirectory(_options.VaultDirectory);
    }

    public async Task<string?> GetAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var bag = await LoadAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return bag.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    public async Task SetAsync(Guid tenantId, string key, string value, string? description, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bag = await LoadAsync(tenantId, cancellationToken).ConfigureAwait(false);
            bag[key] = new SecretEntry(value, description, DateTimeOffset.UtcNow);
            await SaveAsync(tenantId, bag, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bag = await LoadAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (bag.Remove(key))
            {
                await SaveAsync(tenantId, bag, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var bag = await LoadAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return bag.Keys.ToArray();
    }

    private string PathFor(Guid tenantId) => Path.Combine(_options.VaultDirectory, $"{tenantId:N}.vault");

    private async Task<Dictionary<string, SecretEntry>> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var path = PathFor(tenantId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, SecretEntry>(StringComparer.Ordinal);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length < 12 + 16)
        {
            throw new InvalidDataException($"Vault file {path} is corrupt.");
        }

        var nonce = bytes.AsSpan(0, 12).ToArray();
        var tag = bytes.AsSpan(bytes.Length - 16, 16).ToArray();
        var ciphertext = bytes.AsSpan(12, bytes.Length - 12 - 16).ToArray();
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        var json = Encoding.UTF8.GetString(plaintext);
        return JsonSerializer.Deserialize<Dictionary<string, SecretEntry>>(json)
            ?? new Dictionary<string, SecretEntry>(StringComparer.Ordinal);
    }

    private async Task SaveAsync(Guid tenantId, Dictionary<string, SecretEntry> bag, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(bag);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, output, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length + ciphertext.Length, tag.Length);

        var path = PathFor(tenantId);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, output, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private sealed record SecretEntry(string Value, string? Description, DateTimeOffset UpdatedAt);
}
