---
description: "Task list for 001-agent-platform-mvp"
---

# Tasks: Desktop AI Agent Platform (MVP)

**Input**: Design documents from `/specs/001-agent-platform-mvp/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included by default. The project constitution
(`.specify/memory/constitution.md`, Principle II) mandates TDD;
contract tests for every service interface are non-negotiable.

**Organization**: Tasks are grouped by user story (per spec.md
priorities). The two P1 stories are split into two phases —
US1 (sign-in + chat) is the headline MVP slice; US3 (dangerous-action
confirmation) is also P1 and lands immediately after, because it is
required before any module skill or scenario can execute safely.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on
  incomplete tasks).
- **[Story]**: `US1` … `US4` map to spec.md user stories.
- File paths assume the project layout in `plan.md`.

## Path Conventions

- Solution: `AgentDesktop.sln` at repo root.
- Source: `src/AgentDesktop.{Domain,Application,Infrastructure,Desktop,Api}/`.
- Tests: `tests/AgentDesktop.{Domain,Application,Infrastructure,Desktop,Contracts}.Tests/` and `tests/AgentDesktop.Bench/`.
- Bundled assets: `modules/df-client-launchpad/` (already populated), `scenarios/*.yaml` (already populated).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution scaffolding, tooling, and CI baseline.

- [x] T001 Create `AgentDesktop.sln` at repo root referencing all projects below
- [x] T002 [P] Create `src/AgentDesktop.Domain/AgentDesktop.Domain.csproj` (net9.0, nullable enable, no project references)
- [x] T003 [P] Create `src/AgentDesktop.Application/AgentDesktop.Application.csproj` (references Domain only)
- [x] T004 [P] Create `src/AgentDesktop.Infrastructure/AgentDesktop.Infrastructure.csproj` (references Application + Domain)
- [x] T005 [P] Create `src/AgentDesktop.Desktop/AgentDesktop.Desktop.csproj` (Avalonia 11.x, references Application; references Infrastructure only from `Program.cs` composition root)
- [x] T006 [P] Create `src/AgentDesktop.Api/AgentDesktop.Api.csproj` (ASP.NET Core Minimal APIs, references Domain)
- [x] T007 [P] Create `tests/AgentDesktop.Domain.Tests/AgentDesktop.Domain.Tests.csproj` (xUnit + FluentAssertions)
- [x] T008 [P] Create `tests/AgentDesktop.Application.Tests/AgentDesktop.Application.Tests.csproj`
- [x] T009 [P] Create `tests/AgentDesktop.Infrastructure.Tests/AgentDesktop.Infrastructure.Tests.csproj`
- [x] T010 [P] Create `tests/AgentDesktop.Desktop.Tests/AgentDesktop.Desktop.Tests.csproj` (Avalonia.Headless + Verify)
- [x] T011 [P] Create `tests/AgentDesktop.Contracts.Tests/AgentDesktop.Contracts.Tests.csproj` (cross-project contract suite)
- [x] T012 [P] Create `tests/AgentDesktop.Bench/AgentDesktop.Bench.csproj` (BenchmarkDotNet)
- [x] T013 [P] Add `Directory.Build.props` at repo root (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`, `<LangVersion>12</LangVersion>`, `<EnableNETAnalyzers>true</EnableNETAnalyzers>`)
- [x] T014 [P] Add `.editorconfig` at repo root with .NET formatting rules + analyzer severity overrides aligned with constitution Principle I
- [x] T015 [P] Add `Directory.Packages.props` (CPM) pinning Avalonia, Microsoft.Data.Sqlite, Dapper, NJsonSchema, YamlDotNet, ModelContextProtocol, CommunityToolkit.Mvvm, xUnit, FluentAssertions, NSubstitute, Verify, BenchmarkDotNet
- [x] T016 [P] Add `.github/workflows/ci.yml` matrix (`ubuntu-latest`, `macos-latest`, `windows-latest`): restore → build (`-warnaserror`) → test → coverage upload
- [x] T017 [P] Add `.github/workflows/bench-smoke.yml` (PR-only) running `tests/AgentDesktop.Bench` smoke filter
- [x] T018 [P] Add `.github/dependabot.yml` for nuget + github-actions ecosystems
- [x] T019 [P] Add `LICENSE` and skeleton `README.md` (point at `specs/001-agent-platform-mvp/quickstart.md` for build instructions)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, service interfaces, persistence schema,
and runtime fakes — every user story depends on this phase.

**⚠️ CRITICAL**: No user story phase may begin until this phase
is complete and `dotnet test` is green.

### Domain layer (no external deps)

- [x] T020 [P] Create strongly-typed ids in `src/AgentDesktop.Domain/Identifiers.cs` — `ConversationId`, `MessageId`, `ModuleId`, `SkillId`, `ScenarioId`, `PolicyDecisionId`, `AccountId`, `SecretKey` (record structs)
- [x] T021 [P] Create `src/AgentDesktop.Domain/SemanticVersion.cs` and `SemanticVersionRange.cs` (parser + comparison)
- [x] T022 [P] Create domain enums in `src/AgentDesktop.Domain/Enums.cs` — `MessageAuthor`, `ActionClassification`, `RuntimeStatus`, `RuntimeKind`, `ModuleLoadStatus`, `ScenarioLoadStatus`, `ModuleSource`, `SkillKind`, `DangerousActionKind`, `PolicyOutcome`, `PolicyExecutionResult`, `SubscriptionStatus`
- [x] T023 [P] Create chat domain types in `src/AgentDesktop.Domain/Chat/` — `Conversation.cs`, `Message.cs`, `ScenarioStepRef.cs`, `SkillRef.cs`
- [x] T024 [P] Create module domain types in `src/AgentDesktop.Domain/Modules/` — `Module.cs`, `Skill.cs` (with `Kind`, `SourcePath`, `VerificationKey`), `SkillParameter.cs`, `ModuleDependency.cs`, `McpServerDescriptor.cs`, `PromptTemplate.cs`, `ModulePolicy.cs`
- [x] T025 [P] Create scenario domain types in `src/AgentDesktop.Domain/Scenarios/` — `Scenario.cs`, `ScenarioStep.cs`, `ScenarioBinding.cs`, `ScenarioStatus.cs`
- [x] T026 [P] Create policy domain types in `src/AgentDesktop.Domain/Policies/` — `DangerousAction.cs`, `PolicyDecision.cs`, `PolicyOrigin.cs`
- [x] T027 [P] Create identity domain types in `src/AgentDesktop.Domain/Identity/UserAccount.cs`
- [x] T028 [P] Domain tests: invariants for Conversation/Message in `tests/AgentDesktop.Domain.Tests/Chat/ConversationTests.cs` (LastActivityAt monotonic, Message.Index unique within Conversation, OriginatingScenarioStep XOR OriginatingSkill)
- [x] T029 [P] Domain tests: SemanticVersion + range in `tests/AgentDesktop.Domain.Tests/SemanticVersionTests.cs`

### Application abstractions (interface-first)

- [x] T030 [P] Define `IClock` in `src/AgentDesktop.Application/Abstractions/IClock.cs`
- [x] T031 [P] Define `IConfirmationPrompt` in `src/AgentDesktop.Application/Abstractions/IConfirmationPrompt.cs`
- [x] T032 [P] Define `IAuditLog` in `src/AgentDesktop.Application/Abstractions/IAuditLog.cs`
- [x] T033 [P] Define `IModuleSource` and `IScenarioSource` in `src/AgentDesktop.Application/Abstractions/`
- [x] T034 [P] Define `IChatRepository` in `src/AgentDesktop.Application/Chat/IChatRepository.cs`
- [x] T035 [P] Define `IChatService` + `MessageChunk` in `src/AgentDesktop.Application/Chat/IChatService.cs` (matches `contracts/IChatService.md`)
- [x] T036 [P] Define `IModuleRegistry` in `src/AgentDesktop.Application/Modules/IModuleRegistry.cs` (matches `contracts/IModuleRegistry.md`)
- [x] T037 [P] Define `IScenarioRegistry` + `IScenarioRunner` + `ScenarioEvent` in `src/AgentDesktop.Application/Scenarios/IScenarioRunner.cs`
- [x] T038 [P] Define `IPolicyEngine` in `src/AgentDesktop.Application/Policies/IPolicyEngine.cs`
- [x] T039 [P] Define `IRuntimeManager` + `SkillInvocationResult` in `src/AgentDesktop.Application/Runtime/IRuntimeManager.cs`
- [x] T040 [P] Define `ISecretStore` in `src/AgentDesktop.Application/Secrets/ISecretStore.cs`
- [x] T041 [P] Define `ISubscriptionGate` in `src/AgentDesktop.Application/Subscription/ISubscriptionGate.cs`
- [x] T042 [P] Define `ISessionLog` in `src/AgentDesktop.Application/Modules/ISessionLog.cs` (reads `[<skill>: verified]` records — required by launchpad scenario gating)

### Contract-test scaffolding

- [x] T043 Create contract-test base in `tests/AgentDesktop.Contracts.Tests/ContractFixture.cs` and `RequiresLiveRuntimeAttribute.cs` (xUnit trait), wired so default CI excludes the live trait
- [x] T044 [P] Add `tests/AgentDesktop.Contracts.Tests/Fakes/FakeRuntimeManager.cs` — programmable, deterministic, honours full `RuntimeStatus` state machine; can pre-program responses per `(moduleId, skillId)`
- [x] T045 [P] Add `tests/AgentDesktop.Contracts.Tests/Fakes/FakeClock.cs`, `FakeAuditLog.cs`, `FakeConfirmationPrompt.cs`, `FakeSecretStore.cs`, `FakeChatRepository.cs`

### Infrastructure foundation (no story-specific behaviour yet)

- [ ] T046 Create migrations folder + initial schema script in `src/AgentDesktop.Infrastructure/Persistence/Sqlite/Migrations/0001_init.sql` (tables: `conversations`, `messages`, `policy_decisions`, `audit_events`)
- [ ] T047 [P] Implement `SqliteConnectionFactory` in `src/AgentDesktop.Infrastructure/Persistence/Sqlite/SqliteConnectionFactory.cs` (resolves per-user data dir, applies migrations on first open)
- [ ] T048 [P] Implement `EncryptedFileSecretStore` (cross-platform fallback, AES-GCM) in `src/AgentDesktop.Infrastructure/Secrets/EncryptedFileSecretStore.cs`
- [ ] T049 [P] Implement `WindowsDpapiSecretStore` in `src/AgentDesktop.Infrastructure/Secrets/WindowsDpapiSecretStore.cs`
- [ ] T050 [P] Implement `MacKeychainSecretStore` in `src/AgentDesktop.Infrastructure/Secrets/MacKeychainSecretStore.cs`
- [ ] T051 [P] Implement `LinuxSecretStore` (libsecret P/Invoke, fallback to `EncryptedFileSecretStore` if unavailable) in `src/AgentDesktop.Infrastructure/Secrets/LinuxSecretStore.cs`
- [ ] T052 Implement `SecretStoreFactory` in `src/AgentDesktop.Infrastructure/Secrets/SecretStoreFactory.cs` selecting the platform adapter at runtime
- [ ] T053 [P] Contract test for `ISecretStore` against the live platform adapter and the encrypted-file fallback in `tests/AgentDesktop.Contracts.Tests/SecretStoreContractTests.cs`

### Desktop composition root

- [ ] T054 Create `src/AgentDesktop.Desktop/Program.cs` — CLI args (`--fake-runtime`), DI registration of Application + Infrastructure (the only Infrastructure-aware file in `Desktop`)
- [ ] T055 [P] Create design tokens + base styles in `src/AgentDesktop.Desktop/Theme/Tokens.axaml` (color, spacing, typography)
- [ ] T056 [P] Create localisation infrastructure in `src/AgentDesktop.Desktop/Resources/Strings.resx` and `LocalizationProvider.cs` (English-only at MVP, externalised)
- [ ] T057 [P] Add UI-thread watchdog (debug-only) in `src/AgentDesktop.Desktop/Diagnostics/UiThreadWatchdog.cs` asserting no >50 ms synchronous work per frame (constitution Principle IV)
- [x] T123 [P] Contract test `IRuntimeManager` in `tests/AgentDesktop.Contracts.Tests/RuntimeManagerContractTests.cs` covering the documented state machine (`NotInstalled → Installing → Starting → Ready → Degraded → Stopped`), illegal transitions throw, `InvokeSkillAsync` throws a typed exception naming current status when not `Ready`, `StreamChatAsync` finalises exactly once with `IsFinal=true`, double dispose is a no-op (idempotent). Runs against `FakeRuntimeManager` only at MVP; the same tests are reused against `ProcessRuntimeManager` in Phase 7 behind the `RequiresLiveRuntime` trait

**Checkpoint**: Foundational ready — all interfaces, fakes, and
schema in place; `dotnet test` green; user-story phases may begin.

---

## Phase 3: User Story 1 — Sign in, configure model token, and chat (Priority: P1) 🎯 MVP

**Goal**: A subscriber can install, sign in, save a model API token,
send a message, see streamed reply, and find the conversation again
after restart.

**Independent Test**: Fresh install → sign in with valid subscription
→ paste token → send message → observe streamed reply → close and
reopen → conversation present in history.

### Tests for User Story 1

- [ ] T058 [P] [US1] Contract test `IChatService` in `tests/AgentDesktop.Contracts.Tests/ChatServiceContractTests.cs`: persists user message before runtime call, exactly one `IsFinal=true` chunk, cancellation leaves consistent state, `Degraded` runtime emits `System` message + final chunk
- [ ] T059 [P] [US1] Integration test `SqliteChatRepository` round-trip in `tests/AgentDesktop.Infrastructure.Tests/Persistence/SqliteChatRepositoryTests.cs` (real file-backed temp DB)
- [ ] T060 [P] [US1] Integration test `HttpSubscriptionGate` against a `WebApplicationFactory<Program>` of `AgentDesktop.Api` in `tests/AgentDesktop.Infrastructure.Tests/Subscription/HttpSubscriptionGateTests.cs`
- [ ] T061 [P] [US1] Headless UI test `SignInView` keyboard-only flow in `tests/AgentDesktop.Desktop.Tests/Views/SignInViewTests.cs` (a11y assertion: focus order, label association, contrast)

### Implementation for User Story 1

- [ ] T062 [P] [US1] Implement `SqliteChatRepository : IChatRepository` in `src/AgentDesktop.Infrastructure/Persistence/Sqlite/SqliteChatRepository.cs` (Dapper, append-only messages, transaction per `SendMessageAsync` finalisation)
- [ ] T063 [P] [US1] Implement `SubscriptionGate` validation cache + grace-window logic in `src/AgentDesktop.Application/Subscription/SubscriptionGate.cs` (uses `ISubscriptionGate` adapter)
- [ ] T064 [P] [US1] Implement `HttpSubscriptionGate` in `src/AgentDesktop.Infrastructure/Subscription/HttpSubscriptionGate.cs` (HttpClient + token storage via `ISecretStore`)
- [ ] T065 [US1] Implement `ChatService : IChatService` in `src/AgentDesktop.Application/Chat/ChatService.cs` — orchestrates `IChatRepository`, `IRuntimeManager.StreamChatAsync`, `ISubscriptionGate`, surfacing `System` messages on runtime degradation (depends on T062, T063, T065's chunk type from foundational T035)
- [ ] T066 [US1] Application test for `ChatService` ordering and degradation paths in `tests/AgentDesktop.Application.Tests/Chat/ChatServiceTests.cs` (uses `FakeRuntimeManager`, `FakeChatRepository`)
- [ ] T067 [P] [US1] Implement `SignInViewModel` in `src/AgentDesktop.Desktop/ViewModels/SignInViewModel.cs` (CommunityToolkit.Mvvm `[ObservableProperty]`/`[RelayCommand]`)
- [ ] T068 [P] [US1] Implement `SignInView.axaml` in `src/AgentDesktop.Desktop/Views/SignInView.axaml`
- [ ] T069 [P] [US1] Implement `ChatViewModel` in `src/AgentDesktop.Desktop/ViewModels/ChatViewModel.cs` (binds `IAsyncEnumerable<MessageChunk>`)
- [ ] T070 [P] [US1] Implement `ChatView.axaml` in `src/AgentDesktop.Desktop/Views/ChatView.axaml`
- [ ] T071 [P] [US1] Implement `ConversationListViewModel` and `ConversationListView.axaml` in `src/AgentDesktop.Desktop/{ViewModels,Views}/`
- [ ] T072 [US1] Wire `Program.cs` to register `ChatService`, `SqliteChatRepository`, `HttpSubscriptionGate`, `SecretStoreFactory`, `IClock`
- [ ] T073 [US1] Implement `Api/Endpoints/SubscriptionEndpoints.cs` — `POST /v1/subscription/validate` returning subscription status (depends on T072 only conceptually; lives in `Api`)
- [ ] T074 [US1] End-to-end smoke test using `FakeRuntimeManager` in `tests/AgentDesktop.Desktop.Tests/EndToEnd/SignInAndChatTests.cs` (sign in → send message → assert chunks render → restart simulation → assert history persisted)
- [ ] T124 [US1] Application test `ChatService` honours `ISubscriptionGate` in `tests/AgentDesktop.Application.Tests/Chat/ChatServiceSubscriptionTests.cs` — covers FR-023: when `ISubscriptionGate` reports `Expired` / `Revoked` / out-of-grace `Unknown`, `ChatService.SendMessageAsync` MUST refuse, surface a `System` message naming the recovery path, leave existing conversations readable via `GetConversationAsync` / `ListConversationsAsync`, and emit no runtime call. Re-enable on transition back to `Active`

**Checkpoint**: US1 fully functional. SC-001 (≤5 min onboarding),
SC-002 (≤2 s first chunk), SC-006 (≤2 s history restore) measurable.

---

## Phase 4: User Story 3 — Confirm dangerous actions before they happen (Priority: P1)

**Goal**: Every dangerous action surfaces a confirmation prompt
naming action + target before any side effect; declining skips,
confirming runs once.

**Independent Test**: Trigger an agent request that would delete a
file → see confirmation prompt naming the file → decline → confirm
no deletion + chat records skip → trigger again → confirm → file
deleted exactly once + audit entry recorded.

### Tests for User Story 3

- [ ] T075 [P] [US3] Contract test `IPolicyEngine` in `tests/AgentDesktop.Contracts.Tests/PolicyEngineContractTests.cs` covering each baseline `DangerousActionKind`, `Safe` skips prompt, module override `Safe→Dangerous`, decline → no execution + audit entry, single-use Confirmed, prompts serialised, unknown `Kind` → `Dangerous`
- [ ] T076 [P] [US3] Branch-coverage test for `DefaultPolicyEngine` in `tests/AgentDesktop.Application.Tests/Policies/DefaultPolicyEngineCoverageTests.cs` (constitution gate: 100% branch coverage on the engine)
- [ ] T077 [P] [US3] Headless a11y test for `ConfirmationDialog` in `tests/AgentDesktop.Desktop.Tests/Views/ConfirmationDialogTests.cs` (keyboard-only confirm/decline, target text reads correctly to a screen-reader stub, focus trapped while open)

### Implementation for User Story 3

- [ ] T078 [P] [US3] Implement baseline classification table in `src/AgentDesktop.Application/Policies/BaselineClassificationTable.cs` (enumerates `DangerousActionKind`)
- [ ] T079 [US3] Implement `DefaultPolicyEngine : IPolicyEngine` in `src/AgentDesktop.Application/Policies/DefaultPolicyEngine.cs` (combines baseline + module-declared `ModulePolicy`, queues prompts via `IConfirmationPrompt`, writes `PolicyDecision` through `IAuditLog`)
- [ ] T080 [P] [US3] Implement `SqliteAuditLog : IAuditLog` in `src/AgentDesktop.Infrastructure/Persistence/Sqlite/SqliteAuditLog.cs` (append-only `audit_events` and `policy_decisions` tables)
- [ ] T081 [P] [US3] Implement `ConfirmationDialog.axaml` in `src/AgentDesktop.Desktop/Views/ConfirmationDialog.axaml` (single shared component reused for every dangerous action)
- [ ] T082 [P] [US3] Implement `AvaloniaConfirmationPrompt : IConfirmationPrompt` in `src/AgentDesktop.Desktop/Adapters/AvaloniaConfirmationPrompt.cs` (serialises requests on a single `SemaphoreSlim`)
- [ ] T083 [US3] Wire `DefaultPolicyEngine` into `ChatService` skill-invocation path so all agent-initiated actions evaluate through the engine before the runtime executes them
- [ ] T084 [US3] Update `ChatService` to surface a `System` message on `Declined`/`Skipped` outcomes (visible in chat surface, traceable via `PolicyDecisionId`)
- [ ] T085 [US3] Register `DefaultPolicyEngine`, `SqliteAuditLog`, `AvaloniaConfirmationPrompt` in `Program.cs`

**Checkpoint**: US3 fully functional. SC-003 (100% of dangerous
actions prompted), SC-004 (0 unconfirmed dangerous executions),
FR-022 audit log measurable.

---

## Phase 5: User Story 2 — Delegate an operation to a module and follow it in chat (Priority: P2)

> **Architectural pivot — research.md R18**: this phase is the
> **delegation runner**, not a platform-side scenario engine.
> Modules are self-orchestrating; the platform delegates whole
> operations and observes the module's events. Tasks T087, T091,
> T093, T094, T096, T097, T098 from earlier drafts of tasks.md are
> **deleted** (struck out below for traceability) and replaced by
> the new shape.

**Goal**: Subscriber invokes the launchpad's `onboard-client`
operation, supplies inputs, and the platform delegates the full
job to the module. The chat surface streams the module's progress;
dangerous-action confirmations and human-handoff requests from the
module surface as platform prompts.

**Independent Test**: Pick `onboard-client` from the catalogue,
supply niche/city/clientName → platform calls
`IRuntimeManager.DelegateAsync(launchpad, "onboard-client", inputs, …)`
→ the fake module emits a progress event ("starting"), then a
`RequestConfirmationAsync(DeleteFile, …)` call which the platform
prompts the user about, then a `RequestHumanHandoffAsync("brief", …)`
call which opens the handoff dialog, then completes with a
`DelegationResult { Succeeded = true }` and `siteUrl` output.
Cancellation mid-delegation propagates and the module's
finalisation produces a `Succeeded = false` result.

### Tests for User Story 2

- [ ] T086 [P] [US2] Contract test `IModuleRegistry` in `tests/AgentDesktop.Contracts.Tests/ModuleRegistryContractTests.cs` (valid module loads with operations, unknown `schemaVersion` → `Incompatible`, missing dependency → `Unavailable`, concurrent `RefreshAsync` idempotent, `FindOperation` resolves declared operations and returns null for unknown ones)
- ~~T087 [P] [US2] Contract test IScenarioRegistry + IScenarioRunner~~ — **DELETED** (R18); platform-side scenario engine no longer exists. Delegation contract is covered by T123 in Phase 2 (now extended to cover the new `DelegateAsync` shape).
- [ ] T088 [P] [US2] Bundled-content test in `tests/AgentDesktop.Infrastructure.Tests/Manifests/BundledContentTests.cs` — load `modules/df-client-launchpad/module.json`, assert it loads with `LoadStatus = Loaded`, exposes the 3 declared operations (`onboard-client`, `launch-ads`, `monthly-report`) with their declared inputs, and resolves all `policies[].actionClass` entries against the baseline classification table
- [ ] T089 [P] [US2] Integration test `FileSystemModuleSource` schema-validation failure paths in `tests/AgentDesktop.Infrastructure.Tests/Manifests/FileSystemModuleSourceTests.cs`

### Implementation for User Story 2

- [ ] T090 [P] [US2] Implement `ModuleManifestValidator` (NJsonSchema, embedded `module.schema.json` — including the new `operations[]` schema) in `src/AgentDesktop.Application/Modules/ModuleManifestValidator.cs`
- ~~T091 [P] [US2] ScenarioManifestValidator~~ — **DELETED** (R18); no platform-side scenarios.
- [ ] T092 [US2] Implement `ModuleRegistry : IModuleRegistry` in `src/AgentDesktop.Application/Modules/ModuleRegistry.cs` (idempotent `RefreshAsync`, dependency resolution, `LoadStatus` transitions, O(1) `Find`/`FindOperation`/`FindSkill`)
- ~~T093 [US2] ScenarioRegistry~~ — **DELETED** (R18).
- ~~T094 [US2] ScenarioRunner~~ — **DELETED** (R18). Replaced by T094-NEW below.
- [ ] T094-NEW [US2] Implement `DelegationRunner` in `src/AgentDesktop.Application/Modules/DelegationRunner.cs` — services both the top-level agent's `delegate_operation` tool call AND the catalogue-pick UI shortcut (US4). Validates `(moduleId, operationId)` against `IModuleRegistry.FindOperation`, accepts partial inputs (per FR-009 advisory-inputs rule), calls `IRuntimeManager.DelegateAsync` with a platform-supplied `IDelegationCallbacks` adapter that routes: progress → chat (Message rows with `OriginatingDelegation`), confirmation requests → `IPolicyEngine`, human-handoff → handoff dialog, and **`AskUserAsync` → chat-question-pending state** (the next user chat message becomes the answer; new chat turns are blocked while a question is pending).
- [ ] T126 [US2] Top-level agent contract — define the agent's prompt and tool surface (`list_modules`, `delegate_operation`) in `src/AgentDesktop.Application/Runtime/TopLevelAgentContract.cs` (JSON-Schema descriptors + the platform-controlled prompt fragments). Document that no `ask_user` tool is exposed. This is the platform↔runtime contract the `ProcessRuntimeManager` (T111, Phase 7) honours when launching OpenClaw.
- [ ] T127 [US2] Pending-question router — `IConversationQuestionState` in `src/AgentDesktop.Application/Chat/IConversationQuestionState.cs` plus its `InMemoryConversationQuestionState` implementation. Tracks per-conversation "is a delegation question pending?" state. `ChatService.SendMessageAsync` consults this before calling `RunChatTurnAsync`: if a question is pending the message is delivered to the pending `AskUserAsync` task; otherwise a new chat turn runs.
- [ ] T095 [P] [US2] Implement `FileSystemModuleSource` in `src/AgentDesktop.Infrastructure/Manifests/FileSystemModuleSource.cs` (reads `<root>/<id>/module.json`; resolves `sourcePath` relative to module root; understands the launchpad's `source/` submodule layout)
- ~~T096 [P] [US2] FileSystemScenarioSource~~ — **DELETED** (R18); no platform-side scenarios.
- ~~T097 [P] [US2] SessionLogReader~~ — **DELETED** (R18); the launchpad's `SESSION-LOG.md` verification is the **module's** internal concern, not the platform's.
- ~~T098 [US2] verificationKey gating~~ — **DELETED** (R18); the module enforces its own `[<skill>: verified]` discipline internally.
- [ ] T099 [P] [US2] Implement `ModuleCatalogViewModel` and `ModuleCatalogView.axaml` in `src/AgentDesktop.Desktop/{ViewModels,Views}/` — lists installed modules and, per module, the **operations** it declares (name, description, inputs)
- [ ] T100 [P] [US2] Implement `OperationRunViewModel` and `OperationRunView.axaml` in `src/AgentDesktop.Desktop/{ViewModels,Views}/` — collects the operation's declared inputs, kicks off `DelegationRunner.RunAsync`, renders the progress event stream, reuses `ConfirmationDialog` for confirmation requests. Includes per-view a11y assertion (focus order, screen-reader labels on progress text, contrast on event log) in the corresponding headless test under `tests/AgentDesktop.Desktop.Tests/Views/OperationRunViewTests.cs` (constitution Principle III)
- [ ] T101 [US2] Implement human-handoff prompt in `src/AgentDesktop.Desktop/Views/HumanHandoffDialog.axaml` (paused-with-instructions UI driven by the module's `RequestHumanHandoffAsync` calls). Includes per-view a11y assertion (focus trapped while open, instructions read by screen reader, "mark done" button keyboard-reachable) in `tests/AgentDesktop.Desktop.Tests/Views/HumanHandoffDialogTests.cs` (constitution Principle III)
- [ ] T102 [US2] Register `ModuleRegistry`, `ModuleManifestValidator`, `DelegationRunner`, `FileSystemModuleSource` (configured to read `modules/`) in `Program.cs`. (No scenario services to register.)
- [ ] T103 [US2] End-to-end test in `tests/AgentDesktop.Desktop.Tests/EndToEnd/DelegateOnboardClientTests.cs` — launches `onboard-client` via UI under a programmed `FakeRuntimeManager`, asserts the platform calls `DelegateAsync(launchpad, "onboard-client", inputs, …)`, asserts the fake module's progress events render in chat, asserts a programmed `RequestConfirmationAsync(DeleteFile, "/tmp/foo")` opens the confirmation dialog and the user's decline propagates back to the module, asserts `RequestHumanHandoffAsync("brief", …)` opens the handoff dialog and the user's "done" propagates back, asserts cancellation mid-delegation propagates and produces `Succeeded = false`
- [ ] T125 [US2] Headless test `OnboardClient human-handoff pauses` in `tests/AgentDesktop.Desktop.Tests/EndToEnd/OnboardClientHumanHandoffTests.cs` — covers SC-005: programs the fake module to emit `RequestHumanHandoffAsync` for `brief`, `offer`, and `ads`, asserts the dialog opens with the module-supplied instructions, asserts the module pauses until "done" is clicked

**Checkpoint**: US2 fully functional. The `df-client-launchpad`
module loads, declares 3 operations, and the platform delegates
each operation end-to-end against the fake runtime. SC-005
(`onboard-client` halts on human-handoff requests from the module)
measurable.

---

## Phase 6: User Story 4 — Browse the module catalogue and pick an operation (Priority: P3)

**Goal**: Browse the module catalogue and launch one of the
module's declared operations directly. (Advanced direct **skill**
invocation — `IRuntimeManager.InvokeSkillAsync` — is included as a
power-user surface but is not the primary US4 path.)

**Independent Test**: Open module catalogue → see
`df-client-launchpad` listed with version, description, and 3
declared operations → pick `monthly-report` → supply `clientPath`
→ delegation begins, progress flows into chat.

### Tests for User Story 4

- [ ] T104 [P] [US4] Contract test for direct **skill** invocation (US4 advanced power-user path) in `tests/AgentDesktop.Contracts.Tests/SkillInvocationContractTests.cs` — happy path through `IPolicyEngine`, missing-required-input refusal, dangerous skill prompts confirmation. (Operations use the delegation flow already covered by Phase 5.)
- [ ] T105 [P] [US4] Headless test for `ModuleCatalogView` in `tests/AgentDesktop.Desktop.Tests/Views/ModuleCatalogViewTests.cs` (renders module rows with declared **operations**; advanced view exposes internal skills; keyboard navigation; screen-reader labels; focus order matches visual order; contrast on classification badges) — full a11y assertion per constitution Principle III

### Implementation for User Story 4

- [ ] T106 [P] [US4] Implement `SkillInvocationService` in `src/AgentDesktop.Application/Modules/SkillInvocationService.cs` (validates inputs, evaluates via `IPolicyEngine`, dispatches via `IRuntimeManager.InvokeSkillAsync`, persists output as a `Message` with `OriginatingSkill` set). For advanced direct skill invocation only — operations use `DelegationRunner` from Phase 5.
- ~~T107 [P] [US4] ModuleCatalogViewModel + View~~ — already created in Phase 5 (T099). Phase 6 extends the view with an "advanced" pane exposing internal skills.
- [ ] T108 [P] [US4] Implement `SkillRunViewModel` and `SkillRunView.axaml` in `src/AgentDesktop.Desktop/{ViewModels,Views}/` — input form generated from `SkillParameter[]`, refuses to run until required fields are filled. Visible only in the advanced pane of the module catalogue. Includes per-view a11y assertion (every input has an associated label, error messages announced to screen reader, submit button keyboard-reachable) in `tests/AgentDesktop.Desktop.Tests/Views/SkillRunViewTests.cs` (constitution Principle III)
- [ ] T109 [US4] Register `SkillInvocationService` in `Program.cs`
- [ ] T110 [US4] End-to-end test in `tests/AgentDesktop.Desktop.Tests/EndToEnd/DirectSkillInvocationTests.cs` — pick the launchpad's internal `pre-research` skill from the advanced catalogue pane, supply input, assert output rendered, no confirmation prompt (skill is `safe`)

**Checkpoint**: All four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Constitution gates (perf, a11y, security), packaging,
docs.

> **FR-017 caveat**: "All agent-initiated activity that touches
> the user's machine MUST run inside the sandboxed runtime layer;
> activity that bypasses the sandbox MUST be refused" is testable
> only against the **real** `ProcessRuntimeManager` (T111). MVP
> demos against `FakeRuntimeManager` (Phases 3–6) cannot exercise
> sandbox-escape refusal. SC-007 explicitly accepts this trade-off
> in exchange for a green automated suite without OpenClaw /
> NemoClaw installed.

- [ ] T111 [P] Implement `ProcessRuntimeManager : IRuntimeManager` in `src/AgentDesktop.Infrastructure/Runtime/ProcessRuntimeManager.cs` (real OpenClaw + NemoClaw orchestration over JSON-RPC stdio) — gated behind `RequiresLiveRuntime` for CI
- [ ] T112 [P] Implement `McpClient` and `McpServerLauncher` in `src/AgentDesktop.Infrastructure/Mcp/` (routes MCP processes through the sandbox)
- [ ] T113 [P] BenchmarkDotNet baselines in `tests/AgentDesktop.Bench/` — `RuntimeStartupBenchmarks.cs`, `ChatRoundTripBenchmarks.cs`, `ScenarioStepLatencyBenchmarks.cs`, **`HistoryRestoreLatencyBenchmarks.cs`** (covers SC-006 — measures time from app launch to first conversation rendered, asserts ≤2 s on the per-leg reference machine); commit baseline results under `tests/AgentDesktop.Bench/baseline/`
- [ ] T114 [P] CI: enforce coverage thresholds in `.github/workflows/ci.yml` — ≥90% Domain + Application, ≥80% Infrastructure + Desktop, **100% branch coverage** for every module that handles user data, IPC boundaries, or filesystem mutations (constitution Principle II): `DefaultPolicyEngine`, `SqliteChatRepository`, `SqliteAuditLog`, `ProcessRuntimeManager`, `FakeRuntimeManager` (programmable surface), `FileSystemModuleSource`, `FileSystemScenarioSource`, `SessionLogReader`, `EncryptedFileSecretStore`, `WindowsDpapiSecretStore`, `MacKeychainSecretStore`, `LinuxSecretStore`
- [ ] T115 [P] CI: a11y gate using Avalonia.Headless assertions across every shipped view (focus order, label association, contrast, target size)
- [ ] T116 [P] CI: dependency vulnerability scan (`dotnet list package --vulnerable`) and secret scan (gitleaks); fail on high-severity findings
- [ ] T117 [P] CI: perf-regression gate fails on >10% regression vs `tests/AgentDesktop.Bench/baseline/`
- [ ] T118 [P] Packaging — macOS notarised bundle, Windows signed installer, Linux AppImage + .deb, all produced from `dotnet publish` profiles under `build/`
- [ ] T119 [P] Documentation: update `CLAUDE.md` agent-context block, add architecture diagram in `docs/architecture.md`, fold final decisions back into `research.md` if any drifted
- [ ] T120 [P] Run `quickstart.md` verbatim on a clean macOS, Windows, and Linux machine; record outcomes; tighten any step that confused a fresh contributor
- [ ] T121 Final perf check vs constitution budgets: cold start ≤2.0 s, warm interaction p95 ≤100 ms, idle CPU ≤1%, resident memory ≤300 MB after 1 hr; capture results in `tests/AgentDesktop.Bench/baseline/v1.0.0.md`
- [ ] T122 Tag release candidate `v0.1.0-rc1`, capture release notes summarising US1–US4 + bundled `df-client-launchpad`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: requires Setup; **blocks** every user-story phase.
- **US1 (Phase 3)**: requires Foundational; independently testable.
- **US3 (Phase 4)**: requires Foundational; independently testable. May start in parallel with US1; integrates cleanly because US1 wires the policy engine in `ChatService` (T083) only after both phases land.
- **US2 (Phase 5)**: requires Foundational + US3 (because every scenario step routes through the policy engine).
- **US4 (Phase 6)**: requires Foundational + US3 + US1 (chat surface to render output) + US2 (`IModuleRegistry`).
- **Polish (Phase 7)**: requires whichever stories are in scope for the release; T111 (real `ProcessRuntimeManager`) intentionally lives here so US1–US4 ship independently of OpenClaw/NemoClaw availability (constitution + SC-007).

### Within each phase

- Tests written **before** implementation (constitution Principle II).
- Domain types before Application services that use them.
- Application interfaces before Infrastructure adapters.
- Adapters before composition-root wiring.
- Composition wiring before end-to-end tests.

### Parallel opportunities

- All `[P]` tasks within a phase target different files and can run concurrently.
- Once Foundational is green, US1 and US3 can be staffed in parallel (different teams, different files).
- US2 and US4 can be parallelised once US3 lands (both consume the policy engine).
- Polish tasks marked `[P]` (benches, packaging, docs, CI gates) parallelise freely.

---

## Parallel Example: User Story 1

```bash
# Tests for US1 (write first, expect to fail):
Task: "T058 Contract test IChatService in tests/AgentDesktop.Contracts.Tests/ChatServiceContractTests.cs"
Task: "T059 Integration test SqliteChatRepository in tests/AgentDesktop.Infrastructure.Tests/Persistence/SqliteChatRepositoryTests.cs"
Task: "T060 Integration test HttpSubscriptionGate in tests/AgentDesktop.Infrastructure.Tests/Subscription/HttpSubscriptionGateTests.cs"
Task: "T061 Headless UI test SignInView in tests/AgentDesktop.Desktop.Tests/Views/SignInViewTests.cs"

# Adapters/views for US1 (different files, different layers):
Task: "T062 Implement SqliteChatRepository in src/AgentDesktop.Infrastructure/Persistence/Sqlite/SqliteChatRepository.cs"
Task: "T063 Implement SubscriptionGate in src/AgentDesktop.Application/Subscription/SubscriptionGate.cs"
Task: "T064 Implement HttpSubscriptionGate in src/AgentDesktop.Infrastructure/Subscription/HttpSubscriptionGate.cs"
Task: "T067 Implement SignInViewModel in src/AgentDesktop.Desktop/ViewModels/SignInViewModel.cs"
Task: "T068 Implement SignInView.axaml in src/AgentDesktop.Desktop/Views/SignInView.axaml"
Task: "T069 Implement ChatViewModel in src/AgentDesktop.Desktop/ViewModels/ChatViewModel.cs"
Task: "T070 Implement ChatView.axaml in src/AgentDesktop.Desktop/Views/ChatView.axaml"
Task: "T071 Implement ConversationListViewModel and View"
```

---

## Implementation Strategy

### MVP First (US1 + US3 — both P1)

1. **Phase 1 Setup** — solution, projects, CI, tooling.
2. **Phase 2 Foundational** — domain, interfaces, fakes, schema.
3. **Phase 3 US1** — sign-in + chat + history (the "smallest end-to-end slice").
4. **Phase 4 US3** — confirmation gate (required before any module can run safely).
5. **STOP and validate**: SC-001, SC-002, SC-003, SC-004, SC-006 measurable; demo-able to stakeholders.

### Incremental Delivery

1. MVP slice (above) → demo.
2. **Phase 5 US2** — bundled `df-client-launchpad` + three scenarios → demo `onboard-client` against fake runtime.
3. **Phase 6 US4** — module catalogue + direct skill run → demo.
4. **Phase 7 Polish** — real `ProcessRuntimeManager`, packaging, perf gates → release `v0.1.0`.

### Parallel Team Strategy

- One developer drives Phase 1 + Phase 2 to a green test suite.
- Then split:
  - Dev A: US1 (Phase 3).
  - Dev B: US3 (Phase 4).
- Once US3 lands:
  - Dev A: US2 (Phase 5).
  - Dev B: US4 (Phase 6).
- Reconverge for Phase 7 polish.

---

## Notes

- Every `[P]` task targets a distinct file; serial tasks share a file or depend on a prior task's output.
- Constitution gates apply to every PR — coverage, perf, a11y, security. T114–T117 wire those gates into CI; until they're in place, run them locally before opening PRs.
- The `df-client-launchpad` module ships as a Git submodule under `modules/df-client-launchpad/source/`. The first task that touches it on a fresh checkout is the bundled-content test (T088); developers must run `git submodule update --init --recursive` before that test will pass.
- T111 (`ProcessRuntimeManager`) is in Polish on purpose: SC-007 requires the full automated suite to pass *without* the real runtime, so US1–US4 must be implementable against `FakeRuntimeManager` only.
- Stop at any **Checkpoint** to validate the corresponding story independently.
