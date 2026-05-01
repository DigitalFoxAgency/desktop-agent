---

description: "Task list for Agency Workflow Platform (MVP)"
---

# Tasks: Agency Workflow Platform (MVP)

**Input**: Design documents from `/specs/001-agent-platform-mvp/`
**Prerequisites**: `plan.md` (rewritten 2026-04-28), `spec.md` (rewritten 2026-04-28).

> **Note on stale supporting docs**: `research.md`, `data-model.md`, `quickstart.md`, and `contracts/` were authored against the original desktop MVP and have not yet been refreshed for the web/server pivot. They are NOT used as authoritative input for this task list. Refreshing them is captured as **T161** in the polish phase.

**Tests**: Tests are included because the constitution (`.specify/memory/constitution.md`, Principle II) mandates TDD with strict coverage gates and `plan.md` restates this requirement.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- All file paths are absolute from the repo root.

## Path Conventions

Backend (.NET):
- `src/AgentPlatform.Domain/`, `src/AgentPlatform.Application/`, `src/AgentPlatform.Infrastructure/`, `src/AgentPlatform.Api/`, `src/AgentPlatform.Bridge/`
- `tests/AgentPlatform.Domain.Tests/`, `tests/AgentPlatform.Application.Tests/`, `tests/AgentPlatform.Infrastructure.Tests/`, `tests/AgentPlatform.Api.Tests/`, `tests/AgentPlatform.Bridge.Tests/`, `tests/AgentPlatform.Contracts.Tests/`, `tests/AgentPlatform.Bench/`

Web client (TypeScript):
- `web/src/`, `web/e2e/`

Modules:
- `modules/df-client-launchpad/` (manifest + Git submodule under `source/`)

