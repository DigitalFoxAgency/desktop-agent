# Implementation Plan: Agency Workflow Platform (MVP)

**Branch**: `001-agent-platform-mvp` | **Date**: 2026-04-26 (rewritten 2026-04-28 for the web/server pivot) | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-agent-platform-mvp/spec.md`. The desktop-MVP is preserved on the `desktop` branch and is no longer the target.

## Summary

Build a multi-tenant web platform where marketing agencies run packaged AI workflows (starting with `df-client-launchpad`) end-to-end on a backend that hosts **Claude Code per run**. Agency staff with different roles pick up phases from a role-based inbox, hold a chat conversation in their browser, and watch files appear in a live read-only file tree. The platform supplies the AI under the hood (no BYOK).

The platform does not re-implement orchestration. The launchpad already orchestrates itself (its `ORDER.md`, skills, `SESSION-LOG.md`). The platform is a host: it runs `claude` inside a sandboxed container per session, mounts a persistent working directory across phase hand-offs, gates dangerous actions, and routes phases by role.

The first deployment target is a **single Linux VPS (2 cores / 8 GB RAM)** running Docker. Run execution sits behind a container-driver abstraction so we can move to elastic container exec (Fly Machines, Fargate) when volume demands it without rearchitecting.

## Technical Context

**Language/Version**: C# 12 on .NET 9 (LTS-track) for the server; TypeScript + React for the web client.

**Primary dependencies**:

- **API**: ASP.NET Core Minimal APIs + WebSocket. Auth via ASP.NET Core Identity (email + password; external providers via OIDC deferred).
- **Persistence**: Microsoft.EntityFrameworkCore + Npgsql for Postgres (chat-transcript-free; SaaS plumbing only). No SQLite.
- **Secrets**: pluggable `ISecretStore` with a file-based KMS-encrypted vault for MVP; pluggable for HashiCorp Vault / cloud secret manager later.
- **Manifests**: System.Text.Json for `module.json`; JSON Schema validation via NJsonSchema.
- **Container exec**: pluggable `IRunContainerDriver`; the MVP driver talks to the local Docker Engine (via `Docker.DotNet` or HTTP to `unix:///var/run/docker.sock`).
- **Bridge**: a small .NET process inside each run container that wraps the `claude` CLI (interactive PTY or stream-JSON — decided at prototype time) and exposes a WebSocket back to the API for chat I/O, confirmation prompts, and file-system events.
- **GitHub**: Octokit.net configured for **GitHub App** installation tokens (per-tenant, short-lived).
- **Anthropic**: server-side platform key only; prompt caching enabled. (Agencies do **not** supply their own keys.)
- **Web client**: TypeScript + React + Vite + Tailwind. State via TanStack Query. WebSocket via native `WebSocket` API.
- **Testing**: xUnit, FluentAssertions, NSubstitute, Verify, Testcontainers (real Postgres in integration tests), Playwright for E2E.

**Storage**:

- **Postgres** (single DB): tenants, users, roles, subscriptions, modules, workflow runs, phase runs, inbox items, confirmation requests, audit log, usage ledger, vault references. **No chat transcripts.**
- **Per-run persistent volume** on the host filesystem at `/var/lib/agency/runs/<runId>/`. Holds the launchpad working directory across phase hand-offs. Backed up on run completion.
- **Module sources** under `modules/` (the launchpad as a Git submodule, baked into the run-container image).
- **Vault**: file-based encrypted store for MVP, mounted read-only into the API service.

**Testing**: xUnit unit + integration suites per project; a contract-test project that exercises every published service interface against in-memory and real adapters; Playwright E2E driven by a docker-compose harness.

**Target platform**:

- **Server**: Linux x64, Ubuntu 22.04+, Docker Engine 24+. Single-VPS deployment (2 cores / 8 GB RAM, +4–8 GB swap).
- **Web client**: modern Chromium, Firefox, Safari (desktop primary; mobile read-only acceptable at MVP).

**Project type**: Multi-project .NET solution (Domain → Application → Infrastructure → Api + Bridge) plus a separate web client (TypeScript + React).

**Performance goals**:

- First-token latency: 95% of chat messages begin streaming within **2 s** of send on broadband.
- File-tree update latency: file changes appear in the web UI within **2 s**.
- Phase hand-off propagation: next assignee's inbox updated within **5 s** of phase completion.
- Concurrent capacity on the reference VPS: **3 idle runs sustained**, **2 concurrent build-class steps** without OOM (build-step semaphore enforced).

**Reference machine**: Ubuntu 22.04 VPS, 2 cores, 8 GB RAM, NVMe, gigabit. Performance gates fire on >10 % regression vs the per-leg baseline.

**Constraints**:

