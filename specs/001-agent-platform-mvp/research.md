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

## R5. Operations declared in module manifest (replaces former platform-side scenarios)

**Decision**: A module exposes user-facing entry points as
**operations** declared inside its `module.json` (under an
`operations[]` array). The platform uses these for catalogue
display and intent routing; it does NOT replicate or override the
module's internal pipeline. Platform-side scenario YAML/JSON files
are explicitly NOT a thing.

**Rationale**:
- Operations live where their semantics live — inside the module.
  Module updates change `module.json` and the change reaches the
  platform on next refresh; there is no platform-side YAML to
  drift.
- A single source of truth (the module) eliminates the failure
  mode where the platform's scenario file knows things the
  module's own constitution disagrees with.
- Module authors decide what the platform can call and what stays
  internal. The launchpad declares three platform-visible
  operations (`onboard-client`, `launch-ads`, `monthly-report`)
  while keeping its 17 internal `SKILL.md` steps as
  implementation detail.

**Alternatives considered**:
- **YAML scenarios in `scenarios/` driven by a platform scenario
  engine** — original design; rejected. See R18.
- **JSON manifest only with skills, no operations** — would force
  the platform to either (a) invoke skills individually
  (re-introducing the rejected scenario engine) or (b) guess which
  skills are user-facing. Better to have modules declare
  operations explicitly.

---

## R18. Module-owned orchestration — non-negotiable architectural model

**Decision**: The platform delegates **whole operations** to
modules and observes a stream of events the module emits. The
platform does NOT step through a module's internal pipeline. Each
module is a self-contained agent that runs its own work; the
platform's role is chat-as-bridge plus safety brokerage
(confirmations + audit) plus identity/secrets.

**Operating model**:
1. User states an intent in chat (free text, or a chosen operation
   from the catalogue).
2. The platform routes the intent to a module + operation pair.
3. The platform calls
   `IRuntimeManager.DelegateAsync(moduleId, operationId, inputs,
   conversationId, callbacks, ct)` — handing the job to the
   module.
4. The module runs its own internal pipeline (its `ORDER.md`,
   its skills, its `SESSION-LOG.md` verification, possibly its
   own subagents). The platform does NOT see or influence those
   steps directly.
5. The module emits events back through the supplied callbacks:
   - **Progress text** → chat surface.
   - **Confirmation requests** for dangerous actions → platform
     policy engine → user prompt → answer back to module.
   - **Human-handoff requests** for human-only steps → handoff
     dialog → user marks done → answer back to module.
6. The module returns a final result (success / failure) when its
   internal pipeline finishes.
7. If the user cancels, the platform signals cancellation; the
   module finalises in-flight work and stops; the platform
   records the cancellation in chat history.

**Rationale**:
- A platform-side scenario engine that drives a module's
  individual steps **duplicates the module's own constitution**
  and creates two sources of truth for "what runs in what order".
  When they diverge — and they will — the user's experience
  depends on whichever wins, and the module author has no good
  way to fix it.
- Self-contained modules align with how OpenClaw thinks about
  agents: each module *is* an agent (or a small graph of
  subagents the module spawns); the platform doesn't tell agents
  how to do their work.
- Safety stays with the platform precisely because a malicious or
  buggy module shouldn't be trusted to gate its own dangerous
  actions or write its own audit log. The callback pattern keeps
  the policy engine and audit log on the platform side while
  letting the module own the rest.

**Alternatives considered**:
- **Platform-driven scenario engine (the original spec)** —
  rejected; see above and `spec.md` Story 2 rewrite.
- **Module as a passive library of skills the platform sequences** —
  same problem as scenarios; rejected.
- **Module emits a one-shot result with no progress events** —
  unworkable for long-running operations like client onboarding,
  which take hours and need human input mid-flight.

**Consequences captured elsewhere**:
- `spec.md` FR-006 through FR-011, FR-020/FR-021, Story 2,
  Story 4, edge cases, key entities — all rewritten.