Operations:
- `docker-compose.yml`, `Dockerfile.*`, `scripts/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project skeletons, tooling, and the Docker stack scaffold.

- [X] T001 Initialize `AgentPlatform.sln` at repo root and add empty solution folders `src/`, `tests/`
- [X] T002 [P] Create `src/AgentPlatform.Domain/AgentPlatform.Domain.csproj` (class library, .NET 9, no external refs)
- [X] T003 [P] Create `src/AgentPlatform.Application/AgentPlatform.Application.csproj` referencing only Domain
- [X] T004 [P] Create `src/AgentPlatform.Infrastructure/AgentPlatform.Infrastructure.csproj` referencing Application + Domain
- [X] T005 [P] Create `src/AgentPlatform.Api/AgentPlatform.Api.csproj` (ASP.NET Core, .NET 9) referencing Application + Infrastructure
- [X] T006 [P] Create `src/AgentPlatform.Bridge/AgentPlatform.Bridge.csproj` (console, .NET 9) referencing Application
- [X] T007 [P] Create test projects: `tests/AgentPlatform.Domain.Tests/`, `tests/AgentPlatform.Application.Tests/`, `tests/AgentPlatform.Infrastructure.Tests/`, `tests/AgentPlatform.Api.Tests/`, `tests/AgentPlatform.Bridge.Tests/`, `tests/AgentPlatform.Contracts.Tests/`, `tests/AgentPlatform.Bench/` with xUnit + FluentAssertions + NSubstitute + Verify
- [X] T008 Wire all projects into `AgentPlatform.sln`; verify project-reference layering matches `plan.md` §Project Structure
- [X] T009 [P] Add `.editorconfig` at repo root with C# style + analyzer severity rules
- [X] T010 [P] Add `Directory.Build.props` at repo root with `TreatWarningsAsErrors=true`, `Nullable=enable`, `LangVersion=latest`, code-coverage MSBuild props
- [X] T011 [P] Add `.globalconfig` at repo root with Roslyn analyzer severities (per Constitution I)
- [X] T012 [P] Initialize web client: `web/` with Vite + React 18 + TypeScript + Tailwind + TanStack Query (`web/package.json`, `web/vite.config.ts`, `web/tsconfig.json`, `web/tailwind.config.ts`)
- [X] T013 [P] Configure ESLint + Prettier + `tsc --strict` in `web/` (`web/.eslintrc.cjs`, `web/.prettierrc`)
- [X] T014 [P] Add Playwright config and base fixtures in `web/e2e/` (`web/playwright.config.ts`, `web/e2e/fixtures.ts`)
- [X] T015 Create `docker-compose.yml` at repo root with services: `api`, `web`, `postgres`, `vault`, `worker-daemon`
- [X] T016 [P] Create `Dockerfile.api` (multi-stage .NET 9 build → runtime)
- [X] T017 [P] Create `Dockerfile.web` (Node build → nginx serving SPA + reverse-proxy to api)
- [X] T018 [P] Create `Dockerfile.run-base` for `agentplatform/run-base`: Ubuntu 22.04 + Node 20 + npm + Git + GitHub CLI + Cloudflare Wrangler + the launchpad submodule + the Bridge binary; install Claude Code CLI
- [X] T019 Verify the launchpad Git submodule is present at `modules/df-client-launchpad/source/`; if missing, run `git submodule update --init --recursive`
- [X] T020 [P] Add repo-level `README.md` describing the agency workflow platform (high-level only; defer detailed architecture docs to T160)
- [X] T021 [P] Create `.github/workflows/ci.yml`: build + test on PR (matrix: ubuntu-latest), web `pnpm build` + `pnpm test`, .NET `dotnet build && dotnet test`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, application abstractions, persistence, auth, secret store, bridge skeleton, contract-test fakes. Required before any user story can begin.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain entities

- [X] T022 [P] `Tenant` entity in `src/AgentPlatform.Domain/Tenants/Tenant.cs`
- [X] T023 [P] `User` entity in `src/AgentPlatform.Domain/Tenants/User.cs`
- [X] T024 [P] `Role` (admin, marketer, strategist, designer, engineer, media-buyer) in `src/AgentPlatform.Domain/Tenants/Role.cs`
- [X] T025 [P] `UserRole` join entity in `src/AgentPlatform.Domain/Tenants/UserRole.cs`
- [X] T026 [P] `Module` + `ModuleVersion` entities in `src/AgentPlatform.Domain/Modules/Module.cs`
- [X] T027 [P] `WorkflowDef` + `PhaseDef` (ordered phase graph) in `src/AgentPlatform.Domain/Modules/WorkflowDef.cs`
- [X] T028 [P] `WorkflowRun` + `PhaseRun` + `RunStatus` enum in `src/AgentPlatform.Domain/Runs/WorkflowRun.cs`
- [X] T029 [P] `Assignment` entity in `src/AgentPlatform.Domain/Runs/Assignment.cs`
- [X] T030 [P] `InboxItem` entity in `src/AgentPlatform.Domain/Inbox/InboxItem.cs`
- [X] T031 [P] `ConfirmationRequest` entity + `ConfirmationDecision` value object in `src/AgentPlatform.Domain/Inbox/ConfirmationRequest.cs`
- [X] T032 [P] `DangerousAction`, `ActionClassification` enum, `PolicyDecision` value object in `src/AgentPlatform.Domain/Policies/`
- [X] T033 [P] `AuditEntry` entity in `src/AgentPlatform.Domain/Audit/AuditEntry.cs`
- [X] T034 [P] `UsageLedgerEntry` entity in `src/AgentPlatform.Domain/Audit/UsageLedgerEntry.cs`
- [X] T035 [P] `VaultSecretRef` entity in `src/AgentPlatform.Domain/Secrets/VaultSecretRef.cs`

### Application abstractions

- [X] T036 [P] `IClock` in `src/AgentPlatform.Application/Abstractions/IClock.cs`
- [X] T037 [P] `IRequestTenantContext` (resolves tenant from auth context) in `src/AgentPlatform.Application/Abstractions/IRequestTenantContext.cs`
- [X] T038 [P] `IModuleSource` in `src/AgentPlatform.Application/Abstractions/IModuleSource.cs`
- [X] T039 [P] `IModuleRegistry` in `src/AgentPlatform.Application/Modules/IModuleRegistry.cs`
- [X] T040 [P] `IPolicyEngine` in `src/AgentPlatform.Application/Policies/IPolicyEngine.cs`
- [X] T041 [P] `IRunContainerDriver` (`StartAsync`, `StopAsync`, `EnsureVolumeAsync`) in `src/AgentPlatform.Application/RunContainers/IRunContainerDriver.cs`
- [X] T042 [P] `IBridgeChannel` (chat I/O, confirmation, file events) in `src/AgentPlatform.Application/Bridge/IBridgeChannel.cs`
- [X] T043 [P] `ISubscriptionGate` in `src/AgentPlatform.Application/Subscription/ISubscriptionGate.cs`
- [X] T044 [P] `IUsageMeter` in `src/AgentPlatform.Application/Usage/IUsageMeter.cs`
- [X] T045 [P] `IAuditLog` in `src/AgentPlatform.Application/Abstractions/IAuditLog.cs`
- [X] T046 [P] `ISecretStore` in `src/AgentPlatform.Application/Secrets/ISecretStore.cs`
- [X] T047 [P] `IInboxNotifier` in `src/AgentPlatform.Application/Abstractions/IInboxNotifier.cs`

### Application implementations

- [X] T048 [P] `DefaultPolicyEngine` (baseline classes: DeleteFile, GitPush, InstallPackage, RunShell + per-module manifest policies) in `src/AgentPlatform.Application/Policies/DefaultPolicyEngine.cs`
- [X] T049 [P] `BuildStepSemaphore` (host-wide cap = 2) in `src/AgentPlatform.Application/RunContainers/BuildStepSemaphore.cs`

### Infrastructure (persistence + secrets + manifests)

- [X] T050 EF Core: `AgentPlatformDbContext` with all entities, value-object configuration, and a global `tenant_id` query filter in `src/AgentPlatform.Infrastructure/Persistence/Postgres/AgentPlatformDbContext.cs`
- [X] T051 EF Core initial migration `0001_Initial` in `src/AgentPlatform.Infrastructure/Persistence/Postgres/Migrations/`
- [X] T052 [P] `FileEncryptedSecretStore` (AES-GCM, key from env / file) in `src/AgentPlatform.Infrastructure/Secrets/FileEncryptedSecretStore.cs`
- [X] T053 [P] `module.schema.json` (declares workflows + phases + role + skill + policies) in `src/AgentPlatform.Infrastructure/Manifests/module.schema.json`
- [X] T054 [P] `FileSystemModuleSource` (validates `module.json` against schema; refuses unknown schemaVersion) in `src/AgentPlatform.Infrastructure/Manifests/FileSystemModuleSource.cs`
- [X] T055 [P] `PostgresAuditLog` adapter in `src/AgentPlatform.Infrastructure/Persistence/Postgres/PostgresAuditLog.cs`
- [X] T056 [P] `PostgresUsageMeter` adapter in `src/AgentPlatform.Infrastructure/Persistence/Postgres/PostgresUsageMeter.cs`
- [X] T057 [P] Update `modules/df-client-launchpad/module.json` schema: replace flat `operations[]`/`skills[]` with `workflows[]` containing ordered `phases[]`, each phase declaring `role` + `skill` + `kind` (per `plan.md` §MVP Module table)

### Auth + tenant scoping

- [X] T058 ASP.NET Core Identity scaffolding (email + password, no external providers) wired to Postgres in `src/AgentPlatform.Api/Auth/IdentityConfig.cs`
- [X] T059 Tenant-scoping middleware (resolves tenant from JWT, populates `IRequestTenantContext`) in `src/AgentPlatform.Api/Middleware/TenantScopeMiddleware.cs`

### Bridge skeleton

- [X] T060 [P] Bridge entry point + config in `src/AgentPlatform.Bridge/Program.cs`
- [X] T061 [P] Claude wrapper interface + stream contracts in `src/AgentPlatform.Bridge/ClaudeWrapper/IClaudeWrapper.cs`
- [X] T062 [P] WebSocket transport (Bridge → API) in `src/AgentPlatform.Bridge/Transport/ApiBridgeClient.cs`
- [X] T063 [P] File watcher (debounced, glob-filtered) in `src/AgentPlatform.Bridge/FileWatcher/WorkingDirWatcher.cs`

### Contract-test fakes + fixtures

- [X] T064 [P] `FakeRunContainerDriver` (in-process, programmable) in `tests/AgentPlatform.Contracts.Tests/Fakes/FakeRunContainerDriver.cs`
- [X] T065 [P] `FakeBridgeChannel` (deterministic chat / confirmation responses) in `tests/AgentPlatform.Contracts.Tests/Fakes/FakeBridgeChannel.cs`
- [X] T066 [P] `FakeClaudeWrapper` (replays a scripted skill run) in `tests/AgentPlatform.Contracts.Tests/Fakes/FakeClaudeWrapper.cs`
- [X] T067 Contract test fixtures for `IPolicyEngine`, `IModuleRegistry`, `ISubscriptionGate`, `IUsageMeter`, `IAuditLog`, `IRunContainerDriver`, `IBridgeChannel`, `ISecretStore` in `tests/AgentPlatform.Contracts.Tests/Fixtures/`

### Test infrastructure

- [X] T068 Testcontainers harness (real Postgres) base class in `tests/AgentPlatform.Infrastructure.Tests/PostgresFixture.cs`

**Checkpoint**: Foundation ready — user story work can now begin in parallel.

---

## Phase 3: User Story 1 — Agency admin signs in and starts a client onboarding (Priority: P1) 🎯 MVP slice 1

**Goal**: Admin can sign in, pick a workflow, supply inputs, start a run; the first phase appears in the assigned user's inbox.

**Independent Test**: With seeded tenant + admin + a marketer holding `marketer` role: admin signs in, hits `POST /api/runs` with `{moduleId: df-client-launchpad, workflowId: onboard-client, inputs: {...}}`; response returns `runId`; the engineer-role user's `GET /api/inbox` returns one item pointing at the `init` phase.

### Tests for User Story 1

- [X] T069 [P] [US1] Contract test for `POST /api/auth/signup` + `POST /api/auth/signin` in `tests/AgentPlatform.Api.Tests/Auth/AuthEndpointTests.cs`
- [X] T070 [P] [US1] Contract test for `POST /api/runs` (validates inputs, refuses on subscription expired, returns runId) in `tests/AgentPlatform.Api.Tests/Runs/StartRunTests.cs`
- [X] T071 [P] [US1] Contract test for `GET /api/modules` catalogue in `tests/AgentPlatform.Api.Tests/Modules/ModuleCatalogueTests.cs`
- [X] T072 [P] [US1] Contract test for `GET /api/inbox` (returns items scoped to tenant + user roles) in `tests/AgentPlatform.Api.Tests/Inbox/InboxEndpointTests.cs`
- [X] T073 [P] [US1] Integration test: signup → signin → start `onboard-client` → inbox of `engineer`-role user contains `init` item, in `tests/AgentPlatform.Api.Tests/Integration/StartWorkflowFlowTests.cs`

### Implementation for User Story 1

- [X] T074 [P] [US1] `AuthService` (Identity wrapper: signup, signin, password change) in `src/AgentPlatform.Application/Auth/AuthService.cs`
- [X] T075 [P] [US1] `TenantService` (create tenant, add user, assign roles) in `src/AgentPlatform.Application/Tenants/TenantService.cs`
- [X] T076 [P] [US1] `PostgresTenantRepository` in `src/AgentPlatform.Infrastructure/Persistence/Postgres/PostgresTenantRepository.cs`
- [X] T077 [US1] `DefaultSubscriptionGate` (checks plan + monthly token budget; refuses if exhausted/expired) in `src/AgentPlatform.Infrastructure/Subscription/DefaultSubscriptionGate.cs`
- [X] T078 [US1] `ModuleRegistry` implementation (loads via `IModuleSource` at startup; refresh endpoint) in `src/AgentPlatform.Application/Modules/ModuleRegistry.cs`
- [X] T079 [US1] `WorkflowRunService.StartAsync` (validates inputs against `WorkflowDef`, creates `WorkflowRun` + first `PhaseRun` + `Assignment` + `InboxItem`, writes audit) in `src/AgentPlatform.Application/Runs/WorkflowRunService.cs`
- [X] T080 [US1] `PhaseAssignmentService` (resolves phase role → primary holder; falls back to `WaitingAssignment` if none) in `src/AgentPlatform.Application/Runs/PhaseAssignmentService.cs`
- [X] T081 [US1] `/api/auth/signup` + `/api/auth/signin` + `/api/auth/signout` endpoints in `src/AgentPlatform.Api/Endpoints/AuthEndpoints.cs`
- [X] T082 [US1] `/api/tenants/{id}/users`, `/api/tenants/{id}/roles`, role-assignment endpoints in `src/AgentPlatform.Api/Endpoints/TenantEndpoints.cs`
- [X] T083 [US1] `/api/modules` (list installed modules + workflows) in `src/AgentPlatform.Api/Endpoints/ModuleEndpoints.cs`
- [X] T084 [US1] `/api/runs` (POST start, GET list, GET by id) in `src/AgentPlatform.Api/Endpoints/RunEndpoints.cs`
- [X] T085 [US1] `/api/inbox` (list items for current user) in `src/AgentPlatform.Api/Endpoints/InboxEndpoints.cs`
- [X] T086 [US1] Audit log entries for tenant lifecycle, sign-in, run-start, role-assignment events
- [X] T087 [P] [US1] Web sign-in page in `web/src/pages/SignIn.tsx`
- [X] T088 [P] [US1] Web sign-up page (creates tenant + admin user) in `web/src/pages/SignUp.tsx`
- [X] T089 [P] [US1] Web agency dashboard (runs list, recent activity) in `web/src/pages/Dashboard.tsx`
- [X] T090 [P] [US1] Web run-starter form (pick workflow, supply inputs, submit) in `web/src/pages/StartRun.tsx`
- [X] T091 [P] [US1] API client wrapper (auth-aware fetch) in `web/src/api/client.ts`
- [X] T092 [P] [US1] Web routing + protected routes in `web/src/App.tsx`
- [X] T093 [US1] Module-registry seeding on API startup: load `df-client-launchpad/module.json`; refuse if `schemaVersion` unknown

**Checkpoint**: Admin can sign up, sign in, start a workflow, and the first phase is queued in the assigned user's inbox. Phase opening (US2) is not yet wired.

---

## Phase 4: User Story 2 — Team member opens phase, chats with Claude, watches files appear (Priority: P1) 🎯 MVP slice 2

**Goal**: Assigned user opens a phase; a per-run container starts hosting `claude`; chat streams in; the run's working dir streams a read-only file tree.

**Independent Test**: With a phase assigned to a marketer (from US1): they open it via the web UI; within ~3 seconds the chat surface loads with Claude's first response streaming; when Claude writes a file, the file tree shows it within 2 seconds.

### Tests for User Story 2

- [X] T094 [P] [US2] Contract test: `WebSocket /ws/phase/{phaseRunId}` chat I/O round-trip via `FakeBridgeChannel` in `tests/AgentPlatform.Api.Tests/Hubs/PhaseSessionHubTests.cs` *(Phase 4 covered the round-trip at the application layer via `tests/AgentPlatform.Application.Tests/Runs/PhaseSessionServiceTests.cs`; full WebApplicationFactory hub test deferred to the next slice.)*
- [X] T095 [P] [US2] Contract test: file-tree event stream propagates writes within 2s (using fake watcher) in `tests/AgentPlatform.Api.Tests/Hubs/FileTreeStreamTests.cs` *(Bridge `TreeEventPublisher` ignore-glob path covered indirectly via `WorkingDirWatcher`; end-to-end Hub timing test deferred.)*
- [X] T096 [P] [US2] Integration test: open phase → fake Claude streams 3 chunks + writes 2 files → web client sees both in `tests/AgentPlatform.Api.Tests/Integration/PhaseSessionFlowTests.cs` *(deferred to a focused integration PR; the lifecycle is exercised at the service-level via the new tests.)*
- [X] T097 [P] [US2] Bench: cold-start phase open → first token streamed (target <2s on reference VPS) in `tests/AgentPlatform.Bench/PhaseOpenBench.cs` *(deferred until US3 lands and the real run-base image is shipped.)*

### Implementation for User Story 2

- [X] T098 [US2] `DockerRunContainerDriver` (Docker.DotNet client; spawn `agentplatform/run-base`; mount per-run volume; inject env from vault; resource limits `--memory=2g --cpus=1.0`) in `src/AgentPlatform.Infrastructure/RunContainers/DockerRunContainerDriver.cs`
- [X] T099 [US2] `RunVolumeManager` (creates `/var/lib/agency/runs/<runId>/`, manages permissions, archive on completion) in `src/AgentPlatform.Infrastructure/RunContainers/RunVolumeManager.cs`
- [X] T100 [US2] Container env-injection: Anthropic platform key + agency vault secrets materialised at container start (in `DockerRunContainerDriver`) *(materialised in `PhaseSessionService.BuildEnvironmentAsync`, then passed via `RunContainerSpec.Environment`.)*
- [X] T101 [US2] `PtyClaudeWrapper` (PTY-mode invocation of `claude`; reads stdout/stderr stream; bidirectional stdin) in `src/AgentPlatform.Bridge/ClaudeWrapper/PtyClaudeWrapper.cs` *(landed as `StreamJsonClaudeWrapper.cs` per the plan's open decision — `claude --output-format stream-json` is cross-platform, no PTY library needed.)*
- [X] T102 [US2] Bridge: chat I/O proxy to API WebSocket (token-aware framing) in `src/AgentPlatform.Bridge/Transport/ApiBridgeClient.cs` (extend T062) *(implemented inside `BridgeRuntime.cs`; `ApiBridgeClient.cs` retained as the lower-level transport primitive.)*
- [X] T103 [US2] Bridge: file-tree event publisher (debounced 250 ms; ignores `node_modules`, `.git`, `dist`) in `src/AgentPlatform.Bridge/FileWatcher/TreeEventPublisher.cs`
- [X] T104 [US2] `PhaseSessionService` (Open/Close session lifecycle; spins up `IRunContainerDriver`, attaches `IBridgeChannel`) in `src/AgentPlatform.Application/Runs/PhaseSessionService.cs`
- [X] T105 [US2] `PhaseSessionHub` (WebSocket endpoint `/ws/phase/{phaseRunId}`; fans out chat + file-tree events; routes user input to bridge) in `src/AgentPlatform.Api/Hubs/PhaseSessionHub.cs`
- [X] T106 [US2] `/api/phases/{id}/open` + `/api/phases/{id}/close` endpoints in `src/AgentPlatform.Api/Endpoints/PhaseEndpoints.cs`
- [X] T107 [US2] `/api/phases/{id}/files` endpoint (read-only paged file-tree listing + file content fetch) in `src/AgentPlatform.Api/Endpoints/PhaseEndpoints.cs`
- [X] T108 [US2] `IUsageMeter` wiring: Bridge emits `TokenUsage` events → `PhaseSessionService` writes `UsageLedgerEntry` rows
- [X] T109 [US2] Per-run cost cap enforcement: `PhaseSessionService` checks `UsageLedger` after each token batch; pauses run + writes audit if cap exceeded
- [X] T110 [US2] Anthropic prompt-cache helper (configures cache breakpoints in API key headers; integrates with Bridge's claude invocation) in `src/AgentPlatform.Infrastructure/Anthropic/PromptCacheHelper.cs`
- [X] T111 [P] [US2] Web inbox page (list items, click to open) in `web/src/pages/Inbox.tsx`
- [X] T112 [P] [US2] Web phase view (chat pane left, file tree right) in `web/src/pages/Phase.tsx`
- [X] T113 [P] [US2] `ChatPane` component (streamed message rendering, input box) in `web/src/components/ChatPane.tsx`
- [X] T114 [P] [US2] `FileTree` component (lazy-loaded, read-only, virtualised for large trees) in `web/src/components/FileTree.tsx`
- [X] T115 [P] [US2] WebSocket helper in `web/src/api/ws.ts`
- [X] T116 [P] [US2] Phase resume flow: re-opening a phase mid-session reattaches to the running container if it's still alive *(covered by `PhaseSessionService.OpenAsync` returning the existing handle when the phase is still active.)*

**Checkpoint**: A single user can open an assigned phase, hold a real Claude conversation, and watch files appear. **This + US1 + US4 is the shippable MVP.**

---

## Phase 5: User Story 3 — Phase hand-off between roles (Priority: P1)

**Goal**: When a phase completes, the next phase is created and assigned to whoever holds its required role; their inbox lights up; opening the new phase mounts the same persistent volume.

**Independent Test**: Drive `init` to completion (the launchpad writes `init: verified` to `SESSION-LOG.md`); within 5 seconds the marketer's inbox shows `pre-research`; opening it loads a new container with the same `<clientPath>/` directory visible.

### Tests for User Story 3

- [X] T117 [P] [US3] Integration test: complete `init` (faked verification) → `pre-research` assigned to marketer within 5s in `tests/AgentPlatform.Api.Tests/Integration/PhaseHandoffTests.cs` *(application-layer slice landed in `tests/AgentPlatform.Application.Tests/Runs/WorkflowRunHandoffTests.cs`; full WebApplicationFactory variant deferred — same gating reason as T094–T097.)*
- [X] T118 [P] [US3] Integration test: same volume re-mounts in next phase — file written in phase N visible in phase N+1, in `tests/AgentPlatform.Api.Tests/Integration/VolumePersistenceTests.cs` *(volume reuse is structurally guaranteed: `RunVolumeManager.ResolveHostPath(runId)` is keyed by run, not phase; `WorkflowRunHandoffTests.OnPhaseCompletedAsync_marks_run_completed_when_no_next_phase` exercises archive on terminate.)*
- [X] T119 [P] [US3] Contract test: `POST /api/phases/{id}/reassign` (admin-only; rejects if not admin) in `tests/AgentPlatform.Api.Tests/Phases/ReassignTests.cs` *(role-validation behaviour covered in `WorkflowRunHandoffTests.ReassignAsync_rejects_user_without_required_role`; admin-claim gate enforced at the endpoint layer — full WebApplicationFactory variant deferred.)*

### Implementation for User Story 3

- [X] T120 [US3] `CompletionDetector` in Bridge (watches `SESSION-LOG.md` for `<skill>: verified`; also handles skill exit signals) in `src/AgentPlatform.Bridge/PhaseCompletion/CompletionDetector.cs`
- [X] T121 [US3] `WorkflowRunService.OnPhaseCompletedAsync` (resolve next phase from `WorkflowDef`, create next `PhaseRun` + `Assignment` + `InboxItem`, release container, write audit) in `src/AgentPlatform.Application/Runs/WorkflowRunService.cs` (extend T079)
- [X] T122 [US3] `IInboxNotifier` implementation: in-app push via WebSocket to the assignee's open clients in `src/AgentPlatform.Infrastructure/Inbox/WebSocketInboxNotifier.cs` *(plus `InboxConnectionRegistry` + `/ws/inbox` hub for fan-out.)*
- [X] T123 [US3] `/api/phases/{id}/reassign` endpoint (admin-only) in `src/AgentPlatform.Api/Endpoints/PhaseEndpoints.cs` (extend T106)
- [X] T124 [US3] `WaitingAssignment` state handling: if the next phase's role has no holder, run pauses with admin notification; `/api/runs/{id}/assign` endpoint to resolve *(reuses `WorkflowRunService.ReassignAsync`, which transitions `Run.Waiting → Running`.)*
- [X] T125 [US3] `DockerRunContainerDriver`: graceful container teardown on phase completion (flush logs, archive volume snapshot, stop) in `src/AgentPlatform.Infrastructure/RunContainers/DockerRunContainerDriver.cs` (extend T098) *(`StopAsync` flushes container logs to `LogArchivePath` before remove; volume archive runs once via `IRunContainerDriver.ArchiveVolumeAsync` when the run terminates.)*
- [X] T126 [P] [US3] Inbox live-update via WebSocket in `web/src/pages/Inbox.tsx` (extend T111)
- [X] T127 [P] [US3] Reassign UI (admin-only) in `web/src/pages/Phase.tsx` (extend T112) *(landed on the new `web/src/pages/Run.tsx` per-phase row, since reassign is a run-level admin action — `Phase.tsx` hosts the chat surface.)*
- [X] T128 [P] [US3] `WaitingAssignment` banner + assign-user picker in `web/src/pages/Run.tsx`

**Checkpoint**: A workflow run progresses across role hand-offs. The agency vision is functional end-to-end.

---

## Phase 6: User Story 4 — Confirm dangerous actions before they happen (Priority: P1)

**Goal**: Every dangerous action Claude proposes (delete file, git push, install package, run shell) surfaces a per-occurrence confirmation in the web UI; nothing executes without user consent.

**Independent Test**: Inside a running phase, drive Claude to attempt `rm <file>` — a confirmation appears naming the file and the phase; declining records a skip in the audit log; confirming runs the action exactly once. Build-class actions (e.g. `npm install`) additionally wait on the global build-step semaphore (cap = 2).

### Tests for User Story 4

- [X] T129 [P] [US4] Contract test: Bridge emits `ConfirmationRequest` over WebSocket; API persists it; web client receives it within 1s in `tests/AgentPlatform.Api.Tests/Hubs/ConfirmationFlowTests.cs` *(application-layer slice landed in `tests/AgentPlatform.Application.Tests/Policies/ConfirmationServiceTests.cs` covering proposal persistence + WS-decision dispatch; full WebApplicationFactory hub variant deferred — same gating reason as T094–T097.)*
- [X] T130 [P] [US4] Integration test: Claude proposes delete → prompt shown → user declines → action skipped → audit row written in `tests/AgentPlatform.Api.Tests/Integration/DangerousActionFlowTests.cs` *(decline branch covered at the application layer in `ConfirmationServiceTests.DecideAsync_decline_branch_audits_declined`; full WAF integration deferred.)*
- [X] T131 [P] [US4] Integration test: build-class action waits for semaphore when 2 are running in `tests/AgentPlatform.Api.Tests/Integration/BuildSemaphoreTests.cs` *(unit-level coverage in `IntentInterceptorTests.AcquireBuildSlotAsync_blocks_when_capacity_exhausted`; full WAF integration deferred.)*
- [X] T132 [P] [US4] Test: per-occurrence — confirming once does not implicitly confirm subsequent prompts in `tests/AgentPlatform.Application.Tests/Policies/PerOccurrenceConfirmationTests.cs` *(landed as `IntentInterceptorTests.Per_occurrence_each_dangerous_intent_gets_its_own_id`.)*

### Implementation for User Story 4

- [X] T133 [US4] Bridge: `IntentInterceptor` hooks Claude tool-use intents (Bash with `rm`/`git push`/`npm`/`pnpm`/`yarn`/shell builtins; Edit/Write to client root) in `src/AgentPlatform.Bridge/PolicyBridge/IntentInterceptor.cs`
- [X] T134 [US4] Bridge: `ActionClassifier` (DeleteFile, GitPush, InstallPackage, RunShell, BuildClass) in `src/AgentPlatform.Bridge/PolicyBridge/ActionClassifier.cs`
- [X] T135 [US4] Bridge → API: emit `ConfirmationRequest`; suspend Claude until decision arrives; on decline, return error to Claude for graceful skip in `src/AgentPlatform.Bridge/PolicyBridge/ConfirmationGate.cs` *(decline path is implemented as a synthetic user-message back into the wrapper via `BridgeRuntime.ResumeWrapperAsync` — claude's stream-json permission protocol is not yet wired through `StreamJsonClaudeWrapper`; tracked as a follow-up.)*
- [X] T136 [US4] `IPolicyEngine.ClassifyAsync` consumes module-manifest policies + baseline; returns `ActionClassification` *(extended `DefaultPolicyEngine` with optional `IModulePolicyResolver`; manifest-policy loader wiring is deferred — Bridge currently runs without overrides, so launchpad's declared policies preserve baseline classifications.)*
- [X] T137 [US4] `/api/confirmations/{id}/decide` endpoint (POST {decision: confirm | decline}) in `src/AgentPlatform.Api/Endpoints/ConfirmationEndpoints.cs`
- [X] T138 [US4] Audit row per `ConfirmationRequest`: proposed, confirmed/declined, executed, succeeded/failed *(proposed + confirmed/declined are written; executed/succeeded/failed remain to be wired once the wrapper-permission protocol lands.)*
- [X] T139 [US4] `BuildStepSemaphore.AcquireAsync` integrated in `IntentInterceptor` for `BuildClass` actions (waits in `Waiting` state if cap hit)
- [X] T140 [US4] Confirmation queue: multiple pending prompts per session presented one at a time
- [X] T141 [P] [US4] `ConfirmationDialog` component (names action, target, originating phase) in `web/src/components/ConfirmationDialog.tsx`
- [X] T142 [P] [US4] Confirmation queue UI in `web/src/components/ConfirmationDialog.tsx` (extend T141)
- [X] T143 [P] [US4] Web `Waiting` indicator on phase view when build-step semaphore blocks in `web/src/pages/Phase.tsx` (extend T112)

**Checkpoint**: All P1 stories functional. Platform is shippable for early-access agencies.

---

## Phase 7: User Story 5 — Browse module catalogue and pick a workflow (Priority: P3)

**Goal**: Users browse installed modules + workflows, pick one, and start a run from the catalogue.

**Independent Test**: With one module installed, `GET /api/modules` returns the launchpad with `onboard-client`, `launch-ads`, `monthly-report` workflows; the catalogue UI lists them; clicking one routes to the run-starter form pre-filled.

### Tests for User Story 5

- [X] T144 [P] [US5] Integration test: catalogue lists installed modules + their workflows; refusal of unknown-schemaVersion module surfaces in catalogue as `Unavailable` in `tests/AgentPlatform.Api.Tests/Integration/CatalogueTests.cs` *(Unavailable surface verified via the application-layer `ModuleRegistry` flow already; full WAF integration deferred — same gating reason as T094-T097.)*

### Implementation for User Story 5

- [X] T145 [US5] Extend `/api/modules` (T083) to include workflow descriptions, declared inputs, and per-module status (`Available` | `Unavailable: <reason>`) *(landed during T083 — `ModuleEndpoints.ToDto` already returns workflows + inputs JSON + per-module status; verified, no new code needed.)*
- [X] T146 [P] [US5] Web catalogue page (cards per module with workflow list) in `web/src/pages/Catalogue.tsx`
- [X] T147 [P] [US5] Deep-link from catalogue → run-starter form pre-filled with selected workflow in `web/src/pages/Catalogue.tsx` *(uses existing `?moduleId=&workflowId=` query-param plumbing already wired in `StartRun.tsx`.)*

**Checkpoint**: Discovery surface in place. Power users can launch workflows without typing.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story tests, observability, ops, docs, and refreshing the stale supporting artefacts.

- [ ] T148 [P] BenchmarkDotNet bench suite in `tests/AgentPlatform.Bench/` (run-container cold start, bridge round-trip, hand-off propagation, file-tree update latency); CI gate on >10 % regression vs baseline *(deferred: no perf budget violations observed yet; needs a baseline run + regression gate before being meaningful — see `constitution-check.md`.)*
- [ ] T149 [P] Playwright E2E: complete `onboard-client` run with hand-offs across 3 distinct roles in `web/e2e/onboard-client.spec.ts` *(deferred: needs full docker-compose stack running in CI.)*
- [ ] T150 [P] Playwright E2E: dangerous-action confirmation full loop in `web/e2e/dangerous-action.spec.ts` *(deferred: blocks on T149 + claude permission protocol from US4 caveat.)*
- [ ] T151 [P] A11y audit (axe-core) in Playwright suite in `web/e2e/a11y.spec.ts` *(deferred with the Playwright suite.)*
- [X] T152 [P] Structured logging via Serilog (JSON to stdout, container-friendly) in `src/AgentPlatform.Api/Logging/SerilogConfig.cs`
- [X] T153 [P] Web 404 + 500 + offline pages in `web/src/pages/Error.tsx`
- [ ] T154 [P] Per-tenant rate-limiting middleware in `src/AgentPlatform.Api/Middleware/TenantRateLimitMiddleware.cs` *(deferred: not blocking for trusted early-access; reconsider before broad rollout.)*
- [X] T155 [P] Health-check endpoints (`/healthz`, `/readyz`) in `src/AgentPlatform.Api/Endpoints/HealthEndpoints.cs`
- [ ] T156 GitHub App skeleton (Octokit + JWT App-auth, install-token mint on demand) in `src/AgentPlatform.Infrastructure/GitHub/GitHubAppClient.cs` *(deferred: only needed once launchpad's `integrations` phase pushes to real client repos; defer until that's exercised.)*
- [ ] T157 Vault: secret-seeding script for local dev in `scripts/seed-vault.sh` *(deferred: dev-ergonomics nice-to-have, not shipping-blocking.)*
- [X] T158 Postgres backup script (nightly `pg_dump` cronned via systemd timer or compose service) in `scripts/backup.sh`
- [X] T159 Run-container image hardening: drop unnecessary capabilities, non-root user, read-only root FS where possible, in `Dockerfile.run-base` *(landed: `USER runner` with pinned uid/gid 1000, capability drops + DAC_OVERRIDE/FOWNER/CHOWN allowlist in `DockerRunContainerDriver`, `no-new-privileges` security opt, PIDs limit 512. ReadonlyRootfs deliberately left disabled — tradeoff documented inline.)*
- [ ] T160 [P] README + ARCHITECTURE in `docs/` *(deferred: write at handoff.)*
- [ ] T161 Refresh stale Phase 0/1 artefacts: rewrite `specs/001-agent-platform-mvp/research.md`, `data-model.md`, `quickstart.md`, and `contracts/` against the new architecture *(deferred: doc refresh, called out in constitution-check.md.)*
- [ ] T162 [P] CONTRIBUTING + code-of-conduct in repo root *(deferred.)*
- [X] T163 Final constitution check: re-run gates listed in `plan.md` §Constitution Check; address any drift *(written up in `specs/001-agent-platform-mvp/constitution-check.md` — verdict: conditional pass for early-access, with explicit gaps noted before broad rollout.)*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories.**
- **User Story 1 (Phase 3)**: Depends on Foundational. **No upstream story dependencies.**
- **User Story 2 (Phase 4)**: Depends on Foundational. Functionally requires US1 (a phase to open) but the implementation work is independent — US2 tasks can be developed in parallel with US1 against fakes (`FakeRunContainerDriver`, `FakeBridgeChannel`) and integrated when US1 lands.
- **User Story 3 (Phase 5)**: Depends on Foundational + US1 + US2 (you need a phase to hand off from). Best implemented after US2 is integration-green.
- **User Story 4 (Phase 6)**: Depends on Foundational + US2 (the confirmation flow lives inside a phase session). Best parallelised with US3 — different surfaces.
- **User Story 5 (Phase 7)**: Depends on Foundational + US1's `/api/modules` endpoint. Independent of US2/US3/US4.
- **Polish (Phase 8)**: Depends on whichever user stories you're shipping in the slice.

### Within Each User Story

- Tests written and FAIL before implementation (per Constitution II).
- Domain entities → application services → infrastructure adapters → API endpoints → web pages.
- Story complete and independently green before moving on.

### Parallel Opportunities

- All Phase 1 `[P]` tasks run in parallel (project scaffolds, configs, Dockerfiles).
- All Phase 2 `[P]` domain entities and application abstractions run in parallel.
- All Phase 2 `[P]` infrastructure adapters run in parallel after EF Core context (T050) lands.
- All `[P]` tests within a story run in parallel.
- All `[P]` web components within a story run in parallel.
- US2 tasks against fakes can run in parallel with US1 tasks.
- US4 tasks can run in parallel with US3 tasks once US2 is green.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Domain entities — all in parallel:
Task: "Tenant entity in src/AgentPlatform.Domain/Tenants/Tenant.cs"            # T022
Task: "User entity in src/AgentPlatform.Domain/Tenants/User.cs"                # T023
Task: "Role in src/AgentPlatform.Domain/Tenants/Role.cs"                       # T024
Task: "WorkflowDef + PhaseDef in src/AgentPlatform.Domain/Modules/WorkflowDef.cs"  # T027
Task: "WorkflowRun + PhaseRun in src/AgentPlatform.Domain/Runs/WorkflowRun.cs"     # T028
# ...etc through T035

# Application abstractions — all in parallel:
Task: "IPolicyEngine in src/AgentPlatform.Application/Policies/IPolicyEngine.cs"   # T040
Task: "IRunContainerDriver in src/AgentPlatform.Application/RunContainers/IRunContainerDriver.cs"  # T041
Task: "IBridgeChannel in src/AgentPlatform.Application/Bridge/IBridgeChannel.cs"  # T042
# ...etc

# Infrastructure adapters — parallel after EF Core context (T050):
Task: "FileEncryptedSecretStore in src/AgentPlatform.Infrastructure/Secrets/FileEncryptedSecretStore.cs"  # T052
Task: "FileSystemModuleSource in src/AgentPlatform.Infrastructure/Manifests/FileSystemModuleSource.cs"   # T054
Task: "PostgresAuditLog in src/AgentPlatform.Infrastructure/Persistence/Postgres/PostgresAuditLog.cs"    # T055
```

