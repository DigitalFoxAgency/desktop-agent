# Contract: `ISecretStore`

**Project**: `AgentDesktop.Application` (`Secrets/ISecretStore.cs`)
**Implementations** (Infrastructure):
- `WindowsDpapiSecretStore`
- `MacKeychainSecretStore`
- `LinuxSecretStore` (libsecret)
- `EncryptedFileSecretStore` (cross-platform fallback)

## Interface

```csharp
public interface ISecretStore
{
    Task<string?> GetAsync(SecretKey key, CancellationToken ct);
    Task SetAsync(SecretKey key, string value, CancellationToken ct);
    Task DeleteAsync(SecretKey key, CancellationToken ct);
}

public readonly record struct SecretKey(string Namespace, string Name);
```

## Behavioural contract

1. Secrets MUST never be returned in log output, exceptions, or
   telemetry. Adapters MUST scrub values from any error paths.
2. `Set` MUST overwrite an existing value atomically; partial writes
   leaving the store in an inconsistent state are forbidden.
3. `Get` for a missing key MUST return `null`, not throw.
4. The fallback `EncryptedFileSecretStore` MUST use authenticated
   encryption (AES-GCM) with a key derived from a per-machine seed
   plus a per-install salt; the key file's filesystem permissions
   MUST be `0600` (or NTFS-equivalent ACL restricting to the current
   user).
5. Cross-process concurrency MUST be safe — two app instances
   reading/writing simultaneously must not corrupt the store
   (lockfile or platform primitive as appropriate).

## Required tests

- Round-trip `Set` → `Get` returns the same value.
- `Get` of a missing key returns `null`.
- `Delete` of a missing key does not throw.
- Concurrency test: 100 parallel `Set`/`Get` pairs leave the store
  consistent.
- Fallback adapter: tampering with the cipher text causes `Get` to
  return `null` and surface a `System` log entry, never the
  plaintext.
- Smoke test verifies adapter selection picks DPAPI on Windows,
  Keychain on macOS, libsecret on Linux when available, otherwise
  the fallback.
