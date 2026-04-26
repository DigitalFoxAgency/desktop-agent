# Data Model — Desktop AI Agent Platform (MVP)

All types live in `AgentDesktop.Domain` unless explicitly noted. The
domain layer is pure: no SQLite, Avalonia, file-system, network, or
runtime types appear here. Persistence shapes are derived in
`AgentDesktop.Infrastructure/Persistence/Sqlite/` and are intentionally
not the same types.

> Conventions: identifiers are strongly-typed `record struct` ids
> (`ConversationId`, `MessageId`, `ModuleId`, `SkillId`,
> `ScenarioId`, `PolicyDecisionId`). Timestamps are `DateTimeOffset`
> sourced through `IClock` (no `DateTime.UtcNow` in Domain).

---

## 1. Chat

### Conversation
Persisted thread of messages between a user and the agent.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `ConversationId` | Stable identifier. |
| `Title` | `string` | Auto-generated from the first user message; user-editable. ≤120 chars. |
| `CreatedAt` | `DateTimeOffset` | Set on creation. |
| `LastActivityAt` | `DateTimeOffset` | Updated on every appended message. |
| `Messages` | `IReadOnlyList<Message>` | Ordered by `Index`. |

**Invariants**:
- A conversation has ≥0 messages; an empty conversation is allowed
  while drafting.
- `LastActivityAt >= CreatedAt`.
- `Title` is non-empty after the first user message lands.

### Message
A single utterance.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `MessageId` | Stable identifier. |
| `ConversationId` | `ConversationId` | Owning conversation. |
| `Index` | `int` | 0-based, monotonically increasing within the conversation. |
| `Author` | `MessageAuthor` (enum: `User`, `Agent`, `System`) | |
| `Body` | `string` | Plain text or Markdown; ≤32 KiB. |
| `CreatedAt` | `DateTimeOffset` | |
| `OriginatingDelegation` | `DelegationRef?` | Set when produced by a running module delegation. Carries `(ModuleId, OperationId)`. |
| `OriginatingSkill` | `SkillRef?` | Set when produced by a direct skill invocation (US4 advanced path). Carries `(ModuleId, SkillId)`. |

**Invariants**:
- `Index` is unique within `ConversationId`.
- `Author == System` is reserved for non-conversational artefacts
  such as cancellation notices and runtime-status banners.
- A message produced inside a running delegation has
  `OriginatingDelegation` set; a message produced by direct skill
  invocation has `OriginatingSkill` set; both fields cannot be set
  simultaneously.

### State transitions
Conversations have no lifecycle state; messages are append-only. Edits
or deletions create *new* messages (or system messages) rather than
mutating prior ones. This keeps the audit story (FR-022) trivial.

---

## 2. Modules

### Module
Loaded representation of a validated `module.json`.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `ModuleId` | Reverse-DNS or kebab-case unique id. |
| `Version` | `SemanticVersion` | SemVer 2.0. |
| `Name` | `string` | Display name; ≤80 chars. |
| `Description` | `string` | ≤500 chars. |
| `SchemaVersion` | `int` | The `module.json` schema version that produced this record. |
| `Dependencies` | `IReadOnlyList<ModuleDependency>` | Resolved, not raw. |
| `Operations` | `IReadOnlyList<Operation>` | Platform-visible entry points the platform may delegate to. See Section 3. |
| `Skills` | `IReadOnlyList<Skill>` | **Internal** to the module. Surfaced for catalogue introspection (US4 advanced) but the platform does NOT orchestrate skills — it delegates whole operations (R18). |
| `McpServers` | `IReadOnlyList<McpServerDescriptor>` | |
| `Prompts` | `IReadOnlyList<PromptTemplate>` | |
| `Policies` | `IReadOnlyList<ModulePolicy>` | Per-skill policy overrides. |
| `LoadStatus` | `ModuleLoadStatus` (enum: `Loaded`, `Unavailable`, `Incompatible`) | |
| `LoadError` | `string?` | Set when `LoadStatus != Loaded`. |
| `Source` | `ModuleSource` (`Bundled`, `Submodule`, `UserInstalled`) | Where the module came from. `Submodule` indicates the module's files live in a Git submodule under `modules/<id>/` — the MVP launchpad case. |

### ModuleDependency
| Field | Type | Notes |
|-------|------|-------|
| `ModuleId` | `ModuleId` | Required. |
| `VersionRange` | `SemanticVersionRange` | e.g., `^1.2`. |

