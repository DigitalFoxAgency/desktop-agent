# Phase 0 Research — Desktop AI Agent Platform (MVP)

This document records the technology choices behind `plan.md` and the
alternatives considered. Every decision is testable, reversible, and
scoped to the MVP. There are no `NEEDS CLARIFICATION` items remaining.

---

## R1. UI framework — Avalonia 11.x

**Decision**: Avalonia 11.x with CommunityToolkit.Mvvm and a single
shared design system under `AgentDesktop.Desktop/Theme/`.

**Rationale**:
- Single XAML codebase covering Windows, macOS, and Linux from one
  installer — directly satisfies FR-001.
- Mature MVVM story; pairs cleanly with CommunityToolkit.Mvvm
  (`[ObservableProperty]`, `[RelayCommand]`) so view-models remain
  trivially unit-testable without an Avalonia runtime.
- `Avalonia.Headless` enables UI tests without a display server,
  which matters for the constitution's testing-coverage gates and
  for CI on Linux runners.
- Native menu integration on macOS / Windows respects platform
  conventions required by Principle III (UX Consistency).

**Alternatives considered**:
- **MAUI** — desktop story on Linux is unsupported; rejected on
  FR-001 alone.
- **WPF / WinUI** — Windows-only; rejected on FR-001.
- **Electron / Tauri** — adds a non-.NET runtime, doubles the
  language surface, and breaks the single-stack architecture
  declared by the user.
- **Uno Platform** — viable, but its desktop targets are less
  battle-tested than Avalonia's at the time of writing and its XAML
  dialect drifts from WPF more than Avalonia's does.

---

## R2. Persistence — SQLite via Microsoft.Data.Sqlite + Dapper

**Decision**: Microsoft.Data.Sqlite for the connection, Dapper for
mapping, hand-written migrations under
`AgentDesktop.Infrastructure/Persistence/Sqlite/Migrations/`.

**Rationale**:
- Lightweight, no ORM ceremony, no model-first generation; schema is
  expressed as SQL, which makes the audit trail (FR-022) and the
  100% branch-coverage requirement on `ChatRepository` straightforward
  to satisfy.
- Microsoft.Data.Sqlite ships native binaries for all target
  platforms — no SQLite installation required by end users.
- Dapper keeps Infrastructure tiny and avoids EF Core's startup cost
  (relevant for the ≤2.0 s cold-start budget).

**Alternatives considered**:
- **EF Core 9** — adds ~100–200 ms to cold start and a non-trivial
  surface area to learn; rejected on performance budget + simplicity.
- **LiteDB** — single-file embedded NoSQL; rejected because chat
  history and audit log are intrinsically relational and we want
  SQL-level introspection.
- **Plain JSON files** — rejected: corruption risk on crash, no
  transactional guarantees for the audit log.

---

## R3. Secure credential storage — pluggable `ISecretStore`

**Decision**: Define `ISecretStore` in `Application/Secrets/` and
ship four adapters in Infrastructure: DPAPI (Windows), Keychain
(macOS, via the Security framework P/Invoke), libsecret (Linux), and
an `EncryptedFileSecretStore` fallback (AES-GCM with a key derived
via `Rfc2898DeriveBytes` from a machine-bound seed) for Linux
environments without libsecret.

**Rationale**:
- FR-003 requires OS secure storage where available; the fallback
  ensures the app still works on minimal Linux desktops.
