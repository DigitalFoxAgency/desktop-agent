# Implementation Plan: Desktop AI Agent Platform (MVP)

**Branch**: `001-agent-platform-mvp` | **Date**: 2026-04-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-agent-platform-mvp/spec.md`

## Summary

Build a cross-platform desktop AI agent application that lets a
subscriber sign in, supply their own model API token, and hold a live
chat conversation backed by a versioned module / scenario system and a
mandatory policy engine for dangerous actions. The desktop shell is
Avalonia-on-.NET; the local agent runtime (orchestrator + sandbox) is
managed as an out-of-process subsystem behind a runtime-manager
abstraction so the entire app is testable without it. Persistence is
local SQLite for chat/audit, OS secure storage for credentials, and
file-system loaders for module/scenario manifests. An optional
ASP.NET Core API validates subscriptions out-of-band.

## Technical Context

**Language/Version**: C# 12 on .NET 9 (LTS-track) for all projects.
**Primary Dependencies**:
- UI: Avalonia 11.x (cross-platform XAML), CommunityToolkit.Mvvm.
- Persistence: Microsoft.Data.Sqlite + Dapper (lightweight,
  non-EF — see research.md).
- Secure storage: pluggable `ISecretStore` with platform adapters
  (DPAPI on Windows, Keychain on macOS, libsecret on Linux,
  encrypted-file fallback).
- Manifests: System.Text.Json for `module.json`, JSON Schema
  validation via NJsonSchema. (No scenario YAML — operations are
  declared inside `module.json`; see research.md R5/R18.)
- Process / IPC: `System.Diagnostics.Process` + JSON-RPC over
  stdio for runtime communication; MCP integrations via the
  ModelContextProtocol .NET SDK.
- API (optional project): ASP.NET Core Minimal APIs.
- Testing: xUnit, FluentAssertions, NSubstitute,
  Avalonia.Headless for UI tests, Verify for snapshot tests.
**Storage**:
- Local SQLite database at the OS-appropriate per-user data
  directory for conversations, messages, policy decisions, and audit.
- File-system root `<userData>/modules/` for module manifests
  (each module declares its own operations inside `module.json`);
  bundled defaults shipped with the installer. No separate
  `scenarios/` location — see research.md R5/R18.
- OS secure credential store for the model API token and
  subscription session.
**Testing**: xUnit unit + integration suites per project, plus a
contract-test project that exercises every published service
interface against in-memory and real adapters.
**Target Platform**: Windows 10+ (x64, arm64), macOS 12+ (x64,
arm64), Linux (x64, glibc-based desktop distros). Single .NET 9
codebase; AOT not required for MVP.
**Project Type**: Desktop application + supporting class libraries;
optional ASP.NET Core service for subscription validation.
**Performance Goals**: Bound by the constitution — see Constitution
Check below. Concretely: cold start to interactive ≤2.0 s on the
reference machine; first agent token streamed within 2 s of send for
95% of messages on broadband (SC-002); UI thread never blocks >50 ms
per frame.

**Reference machine** (used by every constitution Principle IV
budget and by SC-006): Apple M2 Pro / 16 GB / SSD on macOS 14 for
the macOS leg; Intel i5-1240P / 16 GB / NVMe on Windows 11 for the
Windows leg; GitHub Actions `ubuntu-latest` runner for the Linux
leg. Benchmarks publish per-leg numbers; gates fire on >10%
regression vs the per-leg baseline rather than against a single
golden machine.
**Constraints**:
- Domain layer MUST NOT reference Avalonia, SQLite, OpenClaw,
  NemoClaw, Stripe, or any file-system / network API.
- UI layer MUST NOT reference Infrastructure directly; it talks
  only to Application services.
- Dangerous actions (delete files, git push, install packages,
  shell commands) require explicit per-occurrence confirmation.
- Full automated test suite MUST run green without the real
  OpenClaw / NemoClaw runtime installed (FR-019, SC-007).
**Scale/Scope**:
- Single-user-per-OS-account desktop app.
- ~5 .NET projects + 2 test projects at MVP.
- 1 bundled module (`df-client-launchpad`, sourced as a Git
  submodule). The module declares 3 operations the platform
  delegates to (`onboard-client`, `launch-ads`, `monthly-report`);
  there are no platform-side scenarios.
- Local SQLite expected to stay below 100 MB for typical use.

**Terminology** (worth pinning even at MVP scale to avoid
ambiguity in PR review):
- **Module** — the **product** concept: a versioned package of
  AI capabilities loaded via `module.json` (e.g.
  `df-client-launchpad`). User-facing.
- The MVP uses a layered architecture (no architectural
  "subsystems" yet); a future modular-monolith migration is
  documented in `docs/architecture/subsystems-future.md` and
  recorded as a deferred decision in `research.md` R16. When
  that migration happens, the architectural unit will be called
  a **Subsystem** to avoid colliding with the product term
  "Module".

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution (`.specify/memory/constitution.md`, v1.0.0)
defines four non-negotiable principles. This plan addresses each:

| Principle | How this plan satisfies it |
|-----------|---------------------------|
| **I. Code Quality** | Solution enforces `TreatWarningsAsErrors=true`, nullable enabled, Roslyn analyzers + `.editorconfig` shipped at repo root. PR template requires reviewer sign-off; complexity cap enforced by analyzer. Public service interfaces (`IChatService`, `IModuleRegistry`, `IScenarioRunner`, `IPolicyEngine`, `IRuntimeManager`, `ISecretStore`) carry XML doc comments mirrored into `contracts/`. |
| **II. Testing Standards** | TDD from day one: every service interface has a contract-test fixture before its first adapter ships. Coverage gate in CI: ≥90% for `Domain` and `Application`, ≥80% for `Infrastructure`/`Desktop`, **100% branch coverage** for every module that handles user data, IPC boundaries, or filesystem mutations — concretely: `DefaultPolicyEngine`, `SqliteChatRepository`, `SqliteAuditLog`, `ProcessRuntimeManager`, `FakeRuntimeManager` (its programmable surface), `FileSystemModuleSource`, `FileSystemScenarioSource`, `SessionLogReader`, `EncryptedFileSecretStore`, `WindowsDpapiSecretStore`, `MacKeychainSecretStore`, `LinuxSecretStore`. Integration tests use real SQLite (file-backed temp DB) and `FakeRuntimeManager`; mocks only for the model provider HTTP boundary, paired with one contract test against the real provider behind a `RequiresLiveModel` trait. |
| **III. UX Consistency** | Single Avalonia design system (`AgentDesktop.Desktop/Theme/`) with tokens for color, spacing, typography. All copy externalised to `.resx` (English-only at MVP, but localisation-ready). Every UI surface tested against axe-core-equivalent (Avalonia.Headless + a11y assertions). Confirmation prompt is one shared component reused for every dangerous action so wording and affordances stay identical. |
| **IV. Performance** | Budgets declared up front (see Technical Context). Benchmark project `AgentDesktop.Bench` runs in CI on every PR with BenchmarkDotNet for runtime-manager startup, scenario step latency, and chat message round-trip; CI fails on >10% regression vs. baseline. Long-running work (runtime install, scenario execution, model streaming) runs on background tasks; UI thread asserts no synchronous I/O via a debug-only watchdog. |

**Gate result**: PASS. No violations require Complexity Tracking
entries.

**Re-check after Phase 1**: PASS — design choices in `data-model.md`
and `contracts/` preserve the layering rules and the runtime-test
abstraction; no constitutional drift introduced.

## Project Structure

### Documentation (this feature)

```text
specs/001-agent-platform-mvp/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (service interface contracts + manifest schemas)
│   ├── IChatService.md
│   ├── IModuleRegistry.md
│   ├── IScenarioRunner.md
│   ├── IPolicyEngine.md
│   ├── IRuntimeManager.md
│   ├── ISecretStore.md
│   ├── module.schema.json
│   └── scenario.schema.json
├── checklists/
│   └── requirements.md  # Spec-quality checklist (already created)
└── tasks.md             # Phase 2 output (NOT created here — /speckit-tasks)
```

### Source Code (repository root)

```text
AgentDesktop.sln