### Skill
| Field | Type | Notes |
|-------|------|-------|
| `Id` | `SkillId` | Unique within its module. |
| `ModuleId` | `ModuleId` | Owning module. |
| `Name` | `string` | Display name. |
| `Description` | `string` | |
| `Inputs` | `IReadOnlyList<SkillParameter>` | Declared inputs. |
| `Outputs` | `IReadOnlyList<SkillParameter>` | Declared outputs. |
| `Classification` | `ActionClassification` (`Safe`, `Dangerous`) | Default classification before per-call evaluation. |
| `Kind` | `SkillKind` (`Automated`, `Human`) | `Human` skills are explicit hand-offs the runner pauses on rather than executes. Used by the launchpad for Discovery calls, dashboard work, client comms, and content approvals (launchpad constitution §12–§15). |
| `SourcePath` | `string?` | Relative path inside the module's source tree (e.g. submodule checkout) to the authoritative skill contract document — for the MVP module this is the existing `template/.claude/skills/<id>/SKILL.md`. |
| `VerificationKey` | `string?` | Token expected in `SESSION-LOG.md` (e.g. `init: verified`) before a downstream skill in the same scenario may start. Mirrors launchpad constitution §8. |

### SkillParameter
| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | |
| `Kind` | `SkillParameterKind` (`String`, `Integer`, `Number`, `Boolean`, `Path`, `Url`, `Json`) | |
| `Required` | `bool` | |
| `Description` | `string` | |

### McpServerDescriptor
| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | |
| `Command` | `string` | Executable; resolved at launch through the sandbox. |
| `Args` | `IReadOnlyList<string>` | |
| `Env` | `IReadOnlyDictionary<string,string>` | Secrets MUST be referenced by name through `ISecretStore`, never embedded. |

### PromptTemplate
| Field | Type | Notes |
|-------|------|-------|
| `Name` | `string` | |
| `Body` | `string` | Mustache-style placeholders permitted. |

### ModulePolicy
| Field | Type | Notes |
|-------|------|-------|
| `SkillId` | `SkillId` | The skill the policy applies to. |
| `Classification` | `ActionClassification` | Module-author classification override. |
| `Reason` | `string` | Human-readable rationale shown in confirmation prompts. |

**Invariants**:
- `Module.Id` + `Module.Version` is unique within the registry.
- A `Skill.Id` is unique within `Module.Id`.
- `LoadStatus = Unavailable` if any declared dependency is missing;
  `Incompatible` if `SchemaVersion` is unsupported or referenced
  skills are absent in the resolved dependency.

---

## 3. Operations & Delegations

> **Architectural note**: There is no platform-side `Scenario`
> aggregate. The previous design with `scenarios/*.yaml` driven by
> a platform scenario engine was replaced by **module-owned
> orchestration** — see research.md R5 / R18. What follows are the
> platform's view of operations declared by modules and the
> live-delegation runtime concept.

### Operation
A platform-visible entry point declared inside a module's
`module.json` under `operations[]`. Lives in the `Modules` section
conceptually because operations belong to modules.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | Unique within its owning module. |
| `Name` | `string` | Display name. ≤80 chars. |
| `Description` | `string` | ≤500 chars. |
| `Inputs` | `IReadOnlyList<SkillParameter>` | Declared inputs the platform collects from the user before delegating. |

`Module.Operations` is an `IReadOnlyList<Operation>` added to the
`Module` aggregate (see Section 2).

**Invariants**:
- `Operation.Id` is unique within `Module.Operations`.
- An operation's inputs use the same `SkillParameter` shape as
  skill parameters (validated as `string` / `integer` / `path` /
  `url` / `json` etc.).

### Delegation (runtime concept, not persisted)
The live execution of an operation. Represented in the
Application layer as the state observable from a running
`IRuntimeManager.DelegateAsync` call.

| Field | Type | Notes |
|-------|------|-------|
| `ModuleId` | `ModuleId` | The owning module. |
| `OperationId` | `string` | The operation being delegated. |
| `ConversationId` | `ConversationId` | Conversation the delegation belongs to (for chat surfacing). |
| `Inputs` | `IReadOnlyDictionary<string, object?>` | Snapshot of the inputs the user supplied. |
| `Status` | `DelegationStatus` enum | `Running → AwaitingConfirmation → AwaitingHumanHandoff → Cancelling → Completed/Cancelled/Failed`. |

The platform never reaches inside the module to drive its
internal steps. The module emits events back through callbacks
(see `contracts/IRuntimeManager.md`):
- `EmitProgressAsync(text)` — chat surface update.
- `RequestConfirmationAsync(DangerousAction)` — synchronously
  awaits the platform's policy decision (Confirmed/Declined).
- `RequestHumanHandoffAsync(stepName, instructions)` —
  synchronously awaits the user marking the step done.

**Invariants**:
- A `Confirmed` decision delivered through the callback is
  single-use; the module re-asks for any subsequent occurrence
  of the same action class (FR-013).
- Cancellation MUST drive `Status` to `Cancelling` first; the
  module's in-flight work finalises before the platform records
  `Cancelled`.
- Every delegation produces exactly one terminal status:
  `Completed`, `Cancelled`, or `Failed`.

---

## 4. Policy

### ActionClassification
Enum: `Safe`, `Dangerous`. (No `Unknown` — unknown actions default
to `Dangerous` at the engine boundary.)

