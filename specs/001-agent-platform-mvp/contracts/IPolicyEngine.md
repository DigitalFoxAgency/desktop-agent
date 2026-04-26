# Contract: `IPolicyEngine`

**Project**: `AgentDesktop.Application` (`Policies/IPolicyEngine.cs`)
**Implementations**: `DefaultPolicyEngine` (Application).
**Collaborators**: `IConfirmationPrompt` (Application abstraction,
implemented by the UI), `IAuditLog` (Application abstraction,
implemented by Infrastructure).

## Interface

```csharp
public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(
        DangerousAction action,
        CancellationToken ct);
}

public interface IConfirmationPrompt
{
    Task<bool> ConfirmAsync(DangerousAction action, CancellationToken ct);
}
```

## Behavioural contract

1. The engine MUST classify every proposed action against (a) the
   baseline table for `DeleteFile`, `GitPush`, `InstallPackage`,
   `RunShell`, and (b) the originating module's per-skill policy
   overrides (`ModulePolicy`). Anything classified `Dangerous` MUST
   require a confirmation; anything classified `Safe` proceeds
   without prompting.
2. Confirmation MUST be requested through `IConfirmationPrompt`
   exactly once per `DangerousAction`. The result MUST NOT be cached
   for any other action, even one with identical fields (FR-013).
3. Every evaluation — confirmed, declined, skipped, expired — MUST
   produce a `PolicyDecision` and append a corresponding entry to
   the audit log (FR-022).
4. Two confirmation prompts MUST NOT be in flight simultaneously;
   the engine queues additional prompts and serialises them.
5. An action whose `Kind` is unknown to the engine MUST be treated
   as `Dangerous`. (Default-deny for new categories.)
6. The engine MUST be deterministic with respect to its inputs:
   given the same `DangerousAction` and the same prompt response,
   it MUST produce the same `PolicyDecision`.

## Required tests

- Each baseline `DangerousActionKind` triggers a confirmation.
- A `Safe` skill never triggers a confirmation.
- A module-declared policy can upgrade a skill from `Safe` to
  `Dangerous`.
- Decline produces `Outcome = Declined`, no execution attempt.
- Confirm produces `Outcome = Confirmed` and a single audit entry.
- Two prompts in close succession are serialised, not parallelised.
- Unknown `Kind` defaults to `Dangerous`.
- 100% branch coverage (constitution gate).
