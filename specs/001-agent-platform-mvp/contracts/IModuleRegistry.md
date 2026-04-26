# Contract: `IModuleRegistry`

**Project**: `AgentDesktop.Application` (`Modules/IModuleRegistry.cs`)
**Implementations**: `ModuleRegistry` (Application), backed by
`IModuleSource` adapters in Infrastructure.

## Interface

```csharp
public interface IModuleRegistry
{
    Task RefreshAsync(CancellationToken ct);
    IReadOnlyList<Module> GetAll();
    Module? Find(ModuleId id);

    /// <summary>Resolve a (module, operation) pair before delegation. Returns null if missing.</summary>
    Operation? FindOperation(ModuleId moduleId, string operationId);

    /// <summary>US4 advanced path: resolve a module-internal skill for direct invocation. Returns null if missing.</summary>
    Skill? FindSkill(ModuleId moduleId, SkillId skillId);
}

public interface IModuleSource
{
    IAsyncEnumerable<RawModuleManifest> EnumerateAsync(CancellationToken ct);
}
```

## Behavioural contract

1. `RefreshAsync` MUST be idempotent and safe to call concurrently
   from the UI thread (it must not block on I/O — the I/O happens on
   a background task).
2. Each manifest MUST be validated against `module.schema.json`
   before it becomes a `Module`. Validation failures produce a
   `Module` with `LoadStatus = Incompatible` and a populated
   `LoadError` — they DO NOT throw.
3. Unknown major `schemaVersion` values MUST mark the module
   `Incompatible` (FR-018).
4. Modules whose declared dependencies are not satisfied MUST be
   marked `Unavailable` with `LoadError` naming the missing
   dependency (FR-008 supporting behaviour).
5. The registry MUST never expose a manifest that failed validation
   as `Loaded`.
6. `Find` and `FindSkill` MUST be O(1) average-case lookups.

## Required tests

- A valid manifest loads with `LoadStatus = Loaded`.
- A manifest with unknown `schemaVersion` loads with `Incompatible`.
- A manifest whose dependency is absent loads with `Unavailable`
  and a `LoadError` mentioning the missing id.
- Concurrent `RefreshAsync` calls do not duplicate entries.
- `FindSkill` returns `null` for an unknown skill on a known module.