- Domain layer MUST NOT reference Postgres, Docker, GitHub, Anthropic, or any I/O API.
- Application layer MUST NOT reference Infrastructure directly; it talks only to its own abstractions.
- Web client MUST NOT call Postgres or vault directly; everything flows through the API.
- The platform MUST NOT replicate the launchpad's pipeline. Phase semantics live inside `module.json`; phase execution lives inside the launchpad's skills.
- Run containers MUST be tenant-scoped; no cross-tenant container or volume reuse.
- Concurrent build-class steps MUST be capped at **2 host-wide** via a semaphore in the API.
- Dangerous actions (delete files, git push, install packages, shell commands) MUST require explicit per-occurrence confirmation surfaced in the web UI.

**Scale/Scope**:

- Multi-tenant SaaS deployed to a single VPS at MVP.
- ~6 .NET projects + 6 test projects + 1 web client.
- 1 bundled module (`df-client-launchpad`, Git submodule).
- ≤5 concurrent runs at MVP volume.
- Postgres expected to stay below ~1 GB for MVP-scale agencies.

**Terminology**:

- **Module** — an agency-installable, versioned product (e.g. `df-client-launchpad`). User-facing.
- **Workflow** — a graph of phases declared inside a module (e.g. `onboard-client`).
- **Phase** — a single assignable unit inside a workflow. Declares its required role and the underlying skill the launchpad will run.
- **Run** — a live execution of a workflow for a tenant.
- **Session** — one user's interaction with Claude Code for one phase. Short-lived; fresh `claude` process. Cross-phase state carries via the persistent working directory.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution (`.specify/memory/constitution.md`, v1.0.0) defines four non-negotiable principles. The pivoted plan addresses each:

| Principle | How this plan satisfies it |
|-----------|---------------------------|
| **I. Code Quality** | Solution enforces `TreatWarningsAsErrors=true`, nullable enabled, Roslyn analyzers + `.editorconfig` shipped at repo root. Public service interfaces (`IRunContainerDriver`, `IBridgeChannel`, `IPolicyEngine`, `IModuleRegistry`, `ISubscriptionGate`, `ISecretStore`, `IAuditLog`, `IUsageMeter`) carry XML doc comments mirrored into `contracts/`. Web client enforces `tsc --strict`, ESLint, Prettier, no implicit `any`. |
| **II. Testing Standards** | TDD: every service interface has a contract-test fixture before its first adapter. Coverage gate in CI: ≥90 % for `Domain` and `Application`, ≥80 % for `Infrastructure`/`Api`/`Bridge`, **100 % branch coverage** on every module that handles user data, IPC boundaries, or filesystem mutations: `DefaultPolicyEngine`, `PostgresChatRepository` (and other Postgres adapters), `DockerRunContainerDriver`, `FakeRunContainerDriver`, `BridgeChannel`, `FileSystemModuleSource`, `FileEncryptedSecretStore`, `WorkflowRunService`, `PhaseAssignmentService`, `UsageMeter`, `SubscriptionGate`. Integration tests use real Postgres via Testcontainers and `FakeRunContainerDriver`; mocks only at the Anthropic and GitHub HTTP boundaries, paired with one contract test against the real API behind a `RequiresLiveExternal` trait. E2E via Playwright + docker-compose covers the chat / file-tree / confirmation / hand-off loop. |
| **III. UX Consistency** | Single design system (`web/src/design/`) with tokens for color, spacing, typography. All copy externalised (i18n-ready; English-only at MVP). Every UI surface tested with Playwright + axe-core (a11y assertions). Confirmation prompt is one shared component reused for every dangerous action so wording and affordances stay identical. |
| **IV. Performance** | Budgets declared up front (see Technical Context). Benchmark project `AgentPlatform.Bench` runs in CI on every PR with BenchmarkDotNet for run-container startup, bridge round-trip, and phase-handoff propagation. CI fails on >10 % regression vs baseline. Long-running work runs on background tasks; the API thread asserts no synchronous I/O via a debug-only watchdog. The build-step semaphore prevents host saturation. |

**Gate result**: PASS. No violations require Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/001-agent-platform-mvp/
├── plan.md              # This file
├── spec.md              # Pivoted spec
├── research.md          # Phase 0 output (STALE — desktop-flavoured; needs refresh)
├── data-model.md        # Phase 1 output (STALE — desktop-flavoured; needs refresh)
├── quickstart.md        # Phase 1 output (STALE — desktop-flavoured; needs refresh)
├── contracts/           # Service interface contracts + manifest schemas (STALE — needs refresh)
├── checklists/
│   └── requirements.md  # Spec-quality checklist
└── tasks.md             # Phase 2 output (STALE — regenerate via /speckit-tasks against the new plan)
```

> The `research.md`, `data-model.md`, `quickstart.md`, and `contracts/` artefacts are **stale** as of the pivot and refer to the desktop-MVP architecture. They need refreshing as a follow-up. They are not blocking the implementation re-start; the new `plan.md` and `spec.md` are sufficient input.

### Source code (repository root)

```text
AgentPlatform.sln