### DangerousAction
Tagged record describing what is about to happen. The engine emits one
per proposed side-effect; it is the unit the user confirms.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `PolicyDecisionId` | Stable across the prompt → result lifecycle. |
| `Kind` | `DangerousActionKind` (`DeleteFile`, `GitPush`, `InstallPackage`, `RunShell`, `ModuleDeclared`) | |
| `Target` | `string` | Human-readable target (path, repo URL, package, command). |
| `Origin` | `PolicyOrigin` (`FromSkill`, `FromDelegation`) + ids | Who proposed it. `FromDelegation` carries `(ModuleId, OperationId)`; `FromSkill` carries `(ModuleId, SkillId)` (US4 advanced path). |
| `RequestedAt` | `DateTimeOffset` | |

### PolicyDecision
The persisted outcome.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `PolicyDecisionId` | Same id as `DangerousAction.Id`. |
| `Action` | `DangerousAction` | |
| `Outcome` | `PolicyOutcome` (`Confirmed`, `Declined`, `Skipped`, `Expired`) | |
| `DecidedAt` | `DateTimeOffset` | |
| `ExecutionResult` | `PolicyExecutionResult?` (`Succeeded`, `Failed`) | Set after execution attempts. |
| `ExecutionError` | `string?` | |

**Invariants**:
- A `Confirmed` decision is single-use; the engine MUST refuse to
  apply it to any other action.
- Every `DangerousAction` produced by the engine MUST land as a
  `PolicyDecision` (no orphans). This is enforced by a contract test.

**Mapping to spec FR-022 audit states**: FR-022 enumerates the
states "proposed, confirmed, declined, executed, succeeded, failed".
They map onto this model as follows:
- **proposed** ↦ creation of the `DangerousAction` record
  (`RequestedAt` timestamp). Every evaluation produces one.
- **confirmed** ↦ `PolicyOutcome.Confirmed` with `DecidedAt`.
- **declined** ↦ `PolicyOutcome.Declined` with `DecidedAt`.
- **executed** ↦ implicit: a `PolicyDecision` whose
  `ExecutionResult` is non-null indicates execution was attempted.
- **succeeded** ↦ `PolicyExecutionResult.Succeeded`.
- **failed** ↦ `PolicyExecutionResult.Failed`.

The audit log (`audit_events`) records each transition as a
separate row keyed by `PolicyDecisionId`, so the full lifecycle is
reconstructable for any single dangerous action.

---

## 5. Runtime

### RuntimeStatus
Enum-only (lives in Domain so the UI can react to it without taking
an Infrastructure dependency):
`NotInstalled → Installing → Starting → Ready → Degraded → Stopped`.

### RuntimeKind
Enum: `OpenClaw`, `Fake`. Anything more granular (e.g., NemoClaw
sub-status) lives behind `IRuntimeManager` in Application and is not
exposed to Domain.

---

## 6. Identity / Subscription

### UserAccount
| Field | Type | Notes |
|-------|------|-------|
| `AccountId` | `AccountId` | Opaque; assigned by the subscription service. |
| `Email` | `string` | Display only. |
| `SubscriptionStatus` | `SubscriptionStatus` (`Active`, `Grace`, `Expired`, `Revoked`, `Unknown`) | |
| `LastValidatedAt` | `DateTimeOffset?` | |

Sensitive material (subscription session token, model API token)
is **not** stored on `UserAccount`. It is held only in
`ISecretStore`. Domain types deliberately have no field for it.

---

## Relationships at a glance

```
UserAccount (in-memory, refreshed by ISubscriptionGate)

Conversation 1───* Message
                    Message *──0..1 DelegationRef
                    Message *──0..1 SkillRef

Module 1───* Operation     (platform-visible entry points)
Module 1───* Skill          (internal; advanced direct invocation only)
Module 1───* ModulePolicy
Module 1───* McpServerDescriptor
Module 1───* PromptTemplate
Module *───* Module (Dependencies)

Delegation (runtime concept) ──> (Module, Operation, Conversation)

PolicyDecision 1───1 DangerousAction
PolicyDecision *───0..1 DelegationRef / SkillRef (via PolicyOrigin)
```

---

## Persistence sketch (Infrastructure-only — *not* part of Domain)

SQLite tables (column lists abridged):

- `conversations(id, title, created_at, last_activity_at)`
- `messages(id, conversation_id, idx, author, body, created_at, delegation_module_id, delegation_operation_id, skill_module_id, skill_id)`
- `policy_decisions(id, kind, target, origin_kind, origin_id, requested_at, outcome, decided_at, execution_result, execution_error)`
- `audit_events(id, created_at, kind, payload_json)` — append-only log
  used for FR-022.

Modules are *not* persisted in SQLite; they are loaded from
`<userData>/modules/` on startup and on explicit refresh. Each
module declares its own operations inside `module.json`; there is
no separate scenarios store. This keeps the module manifest as the
single source of truth (FR-006, FR-007) and avoids stale-cache bugs.