src/
├── AgentDesktop.Domain/            # Pure domain models. No external deps.
│   ├── Chat/                       # Conversation, Message, MessageAuthor
│   ├── Modules/                    # Module, Skill, ModuleManifest, SkillPolicy
│   ├── Scenarios/                  # Scenario, ScenarioStep, ScenarioStatus
│   ├── Policies/                   # ActionClassification, PolicyDecision, DangerousAction
│   └── Runtime/                    # RuntimeStatus, RuntimeKind (enum-only)
│
├── AgentDesktop.Application/       # Use cases, orchestration. Depends on Domain only.
│   ├── Chat/                       # IChatService + ChatService implementation
│   ├── Modules/                    # IModuleRegistry, ModuleLoader (uses IModuleSource), IDelegationRunner
│   ├── Policies/                   # IPolicyEngine + DefaultPolicyEngine
│   ├── Runtime/                    # IRuntimeManager + IDelegationCallbacks (abstractions only)
│   ├── Secrets/                    # ISecretStore (abstraction only)
│   ├── Subscription/               # ISubscriptionGate
│   └── Abstractions/               # IModuleSource, IClock, IConfirmationPrompt, IAuditLog
│
├── AgentDesktop.Infrastructure/    # Adapters. Depends on Application + Domain.
│   ├── Persistence/Sqlite/         # SqliteChatRepository, SqliteAuditLog, migrations
│   ├── Manifests/                  # FileSystemModuleSource (validates module.json including operations[])
│   ├── Secrets/                    # WindowsDpapiSecretStore, MacKeychainSecretStore, LinuxSecretStore, EncryptedFileSecretStore
│   ├── Runtime/                    # ProcessRuntimeManager (OpenClaw + NemoClaw delegation IPC); FakeRuntimeManager lives in tests/AgentDesktop.Contracts.Tests/Fakes/, NOT here
│   ├── Mcp/                        # McpClient, McpServerLauncher
│   └── Subscription/               # HttpSubscriptionGate
│
├── AgentDesktop.Desktop/           # Avalonia UI. Depends on Application only.
│   ├── App.axaml / Program.cs      # Composition root (DI: Application + Infrastructure wiring)
│   ├── Theme/                      # Design tokens, shared controls
│   ├── Views/                      # ChatView, ModuleCatalogView, OperationRunView, ConfirmationDialog, HumanHandoffDialog, SignInView
│   ├── ViewModels/                 # MVVM via CommunityToolkit.Mvvm
│   └── Resources/                  # .resx strings
│
└── AgentDesktop.Api/               # Optional ASP.NET Core. Subscription validation. Depends on Domain.
    ├── Program.cs
    └── Endpoints/

