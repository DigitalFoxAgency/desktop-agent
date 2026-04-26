using AgentDesktop.Application.Secrets;
using AgentDesktop.Contracts.Tests;
using AgentDesktop.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Tests.Secrets;

/// <summary>
/// Runs the cross-project secret-store contract suite against the
/// real <see cref="EncryptedFileSecretStore"/> with a per-test
/// temp directory.
/// </summary>
public sealed class EncryptedFileSecretStoreContractTests : SecretStoreContractTests, IDisposable
{
    private readonly string _tempDir;
    private readonly List<EncryptedFileSecretStore> _created = new();

    public EncryptedFileSecretStoreContractTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentdesktop-secret-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var s in _created)
        {
            s.Dispose();
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    protected override ISecretStore CreateStore()
    {
        var options = Options.Create(new EncryptedFileSecretStoreOptions
        {
            FilePath = Path.Combine(_tempDir, "secrets.bin"),
            SaltPath = Path.Combine(_tempDir, "secrets.salt"),
        });
        var store = new EncryptedFileSecretStore(options, NullLogger<EncryptedFileSecretStore>.Instance);
        _created.Add(store);
        return store;
    }

    [Fact]
    public async Task Tampered_ciphertext_is_treated_as_empty()
    {
        var optionsValue = new EncryptedFileSecretStoreOptions
        {
            FilePath = Path.Combine(_tempDir, "tamper.bin"),
            SaltPath = Path.Combine(_tempDir, "tamper.salt"),
        };
        var options = Options.Create(optionsValue);

        using var first = new EncryptedFileSecretStore(options, NullLogger<EncryptedFileSecretStore>.Instance);
        await first.SetAsync(new("ns", "k"), "value", CancellationToken.None);

        // Flip a byte in the cipher region (after nonce + tag).
        var bytes = await File.ReadAllBytesAsync(optionsValue.FilePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(optionsValue.FilePath, bytes);

        using var second = new EncryptedFileSecretStore(options, NullLogger<EncryptedFileSecretStore>.Instance);
        var read = await second.GetAsync(new("ns", "k"), CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task Saved_value_persists_across_store_instances()
    {
        var optionsValue = new EncryptedFileSecretStoreOptions
        {
            FilePath = Path.Combine(_tempDir, "persist.bin"),
            SaltPath = Path.Combine(_tempDir, "persist.salt"),
        };
        var options = Options.Create(optionsValue);

        using (var writer = new EncryptedFileSecretStore(options, NullLogger<EncryptedFileSecretStore>.Instance))
        {
            await writer.SetAsync(new("ns", "k"), "value-across", CancellationToken.None);
        }

        using var reader = new EncryptedFileSecretStore(options, NullLogger<EncryptedFileSecretStore>.Instance);
        var read = await reader.GetAsync(new("ns", "k"), CancellationToken.None);
        read.Should().Be("value-across");
    }
}