- The interface lives in Application so Domain stays pure (Principle
  I + the user's architecture constraints).
- Each adapter is independently testable; the fallback's encryption
  routine is covered by a property test.

**Alternatives considered**:
- **`Microsoft.AspNetCore.DataProtection`** — primarily a web
  abstraction, awkward to host inside a desktop app and not
  OS-secure on macOS/Linux.
- **A single cross-platform library (e.g., Meziantou.Framework.Win32
  + others)** — rejected to keep the dependency surface auditable.

---

## R4. Module manifest format — JSON with JSON Schema validation

**Decision**: `module.json` per module, validated at load time
against `contracts/module.schema.json` using NJsonSchema. Schema
version is required (`"schemaVersion": 1`); unknown major versions
are refused (FR-018).

**Rationale**:
- JSON is the lowest-friction format for the module ecosystem the
  user described and aligns with how MCP servers typically describe
  themselves.
- A formal schema makes FR-006/FR-007/FR-018 mechanically testable
  and gives module authors a single source of truth.
- NJsonSchema runs offline, supports JSON Schema draft 2020-12, and
  has good error messages for module authors.

**Alternatives considered**:
- **YAML for modules too** — readable but easier to typo; reserved
  for scenarios where authoring ergonomics matter more.
- **TOML** — unfamiliar to most module authors in the AI tooling
  space; rejected.

---

## R5. Scenario format — YAML with the same schema discipline

**Decision**: Scenarios are YAML files validated against
`contracts/scenario.schema.json`. JSON is also accepted (same
schema) for tooling that prefers it.

**Rationale**:
- Scenarios are workflow definitions read and edited by humans
  (designers, power users); YAML's terseness wins here.
- Allowing JSON as a second valid encoding keeps scenarios
  machine-generatable without a separate schema.

**Alternatives considered**:
- **JSON only** — clearer parsing but worse authoring; rejected.
- **A custom DSL** — unjustified for MVP; revisit if scenarios grow
  control-flow constructs beyond linear step sequences.

---

## R6. Local agent runtime abstraction — `IRuntimeManager`

**Decision**: Define `IRuntimeManager` in Application with the
state machine `NotInstalled → Installing → Starting → Ready →
Degraded → Stopped`. Ship two implementations: `ProcessRuntimeManager`
in Infrastructure (manages OpenClaw orchestrator + NemoClaw sandbox
as two cooperating child processes communicating over JSON-RPC on
stdio) and `FakeRuntimeManager` in the test project (deterministic,
in-memory, configurable behaviours).

**Rationale**:
- Directly satisfies FR-015, FR-016, FR-017, FR-019, and SC-007.
- The fake is a first-class artifact, not an afterthought, so
  scenario tests can assert step-by-step orchestration behaviour
  without flakiness from real subprocess startup.
- Keeping both processes (orchestrator + sandbox) inside one
  manager respects the spec's "single logical runtime" assumption
  while still letting the implementation evolve.

**Alternatives considered**:
- **Embed OpenClaw via P/Invoke** — couples the desktop app's
  process to runtime crashes; rejected for sandbox-isolation
  reasons (Principle II + product safety).
- **Run orchestrator + sandbox in the same process** — defeats the
  sandbox's purpose; rejected.

---

## R7. Policy engine — declarative table + per-occurrence prompt

**Decision**: `DefaultPolicyEngine` evaluates each proposed action
against (a) a hard-coded baseline classification table covering the
four classes from FR-012 (delete files, git push, install packages,
shell commands) and (b) policies declared by the originating module
manifest. Any action classified `Dangerous` raises a confirmation
prompt through `IConfirmationPrompt` (implemented by the UI).
Confirmations are scoped to a single occurrence and never cached
(FR-013). Every decision is appended to the audit log (FR-022).

**Rationale**:
- A small declarative table is auditable and trivially
  property-testable for SC-003 / SC-004 (100% confirmation
  coverage).
- Per-occurrence scoping is enforced at the engine, not the UI, so
  no UI bug can accidentally bypass it.
- Module-declared policies allow new modules to extend the table
  without code changes (matching the user's manifest schema).

**Alternatives considered**:
- **Policy as Lua / Wasm scripts** — over-engineering for MVP and
  hard to audit; rejected.
- **Allow "remember my answer" toggle** — explicitly forbidden by
  FR-013.

---

## R8. Subscription validation — ISubscriptionGate + optional API

**Decision**: `ISubscriptionGate` in Application returns the current
subscription state. The Infrastructure adapter `HttpSubscriptionGate`
calls the optional `AgentDesktop.Api` ASP.NET Core service over HTTPS
at sign-in and on a periodic background check; results are cached
locally with a configurable offline grace window (default 7 days).
Token storage uses `ISecretStore`.

**Rationale**:
- Keeps Application pure (no HTTP types leak past the boundary).
- Lets FR-002 / FR-023 be exercised in tests with a fake gate.
- API project is optional so on-prem / self-host deployments can
  swap in a different gate without recompiling Application.

**Alternatives considered**:
- **Validate against the model provider's API key** — conflates
  subscription with model access; rejected on FR-002 separation.
- **License file shipped with installer** — acceptable as a
  fallback gate, but offline grace already covers the common case.

---

## R9. MCP integration — official .NET SDK

**Decision**: Use `ModelContextProtocol` (the official .NET SDK) for
both consuming MCP servers declared by modules and (later) exposing
the agent's own tools as MCP. Module manifests declare MCP servers
under `mcpServers[]`; `McpServerLauncher` brings them up inside the
sandbox layer of the runtime.

**Rationale**:
- Removes a from-scratch protocol implementation and keeps us
  aligned with the broader MCP ecosystem so existing servers
  (filesystem, github) drop in cleanly.
- The launcher routes MCP processes through NemoClaw, satisfying
  FR-017 (no bypass of the sandbox).

**Alternatives considered**:
- **Roll our own JSON-RPC client** — duplicates SDK work and risks
  protocol drift.

---

## R10. Testing stack — xUnit + Avalonia.Headless + BenchmarkDotNet

**Decision**: xUnit for everything, FluentAssertions for readable
assertions, NSubstitute for collaborator fakes, `Avalonia.Headless`
for UI tests, `Verify` for snapshot tests on rendered chat
transcripts, and BenchmarkDotNet in `AgentDesktop.Bench` for
performance gates. CI fails on >10% regression vs. a committed
baseline (Principle IV).

**Rationale**:
- Standard, well-maintained stack; no exotic dependencies.
- Headless Avalonia + Verify lets us assert UI consistency
  (Principle III) without flakiness from real rendering.
- Benchmarks are checked in alongside the code that owns them so
  the perf budget is co-located with the implementation.

**Alternatives considered**:
- **NUnit / MSTest** — equivalent capability, but xUnit's
  per-test isolation matches our "no shared mutable test state"
  preference.

---

## R12. MVP module distribution — single module via Git submodule

**Decision**: The MVP bundles exactly one module,
`df-client-launchpad`, sourced from
`github.com/DigitalFoxAgency/df-client-launchpad` (`refactor`
branch) and brought in as a Git submodule at
`modules/df-client-launchpad/`. A thin `module.json` is authored
in *this* repo (alongside the submodule checkout, not committed
upstream) and references each existing `SKILL.md` by `sourcePath`.

**Rationale**:
- The launchpad already encodes its 17 skills as Claude-Code-style
  `SKILL.md` files with rich frontmatter and a strict `ORDER.md`.
  Re-authoring them as native module skills would duplicate the
  source of truth and immediately decay; pointing at the existing
  files keeps the launchpad's own constitution authoritative.
- Submodule pinning gives reproducible builds (the desktop-agent
  PR records the exact launchpad commit), trivial upstream upgrades
  (`git submodule update --remote`), and clean separation of
  concerns (launchpad evolves on its own cadence and CI).
- "One module at MVP" is a *scope* simplification, not an
  architectural one: the registry, scenario engine, dependency
  resolution, and policy engine remain multi-module by design and
  are exercised by tests using fakes. Adding a second module
  post-MVP requires no engine changes.
- The launchpad's own rules (§5 sequential ordering, §8 required
  verification records, §12–§15 human-only boundaries) are honoured
  by the platform's policy/scenario engines rather than re-implemented
  inside the launchpad — a clean adapter rather than a fork.

**Alternatives considered**:
- **Vendoring (copy-paste the files)** — fast to set up but loses
  upstream-tracking; rejected because the launchpad is actively
  developed on `refactor`.
- **Three separate modules (filesystem / github / frontend-angular-assistant)
  as originally specified** — rejected by user direction; the
  launchpad already covers the territory those placeholders were
  proxies for, and shipping three skeletal modules instead of one
  real one would slow the MVP without product value.
- **Splitting the launchpad into 17 modules (one per skill)** —
  rejected by user direction; the launchpad's cohesion (shared
  `config.ts`, shared `SESSION-LOG.md`, shared `ORDER.md`) is the
  point, and 17 modules would force every scenario to declare 17
  dependencies with no upside.
