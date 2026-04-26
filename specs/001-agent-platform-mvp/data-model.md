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
| `OriginatingScenarioStep` | `ScenarioStepRef?` | Set when the message was produced by a scenario step. |
| `OriginatingSkill` | `SkillRef?` | Set when produced by a direct skill invocation. |

**Invariants**:
- `Index` is unique within `ConversationId`.
- `Author == System` is reserved for non-conversational artefacts
  such as cancellation notices and runtime-status banners.
- A message produced by a scenario step has `OriginatingScenarioStep`
  set; a message produced by direct skill invocation has
  `OriginatingSkill` set; both fields cannot be set simultaneously.

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
| `Skills` | `IReadOnlyList<Skill>` | |
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

## 3. Scenarios

### Scenario
| Field | Type | Notes |
|-------|------|-------|
| `Id` | `ScenarioId` | |
| `Version` | `SemanticVersion` | |
| `Name` | `string` | |
| `Description` | `string` | |
| `SchemaVersion` | `int` | |
| `Inputs` | `IReadOnlyList<SkillParameter>` | Inputs the scenario asks the user for. |
| `Steps` | `IReadOnlyList<ScenarioStep>` | Ordered. |
| `LoadStatus` | `ScenarioLoadStatus` (`Loaded`, `Incompatible`) | |
| `LoadError` | `string?` | |

### ScenarioStep
| Field | Type | Notes |
|-------|------|-------|
| `Index` | `int` | 0-based, ordered. |
| `ModuleId` | `ModuleId` | |
| `SkillId` | `SkillId` | |
| `InputBindings` | `IReadOnlyDictionary<string, ScenarioBinding>` | Maps skill input name → source (scenario input or prior step output). |
| `Description` | `string` | Human-readable summary of what this step does. |

### ScenarioBinding
A discriminated union: either a literal value, a reference to a
scenario input, or a reference to `step[N].outputs.<name>`.

### ScenarioStatus (runtime, not persisted as part of Scenario)
`NotStarted → Running → AwaitingConfirmation → Cancelling → Completed`,
with terminal states `Completed`, `Cancelled`, `Failed`.

**Invariants**:
- `Steps[i].InputBindings` may only reference outputs of `Steps[j]`
  with `j < i`.
- A scenario whose referenced module/skill is missing or whose
  `SchemaVersion` is unknown is `Incompatible` and cannot start.
- Cancellation MUST drive the status to `Cancelling` first; only
  after the in-flight step settles does it become `Cancelled`.

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
| `Origin` | `PolicyOrigin` (`Skill`, `ScenarioStep`) + ids | Who proposed it. |
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
                    Message *──0..1 ScenarioStepRef
                    Message *──0..1 SkillRef

Module 1───* Skill
Module 1───* ModulePolicy
Module 1───* McpServerDescriptor
Module 1───* PromptTemplate
Module *───* Module (Dependencies)

Scenario 1───* ScenarioStep ──> (ModuleId, SkillId)

PolicyDecision 1───1 DangerousAction
PolicyDecision *───0..1 ScenarioStepRef / SkillRef (via Origin)
```

---

## Persistence sketch (Infrastructure-only — *not* part of Domain)

SQLite tables (column lists abridged):

- `conversations(id, title, created_at, last_activity_at)`
- `messages(id, conversation_id, idx, author, body, created_at, scenario_step_ref, skill_ref)`
- `policy_decisions(id, kind, target, origin_kind, origin_id, requested_at, outcome, decided_at, execution_result, execution_error)`
- `audit_events(id, created_at, kind, payload_json)` — append-only log
  used for FR-022.

Modules and scenarios are *not* persisted in SQLite; they are loaded
from `<userData>/modules/` and `<userData>/scenarios/` on startup
and on explicit refresh. This keeps the manifests as the single
source of truth (FR-006, FR-008) and avoids stale-cache bugs.