modules/                            # Bundled modules
└── df-client-launchpad/
    ├── module.json                 # Manifest authored in this repo (parent-tracked)
    └── source/                     # Git submodule: github.com/DigitalFoxAgency/df-client-launchpad@refactor
        └── ...                     # Launchpad files; SKILL.md paths resolved relative to the module root, e.g. source/template/.claude/skills/init/SKILL.md

# NOTE: there is no platform-side scenarios/ directory. Operations
# are declared inside each module's module.json under operations[].
# See research.md R5 and R18 for the architectural rationale (modules
# own their orchestration; the platform delegates whole operations).

tests/
├── AgentDesktop.Domain.Tests/
├── AgentDesktop.Application.Tests/
├── AgentDesktop.Infrastructure.Tests/
├── AgentDesktop.Desktop.Tests/         # Avalonia.Headless
├── AgentDesktop.Contracts.Tests/       # Contract tests for every service interface
└── AgentDesktop.Bench/                 # BenchmarkDotNet
```

**Structure Decision**: Clean Architecture with five layered .NET
projects (Domain → Application → Infrastructure / Desktop / Api). The
boundary rules are enforced at the project-reference level
(`Domain.csproj` references nothing; `Application.csproj` references
only `Domain`; `Infrastructure.csproj` references `Application` +
`Domain`; `Desktop.csproj` references `Application` only — the DI
composition root in `Desktop` is the *only* place that knows about
`Infrastructure`, so the UI never gains a transitive coupling to
SQLite or runtime details). This layout maps 1:1 to the architecture
constraints in the feature description and to the constitution's
code-quality + testability principles.

A migration to a **modular monolith** (one project per bounded
context with `Contracts/` boundaries enforced by analyzer) was
considered and **deferred to post-MVP** (research.md R16). The
fully-designed migration target is captured in
`docs/architecture/subsystems-future.md` for the day it becomes
warranted.

## MVP Module: `df-client-launchpad`

The MVP bundles exactly **one** module: `df-client-launchpad`,
sourced from `github.com/DigitalFoxAgency/df-client-launchpad`
(`refactor` branch) and pinned via Git submodule under
`modules/df-client-launchpad/`. The submodule is the single source
of truth for skill behaviour; this repo authors a thin
`modules/df-client-launchpad/module.json` (next to the submodule
checkout) that:

- Declares `id`, `version`, `name`, `description`.
- Declares **3 operations** the platform delegates to:
  - `onboard-client` — Phase 1 client onboarding (the launchpad's
    full intake-to-deploy pipeline).
  - `launch-ads` — Phase 2 ads kickoff.
  - `monthly-report` — Phase 2 reporting.
  Each operation declares its declared inputs (e.g. `onboard-client`
  takes `niche`, `city`, `clientName`); execution is the launchpad's
  responsibility.
- May ALSO list internal skills under `skills[]` for introspection
  / advanced direct invocation (US4 power-user path). The platform
  does NOT orchestrate skills — see research.md R18.
- Declares per-action `policies[]` so the platform's policy engine
  knows which classes of actions the launchpad will propose
  (delete file, git push, install package, run shell), letting
  the engine pre-classify them as Dangerous before the launchpad
  ever proposes one. The actual proposals come from the launchpad
  at runtime via the delegation callback channel
  (`IDelegationCallbacks.RequestConfirmationAsync`).
- Surfaces "human-only" steps (Discovery call, content approval,
  ads dashboard work) by emitting
  `IDelegationCallbacks.RequestHumanHandoffAsync` from inside the
  module — the platform shows the hand-off dialog and resumes the
  module when the user marks the step done.

### Bundled operations

| Operation | Purpose |
|-----------|---------|
| `onboard-client` | Phase 1 — full client onboarding from intake to live Cloudflare Pages deployment. The launchpad's internal pipeline (init → pre-research → brief → research → semantics → strategy → … → deploy) runs entirely inside the module. |
| `launch-ads` | Phase 2 — ads kickoff once the site is live. |
| `monthly-report` | Phase 2 — recurring optimisation pass and monthly client report. |

Cross-module composition is intentionally not exercised at MVP
because there is only one module. The registry, delegation runner,
and policy engine remain generic — adding a second module
post-MVP requires no engine changes.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations to justify. The five-project split is the simplest layout
that enforces the layering constraints in the spec; collapsing it would
require runtime checks instead of compile-time guarantees and would
violate Code Quality (Principle I).