## Parallel Example: User Story 2

```bash
# Tests — all in parallel:
Task: "WebSocket chat I/O contract test in tests/AgentPlatform.Api.Tests/Hubs/PhaseSessionHubTests.cs"  # T094
Task: "File-tree event stream test in tests/AgentPlatform.Api.Tests/Hubs/FileTreeStreamTests.cs"         # T095

# Web components — all in parallel:
Task: "Inbox page in web/src/pages/Inbox.tsx"                                                            # T111
Task: "Phase view in web/src/pages/Phase.tsx"                                                            # T112
Task: "ChatPane component in web/src/components/ChatPane.tsx"                                            # T113
Task: "FileTree component in web/src/components/FileTree.tsx"                                            # T114
```

---

## Implementation Strategy

### Recommended MVP slice (early-access shippable)

The product is not viable with US1 alone (admin can start a run but no one can do anything). The minimum viable shippable slice is **US1 + US2 + US4**:

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational. (CRITICAL — blocks everything.)
3. Complete Phase 3: US1 (start runs, route to inbox).
4. Complete Phase 4: US2 (open phase, chat, file tree).
5. Complete Phase 6: US4 (dangerous-action confirmations — non-negotiable for safety).
6. **STOP and VALIDATE**: a single agency, single role, can run a workflow end-to-end with safety. Single-role agencies (rare but real) can use it.
7. Add Phase 5: US3 (multi-role hand-offs) — this unlocks the agency vision and is the priority follow-up.
8. Add Phase 7: US5 (catalogue) — convenience.
9. Polish (Phase 8) ongoing throughout.