- `data-model.md` — Scenario aggregate and ScenarioStep types
  removed; Operation type added under Modules section;
  Delegation runtime concept added.
- `contracts/IRuntimeManager.md` — rewritten:
  `DelegateAsync(moduleId, operationId, inputs, conversationId,
  callbacks, ct)` replaces `InvokeSkillAsync` as the primary
  surface. `InvokeSkillAsync` retained only for advanced direct
  skill invocation (US4 power-user path).
- `contracts/IScenarioRunner.md` — DELETED.
- `contracts/scenario.schema.json` — DELETED.
- `contracts/module.schema.json` — gains `operations[]` array;
  `skills[]` retained but documented as internal/optional.
- `modules/df-client-launchpad/module.json` — gains
  `operations[]`.
- `scenarios/` directory — DELETED.
- `tasks.md` Phase 5 — reshaped: scenario-engine tasks deleted,
  delegation-runner + operation-registry tasks added.

**When to revisit**:
- A future module legitimately wants the platform to compose
  across modules (run module-A's operation X, feed its output
  into module-B's operation Y). At that point we re-introduce
  composition — but as **a higher-level "playbook" that chains
  whole operations**, not a step-level scenario. The composition
  unit stays "operation", never "skill".

### Extensibility surface (what fits and what strains the model)

The model is intentionally thin. The platform's job is
chat-bridge + safety + audit + identity + runtime lifecycle.
Almost any future extension you can think of either **wraps a
delegation** or **extends a policy / contract** — both are free.
What is forbidden is **piercing into a module's internals**.

**The invariant (one rule, non-negotiable)**:

> The unit of composition is **operation**. The platform never
> reaches inside an operation.

**Extensions that fit the model** (layer above or around
delegation; module is untouched):

- Pre-delegation input validation (schema, business rules,
  security checks) inside `DelegationRunner` before
  `IRuntimeManager.DelegateAsync`.
- Output validation against the operation's declared output
  schema after `DelegateAsync` returns.
- Retries / timeouts / rate limits as middleware around
  `DelegationRunner`.
- Caching expensive operations when inputs match a recent run
  (opt-in via operation metadata).
- Audit / metrics / structured logging — already part of the
  platform, extends naturally.
- Stricter or user-customised policy rules (e.g. "always
  confirm spends >€10") via `IPolicyEngine`; the policy engine
  still gates the existing `IDelegationCallbacks.RequestConfirmationAsync`
  channel.
- Cross-cutting approvals ("this delegation needs manager
  sign-off") as a new gate around `DelegationRunner` or a
  policy-engine extension.
- Routing fine-tuning (top-level agent prompt nudges,
  tool-priority hints) — the agent's prompt and tool surface
  are platform-controlled.
- Concurrent-delegation limits, budget metering, "dry run"
  modes — all live in `DelegationRunner` or as opt-in
  operation parameters.

**Extensions that strain the model** (signals that the
operation contract is too coarse, NOT that the model is
wrong):

1. *Wanting to inspect a module's internal step output.*
   Example: "Validate the strategy.md the launchpad generates
   between strategy and site." If you need this, the
   launchpad's `onboard-client` is too coarse — it should
   split into `generate-strategy` (returns `strategyMd`) and
   `build-from-strategy` (takes `strategyMd`). Two operations,
   validation between them, model intact.
2. *Wanting to skip / reorder / replace a module's internal
   step.* Example: "Run the launchpad's onboarding but skip
   pre-research." The fix is in the **module**: either accept
   a `skipPreResearch` input, or expose a separate operation
   that omits it. The platform never decides which internal
   step runs.

**Rule of thumb**: when you feel the urge to reach inside a
module, the move is to make the module **expose a
finer-grained operation**, not to **bypass the module**.

**One genuine future extension that fits** — multi-operation
playbooks (see "When to revisit" above). Playbooks compose
operations the same way operations compose internal steps:
each level only knows about the next level down.

```
playbook → operation → (module-internal: skill → step → … )
   ↑           ↑                      ↑
 user-       platform                module
 facing      delegates                owns
                                      this
```

The platform delegates operations, never skills. A playbook
engine would compose operations, never their internals. The
invariant scales without modification.

### Top-level agent — router only (refinement)

The chat surface is owned by a single OpenClaw-driven agent
(the **top-level agent**). On every user message it runs a
chat turn. Its tool surface is intentionally minimal:

- `list_modules()` — return the loaded modules + their
  declared operations (with names, descriptions, declared
  inputs).
- `delegate_operation(moduleId, operationId, inputs)` — start
  a delegation. **Inputs may be partial.** The module accepts
  what it gets and asks the user via callback for whatever it
  still needs.

The top-level agent has **no `ask_user` tool**. Clarification
is the **module's** job during delegation, not the agent's
before delegation. If a user's message is ambiguous, the
agent commits to a delegation (its best guess) and lets the
module ask interactively.

If the user's message doesn't match any operation, the agent
just replies in plain text — no tool call, no delegation.

**Chat routing during an active delegation**: while a
delegation has a pending `IDelegationCallbacks.AskUserAsync`,
the next user chat message is routed **as the answer to that
question**, not as a new chat turn. New chat turns only start
when no delegation question is pending. Cancellation
mid-delegation is a UI affordance (Cancel button on the
operation panel), not something the user types in chat.

**Per-module customisation** is intentionally absent at MVP:
the top-level agent learns about modules entirely through the
registry. Module-supplied prompt fragments and additional
agent-level tools beyond `delegate_operation` are deferred —
revisit if/when concrete modules cannot be reasonably routed
without them.

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

## R16. Architectural style — Layered for MVP, modular monolith deferred

**Decision**: For the MVP, keep the simple **layered Clean
Architecture** (`Domain → Application → Infrastructure / Desktop /
Api`) already in `plan.md`. **Defer** migration to a modular
monolith with bounded-context subsystems until at least one of the
"when to revisit" triggers below fires.

**Rationale for deferring**:
- At MVP scale (~5 source projects, ~80 LoC per repository, single
  developer, single bundled module) the layered structure already
  enforces the boundaries that matter most: UI cannot reach the
  database, infrastructure cannot leak into the domain, and every
  collaborator is behind an interface.
- Modular-monolith boundaries pay off when (a) multiple developers
  are touching the same code daily, (b) cross-context coupling
  starts appearing in PRs, or (c) a subsystem needs to be
  extracted to a separate process. None of these are MVP problems.
- The migration cost from layered → modular at our scale is
  estimated at **1–2 days of mechanical refactor** — not a
  one-way door. Adding it upfront costs ~1 day of setup *plus*
  ongoing discipline overhead (analyzer, banned-symbols files,
  `internal` reviews) that buys nothing until the codebase gets
  larger.
- The interface-driven plan (`IChatService`, `IModuleRegistry`,
  `IPolicyEngine`, `IRuntimeManager`, `ISecretStore`,
  `ISubscriptionGate`) keeps the migration cheap by ensuring no
  code outside Infrastructure ever touches a concrete adapter.

**Terminology decision (kept regardless of migration timing)**:
- The product concept is **"Module"** (`df-client-launchpad`).
- If/when a modular-monolith migration happens, the architectural
  unit will be called a **"Subsystem"** to avoid colliding with
  the product term. This decision is recorded now so the
  vocabulary is settled before any future migration starts.

**Alternatives considered**:
- **Modular monolith (lite — one project per subsystem with
  `Contracts/` boundary)** — fully designed in
  `docs/architecture/subsystems-future.md`, including subsystem
  cuts, reference matrix, analyzer setup, and migration playbook.
  Rejected for MVP timing only; this is the documented target for
  post-MVP if/when warranted.
- **Modular monolith (heavy — four projects per subsystem)** —
  same rejection plus a project-explosion penalty (~28 projects)
  that buys no enforcement we don't already get from `internal`
  + analyzer in the lite layout.
- **Vertical-slice / feature-folder monolith without explicit
  bounded contexts** — boundary creep is famously hard to
  recover from; rejected as a non-target even for MVP.

**When to revisit (triggers for migrating to modular monolith)**:
- A second developer joins and friction appears at the
  application-layer seams (typical signal: PRs touching the same
  files repeatedly).
- A subsystem grows to the point where reviewers cannot hold its
  internals in their head, *and* its scope is clearly separable
  from the rest.
- A subsystem needs out-of-process extraction (e.g. `Runtime`
  for hard isolation) — see the migration playbook in
  `docs/architecture/subsystems-future.md`.
- Modules start declaring policies that materially diverge across
  the platform's bounded contexts (a sign that "Modules" and
  "Policies" want stronger isolation).

**Implications for current plan**:
- `plan.md` Project Structure stays at the original five-project
  layered tree.
- `plan.md` Scale/Scope stays at "~5 source projects + ~6 test
  projects".
- `tasks.md` does not change — Phase 1/Phase 2 task paths target
  the layered tree.
- `docs/architecture/subsystems-future.md` is committed alongside
  this entry as ready-to-execute homework for the future
  migration. It is **not** a description of current state.

---

## R15. Persistence library reaffirmed — Dapper (over EF Core 9)

**Decision**: Keep Dapper as the data-access library for SQLite,
with hand-written numbered SQL migrations (`0001_init.sql`, …)
applied by a tiny custom runner. EF Core 9 was reconsidered and
explicitly rejected for the MVP.

**Rationale**:
- The MVP schema is four small append-only tables with six total
  query shapes across the whole codebase (insert conversation,
  insert message, list conversations, get messages by conversation,
  insert policy decision, insert audit event). No updates, no
  deletes, no relationships beyond a single foreign key.
- The team is unfamiliar with both Dapper and EF Core 9. Given
  unfamiliarity is symmetrical, the smaller surface area wins:
  Dapper's API is ~3 concepts (`IDbConnection`, `QueryAsync<T>`,
  parameter binding); EF Core 9's productive subset is ~10
  (DbContext lifetime, change tracking, `AsNoTracking`, migrations,
  compiled models, factory pattern, owned types, query splitting,
  transactions, `IDbContextFactory`).
- Failure modes are more visible with Dapper: the SQL written is
  the SQL that runs. EF Core's productivity comes with hidden
  performance footguns (N+1, lazy loading on the UI thread,
  change-tracking memory bloat) that a beginner can ship without
  noticing.
- Dapper aligns the team's learning with a transferable skill
  (SQL) rather than a framework (EF Core). This pays off across
  any future stack.
- The `IChatRepository` and `IAuditLog` interfaces in
  `AgentDesktop.Application` are the escape hatch: if the schema
  ever grows joins or reporting needs that benefit from LINQ, EF
  Core can replace Dapper inside `AgentDesktop.Infrastructure`
  without touching any other project.

**Alternatives considered (this re-evaluation)**:
- **EF Core 9** — more familiar to the broader .NET community and
  productive once learned, but materially more to learn correctly
  for a 4-table append-only schema. Cold-start budget is met
  either way; the deciding factor was learning surface area, not
  performance.
- **Raw `Microsoft.Data.Sqlite` (no ORM at all)** — viable, but
  ~50% more boilerplate per repository than Dapper for no
  meaningful gain. Reserved as a fallback for any future hot path
  that profiles slow.

**When to revisit**:
- A query needs more than one join.
- LINQ-over-the-database becomes preferable to SQL-as-strings.
- An admin/reporting surface is added that benefits from EF's
  query composition.

**Implications captured elsewhere**: none — `plan.md` and
`tasks.md` already specify Dapper. This entry exists so the
decision (and the explicit reconsideration) is in the historical
record and the team does not re-litigate it without context.

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
