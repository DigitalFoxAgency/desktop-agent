using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentDesktop.Application.Secrets;
using AgentDesktop.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Secrets;

/// <summary>
/// Cross-platform fallback secret store: AES-GCM ciphertext stored
/// in a single user-private file. Used universally at MVP; the
/// platform-specific OS-secure adapters (DPAPI / Keychain /
/// libsecret) are Phase-7 polish (R3 + plan.md). The encryption
/// key is derived per-install from a salt file co-located with
/// the secrets file (machine-bound seed); the salt file's
/// permissions are 0600 on POSIX systems.
/// </summary>
public sealed class EncryptedFileSecretStore : ISecretStore, IDisposable
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int SaltBytes = 32;
    private const int Pbkdf2Iterations = 200_000;

    private readonly EncryptedFileSecretStoreOptions _options;
    private readonly ILogger<EncryptedFileSecretStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public EncryptedFileSecretStore(
        IOptions<EncryptedFileSecretStoreOptions> options,
        ILogger<EncryptedFileSecretStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    public async Task<string?> GetAsync(SecretKey key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            return entries.TryGetValue(SerializeKey(key), out var value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(SecretKey key, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            entries[SerializeKey(key)] = value;
            await SaveAsync(entries, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(SecretKey key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            if (entries.Remove(SerializeKey(key)))
            {
                await SaveAsync(entries, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string SerializeKey(SecretKey key) => $"{key.Namespace}/{key.Name}";

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        EnsureDirectory();
        if (!File.Exists(_options.FilePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        byte[] cipherBlob;
        try
        {
            cipherBlob = await File.ReadAllBytesAsync(_options.FilePath, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (cipherBlob.Length < NonceBytes + TagBytes)
        {
            _logger.LogWarning("Secret store file at {Path} is corrupt or truncated; treating as empty.", _options.FilePath);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var nonce = cipherBlob.AsSpan(0, NonceBytes).ToArray();
        var tag = cipherBlob.AsSpan(NonceBytes, TagBytes).ToArray();
        var cipherText = cipherBlob.AsSpan(NonceBytes + TagBytes).ToArray();
        var plain = new byte[cipherText.Length];

        var key = await DeriveKeyAsync(ct).ConfigureAwait(false);
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipherText, tag, plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            _logger.LogWarning("Secret store decryption failed (tampered or wrong key); treating as empty.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var json = Encoding.UTF8.GetString(plain);
        var deserialised = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        return deserialised is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(deserialised, StringComparer.Ordinal);
    }

    private async Task SaveAsync(Dictionary<string, string> entries, CancellationToken ct)
    {
        EnsureDirectory();

        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, JsonOptions));
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipherText = new byte[plain.Length];
        var tag = new byte[TagBytes];

        var key = await DeriveKeyAsync(ct).ConfigureAwait(false);
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plain, cipherText, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var blob = new byte[NonceBytes + TagBytes + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
        Buffer.BlockCopy(tag, 0, blob, NonceBytes, TagBytes);
        Buffer.BlockCopy(cipherText, 0, blob, NonceBytes + TagBytes, cipherText.Length);

        var tempPath = _options.FilePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, blob, ct).ConfigureAwait(false);
        File.Move(tempPath, _options.FilePath, overwrite: true);
        TryRestrictPermissions(_options.FilePath);
    }

    private async Task<byte[]> DeriveKeyAsync(CancellationToken ct)
    {
        var salt = await EnsureSaltAsync(ct).ConfigureAwait(false);
        var seed = MachineBoundSeed();
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password: seed,
                salt: salt,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: KeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }
    }

    private async Task<byte[]> EnsureSaltAsync(CancellationToken ct)
    {
        EnsureDirectory();
        if (File.Exists(_options.SaltPath))
        {
            return await File.ReadAllBytesAsync(_options.SaltPath, ct).ConfigureAwait(false);
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        await File.WriteAllBytesAsync(_options.SaltPath, salt, ct).ConfigureAwait(false);
        TryRestrictPermissions(_options.SaltPath);
        return salt;
    }

    private static byte[] MachineBoundSeed()
    {
        // Per-install fingerprint: machine name + user name + OS.
        // Combined with the per-install salt this gives a key that
        // doesn't transfer if the file is copied to another machine
        // without the salt file. (Stronger machine-binding via
        // platform-specific entropy is the job of the OS-secure
        // adapters in Phase 7.)
        var fingerprint = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.Platform}";
        return Encoding.UTF8.GetBytes(fingerprint);
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_options.FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
                // Best effort; nothing actionable.
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
        // On Windows, NTFS ACLs are inherited from the user profile dir
        // by default — restrictive enough at MVP. Phase 7 strengthens this.
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };
}

/// <summary>Configuration for <see cref="EncryptedFileSecretStore"/>.</summary>
public sealed class EncryptedFileSecretStoreOptions
{
    /// <summary>Absolute path to the encrypted secrets file.</summary>
    public string FilePath { get; set; } = "secrets.bin";

    /// <summary>Absolute path to the per-install salt file.</summary>
    public string SaltPath { get; set; } = "secrets.salt";
}