src/
├── AgentPlatform.Domain/             # Pure domain. No external deps.
│   ├── Tenants/                      # Tenant, User, Role, UserRole
│   ├── Modules/                      # Module, ModuleVersion, WorkflowDef, PhaseDef
│   ├── Runs/                         # WorkflowRun, PhaseRun, Assignment
│   ├── Policies/                     # ActionClassification, PolicyDecision, DangerousAction
│   ├── Inbox/                        # InboxItem, ConfirmationRequest
│   └── Audit/                        # AuditEntry, UsageLedgerEntry
│
├── AgentPlatform.Application/        # Use cases. Depends on Domain only.
│   ├── Tenants/                      # tenant CRUD, user/role management
│   ├── Auth/                         # ASP.NET Core Identity bridge
│   ├── Modules/                      # IModuleRegistry, ModuleLoader
│   ├── Runs/                         # IWorkflowRunService, IPhaseAssignmentService
│   ├── Policies/                     # IPolicyEngine + DefaultPolicyEngine
│   ├── Bridge/                       # IBridgeChannel (abstraction)
│   ├── RunContainers/                # IRunContainerDriver (abstraction)
│   ├── Subscription/                 # ISubscriptionGate
│   ├── Usage/                        # IUsageMeter
│   ├── Secrets/                      # ISecretStore
│   └── Abstractions/                 # IClock, IAuditLog, IInboxNotifier, IModuleSource
│
├── AgentPlatform.Infrastructure/     # Adapters. Depends on Application + Domain.
│   ├── Persistence/Postgres/         # EF Core context, migrations, repository adapters
│   ├── Manifests/                    # FileSystemModuleSource (validates module.json)
│   ├── Secrets/                      # FileEncryptedSecretStore (KMS-style); pluggable
│   ├── RunContainers/                # DockerRunContainerDriver (talks to Docker Engine)
│   ├── Bridge/                       # WebSocketBridgeChannel (server-side)
│   ├── GitHub/                       # GitHubAppClient (Octokit + JWT App auth)
│   ├── Anthropic/                    # platform-key client; prompt-cache helper
│   └── Subscription/                 # SubscriptionRecorder
│
├── AgentPlatform.Api/                # ASP.NET Core API + WebSocket.
│   ├── Program.cs                    # Composition root (DI: Application + Infrastructure wiring)
│   ├── Endpoints/                    # Tenants, Auth, Modules, Runs, Phases, Inbox, Confirmations
│   ├── Hubs/                         # WebSocket endpoints (chat, file-tree, confirmation routing)
│   └── Middleware/                   # Tenant scoping, audit
│
└── AgentPlatform.Bridge/             # Process that runs INSIDE each run container.
    ├── Program.cs                    # Wraps the `claude` CLI; exposes WebSocket to the API.
    ├── ClaudeWrapper/                # PTY / stream-JSON adapter
    ├── FileWatcher/                  # Working-dir watcher; debounced events
    └── PolicyBridge/                 # Translates Claude tool-use intents into ConfirmationRequest

web/                                  # TypeScript + React + Vite + Tailwind
├── src/
│   ├── pages/                        # Sign-in, agency dashboard, run starter, inbox, phase view, admin
│   ├── components/                   # ChatPane, FileTree, ConfirmationDialog, InboxList
│   ├── design/                       # Tokens, shared components
│   ├── api/                          # API client + WebSocket helpers
│   └── i18n/                         # English strings
└── e2e/                              # Playwright specs

modules/
└── df-client-launchpad/
    ├── module.json                   # Manifest (workflows declared; phases map skills to roles)
    └── source/                       # Git submodule (unchanged from desktop MVP)