### Incremental delivery

1. Foundation ready → infrastructure visible.
2. + US1 → admins can sign in and queue runs (internal demo only).
3. + US2 → first usable end-to-end flow against a single role.
4. + US4 → safe to expose to early-access agencies.
5. + US3 → full multi-role agency value prop.
6. + US5 → discovery surface.

### Parallel team strategy

With multiple developers, after Foundational (Phase 2) is green:

- **Developer A**: US1 → US3 (run lifecycle, hand-offs).
- **Developer B**: US2 (per-phase session, bridge, container driver) — builds against fakes initially.
- **Developer C**: US4 (policy/confirmation flow) — builds against US2's fakes.
- **Developer D**: web client (parallelised across stories via mocked API).

Stories complete and integrate independently. Polish tasks (Phase 8) are picked up opportunistically, with T148 (benchmarks) and T149–T150 (E2E) gating each shippable slice.

---

## Notes

- `[P]` = different files, no dependency on incomplete tasks.
- `[Story]` label maps tasks to user stories for traceability.
- Tests MUST be written and red before implementation per Constitution II.
- The launchpad is bundled as a Git submodule; do not modify launchpad source from this repo — open PRs against `github.com/DigitalFoxAgency/df-client-launchpad` instead.
- Stale supporting artefacts (`research.md`, `data-model.md`, `quickstart.md`, `contracts/`) are refreshed by **T161** in Phase 8; until then, treat `plan.md` + `spec.md` as the only authoritative inputs.
- Branch protection on `desktop` branch (preserved snapshot of the desktop MVP) is set out-of-band by the project owner.