- **Pulling the launchpad at install time** instead of as a
  submodule — adds a network failure mode to the build and breaks
  air-gapped CI; rejected.

**Implications captured elsewhere**:
- `contracts/module.schema.json` extended to allow
  `skills[].sourcePath` (relative path inside the submodule
  checkout), `skills[].kind` (`automated` | `human`), and
  `skills[].verificationKey` (matches `[<skill>: verified]` in
  `SESSION-LOG.md`).
- `data-model.md` adds `Module.SourceKind` (`Bundled` | `Submodule`
  | `UserInstalled`) and `Skill.Kind` (`Automated` | `Human`).
- `plan.md` records the bundled-module + bundled-scenarios shape.
- `quickstart.md` adds `git submodule update --init --recursive` to
  the bootstrap.

---

## R11. CI gates

**Decision**: Single GitHub Actions workflow (`.github/workflows/ci.yml`)
running on Linux (ubuntu-latest), macOS (macos-latest), and Windows
(windows-latest) matrix legs. Each leg runs: restore → build (warnings
as errors) → unit + integration tests → coverage threshold check →
benchmark smoke (full benchmarks only on a dedicated leg / nightly).
Pre-release additionally signs and notarises the macOS bundle and
signs the Windows installer.

**Rationale**:
- Matches the constitution's quality gates (static analysis, tests,
  performance, accessibility) without forking workflows per
  feature.
- Benchmark cost is contained: PRs run a smoke set, nightly runs
  the full suite against the baseline.

**Alternatives considered**:
- **Self-hosted runners only** — rejected for MVP; revisit if
  benchmark variance on hosted runners exceeds 10%.