tests/
├── AgentPlatform.Domain.Tests/
├── AgentPlatform.Application.Tests/
├── AgentPlatform.Infrastructure.Tests/    # Real Postgres via Testcontainers
├── AgentPlatform.Api.Tests/
├── AgentPlatform.Bridge.Tests/
├── AgentPlatform.Contracts.Tests/         # Contract tests for every service interface
└── AgentPlatform.Bench/                   # BenchmarkDotNet
```

**Structure decision**: Clean Architecture, four backend layers (Domain → Application → Infrastructure → Api / Bridge) plus a separate web client. Layering is enforced at the project-reference level. The `Bridge` is its own project so it can be packaged as a small Docker image layer alongside the launchpad and `claude`.

## MVP Module: `df-client-launchpad`

The MVP bundles exactly **one** module: `df-client-launchpad`, sourced from `github.com/DigitalFoxAgency/df-client-launchpad` (`refactor` branch) and pinned via Git submodule under `modules/df-client-launchpad/`. The submodule is the single source of truth for skill behaviour; `module.json` declares **workflows** (each a graph of phases) the platform exposes.

### Bundled workflows

| Workflow | Phases (in order) | Purpose |
|----------|-------------------|---------|
| `onboard-client` | init · pre-research · keywords · brief · research · semantics · strategy · strategy-pdf · offer · architecture · design · site · integrations · seo · deploy | Phase 1 — full client onboarding from intake to live Cloudflare Pages deployment. |
| `launch-ads` | ads | Phase 2 — ads kickoff once the site is live. |
| `monthly-report` | reporting | Phase 2 — recurring optimisation pass and monthly client report. |

### Phase ↔ role mapping (default; agency can override)

| Phase | Skill | Role | Kind |
|-------|-------|------|------|
| init | init | engineer | automated |
| pre-research | pre-research | marketer | automated |
| keywords | keywords | marketer | automated |
| brief | brief | strategist | human |
| research | research | marketer | automated |
| semantics | semantics | marketer | automated |
| strategy | strategy | strategist | automated |
| strategy-pdf | strategy-pdf | designer | automated |
| offer | offer | strategist | human |
| architecture | architecture | strategist | automated |
| design | design | designer | automated |
| site | site | engineer | automated |
| integrations | integrations | engineer | automated |
| seo | seo | marketer | automated |
| deploy | deploy | engineer | automated |
| ads | ads | media-buyer | human |
| reporting | reporting | media-buyer | automated |

Cross-module composition is intentionally not exercised at MVP (only one module). The registry, run service, and policy engine remain generic — adding a second module post-MVP requires no engine changes.

## Hosting & Operations (MVP)

- **Deployment target**: a single Ubuntu 22.04+ VPS, 2 cores, 8 GB RAM, +4–8 GB swap, NVMe.
- **Runtime stack** via `docker-compose`:
  - `api` — ASP.NET Core API (HTTP + WebSocket).
  - `web` — nginx serving the SPA + reverse-proxying to `api` (TLS via Let's Encrypt or Caddy).
  - `postgres` — single-node Postgres.
  - `vault` — file-based encrypted secret store mounted into `api` (read-only).
  - `worker-daemon` — supervises run containers (lifecycle, build-step semaphore, log shipping).
- **Run containers** spawned on demand by `DockerRunContainerDriver`. Image: `agentplatform/run-base` (Claude Code, Node, npm, Git, gh, deploy CLIs, the launchpad submodule, the Bridge). Resource limits per container: `--memory=2g`, `--cpus=1.0`. Build-step semaphore (max 2 concurrent across the host) enforced server-side before allowing a build-class action to start.
- **Per-run volumes** mounted from `/var/lib/agency/runs/<runId>/` and reused across phase hand-offs. Archived to local backup on run completion.
- **Backups**: Postgres dump nightly to local backup directory; weekly off-host copy via the agency's preferred backup tool.

## Cost guardrails

Because the platform supplies the Anthropic key and bundles AI cost into the agency subscription, cost discipline is mandatory:

- **Token metering** per `PhaseRun` → `UsageLedgerEntry` rows: input, output, cache-read, cache-write tokens.
- **Per-tenant monthly token budget** enforced by `ISubscriptionGate` before starting a workflow and re-checked at every automated phase.
- **Per-run hard token cost cap**: workflow auto-pauses if exceeded; admin must raise the cap or terminate.
- **Anthropic prompt caching enabled on every phase**. The launchpad reloads similar context (CLAUDE.md, ORDER.md, prior research summary, design tokens) at every phase — caching can drop input cost 50–80 % in this shape.
- **Pricing tiers should map to compute envelopes** (workflows/month, automated phase minutes, token allowance), not just seats. (Pricing decisions sit outside the MVP technical scope but must not be blocked by the platform.)

## Multi-tenancy posture

- Strict row-level isolation via `tenant_id` on every row; query helpers enforce it at the API layer (`IRequestTenantContext`).
- Run containers scoped to a single run; no cross-tenant container or volume reuse.
- Vault paths namespaced by `tenant_id`.
- One Anthropic platform key for all tenants at MVP. Per-tenant key delegation revisited if/when Anthropic offers it on terms that justify the operational cost.

## Open decisions deferred to implementation

- **Bridge mechanism** (PTY vs. stream-JSON vs. Claude Agent SDK) — decided at first prototype against `agentplatform/run-base`.
- **TLS terminator** (nginx + Let's Encrypt vs. Caddy) — operational preference.
- **Backup off-host strategy** — agency-specific.
- **Email transactional notifications** — deferred past MVP; in-app inbox only at first.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified.

No violations to justify. The project layout is the simplest layered split that enforces the constraints in the spec and keeps the run-container and Anthropic boundaries swappable.
