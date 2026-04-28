# Feature Specification: Agency Workflow Platform (MVP)

**Feature Branch**: `001-agent-platform-mvp`
**Created**: 2026-04-26 · **Pivoted to web**: 2026-04-28
**Status**: Draft

**Input**: A multi-tenant web platform where marketing agencies run packaged AI workflows (starting with `df-client-launchpad`) end-to-end on a backend that hosts Claude Code per run. Agency staff with different roles (marketer, strategist, designer, engineer, media-buyer, admin) pick up phases from a role-based inbox, hold a chat conversation with Claude in their browser, and watch files appear in a live read-only file tree. The platform supplies the AI under the hood (no BYOK).

> The former desktop-MVP scope (Avalonia, single user, BYO model API token, local SQLite) is preserved on the `desktop` branch and is no longer the target.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Agency admin signs in and starts a client onboarding (Priority: P1)

An agency admin signs in to the web app, picks the **Onboard Client** workflow from their installed modules, supplies the client inputs (niche, city, name), and starts the run. The platform validates the agency's subscription and quota, creates the workflow run, and routes the first phase to whoever holds the required role. That user receives the phase in their inbox.

**Why this priority**: This is the smallest end-to-end slice that proves the multi-tenant, role-routed product exists. Without sign-in, run starting, and inbox routing, none of the per-phase work has a surface to plug into.

**Independent Test**: An admin signs in, picks `Onboard Client`, fills the form, clicks Start. The run appears under the agency's runs with status `In Progress`, and an item appears in the inbox of the user(s) who hold the role of phase `init`.

**Acceptance Scenarios**:

1. **Given** an admin with a valid subscription, **When** they pick a workflow and supply its inputs, **Then** a run is created and the first phase is assigned to a holder of the required role.
2. **Given** an admin whose subscription has expired or whose token quota is exhausted, **When** they try to start a workflow, **Then** the platform refuses and shows a clear recovery message (renew, upgrade, or contact billing).
3. **Given** a workflow whose first phase has no assignable holder for the required role, **When** the admin starts the run, **Then** the run starts in a `WaitingAssignment` state and the admin is prompted to assign a user.

---

### User Story 2 — A team member picks up their phase, chats with Claude, watches files appear (Priority: P1)

A user opens their inbox, sees a phase assigned to them, and clicks it. The platform spins up a backend run container that hosts a fresh Claude Code session against the run's persistent working directory. The user holds a chat conversation in the browser; alongside the chat, a read-only file tree updates live as Claude writes files. When Claude proposes a dangerous action (delete file, git push, install package, run shell), a confirmation prompt appears in the web UI; the action does not run unless the user approves.

**Why this priority**: This is the core of the product — the per-phase work surface. Everything else (inbox, hand-offs, audit) is plumbing around this loop.

**Independent Test**: With Story 1 working, the assigned user opens the inbox item, the chat surface loads with a streamed greeting from Claude, the user types a message and receives a streamed reply, and any files Claude creates in the working directory appear in the file tree within 2 seconds.

**Acceptance Scenarios**:

1. **Given** a user with an assigned phase, **When** they open it, **Then** a chat surface and a file-tree view load and Claude's first message streams in.
2. **Given** an active session, **When** the user sends a message, **Then** Claude's response streams into the chat in real time.
3. **Given** Claude writes or modifies files in the working directory, **When** the file tree polls/streams updates, **Then** new and changed files appear in the tree within 2 seconds.
4. **Given** Claude proposes a dangerous action, **When** the policy engine evaluates it, **Then** a confirmation prompt naming the action and target appears; nothing happens until the user decides.
5. **Given** the user closes the tab mid-session, **When** they re-open the phase, **Then** the run resumes against the same working directory; Claude starts a fresh session that picks up state from `CLAUDE.md`, `SESSION-LOG.md`, and the file tree.

---

### User Story 3 — Phase hand-off between roles (Priority: P1)

When a phase ends (the launchpad signals completion via its own verification key), the platform marks it done and assigns the next phase to whoever holds the next phase's required role. That user's inbox lights up. They open the new phase; a fresh Claude session starts against the **same persistent working directory** the previous role left behind. State carries through files, not chat history.

**Why this priority**: Multi-role hand-offs are the whole reason for the pivot. Without this, the product is just "Claude in a browser."

**Independent Test**: At the end of phase `init` (engineer), phase `pre-research` (marketer) appears in the marketer's inbox within 5 seconds. The marketer opens it; Claude in the new session can read the files `init` produced (e.g. `config.ts`, `01-pre-research/`).

**Acceptance Scenarios**:

1. **Given** a phase completes successfully, **When** the platform marks it done, **Then** the next phase is created, assigned per its declared role, and surfaces in the assignee's inbox within 5 seconds.
2. **Given** the next phase's role has multiple holders, **When** assignment runs, **Then** assignment follows the agency's configured policy (manual, primary holder, round-robin); MVP default is **primary holder**.
3. **Given** a hand-off occurs, **When** the new session starts, **Then** the same persistent working directory is mounted; no chat history is replayed; Claude reads the file system to orient.
4. **Given** an admin reassigns a pending phase to a different user, **When** the reassignment is saved, **Then** the original assignee's inbox item is removed and the new assignee's inbox is updated.

---

### User Story 4 — Confirm dangerous actions before they happen (Priority: P1)

When Claude (during any phase) is about to perform an action classified as dangerous — deleting files, pushing to a remote repository, installing packages, running shell commands — the web UI surfaces a confirmation prompt that names the exact action, the target, and the originating phase. Without confirmation, the action does not run. Confirmations are not implicit; each occurrence requires its own decision.

**Why this priority**: A platform that can touch real client repos and push to GitHub / deploy to Cloudflare is dangerous by default. Trust collapses the first time it does something destructive without permission.

**Independent Test**: During phase `site`, Claude attempts `npm install`. A confirmation appears in the web UI naming the package set and the working directory; declining records a skip in the audit log; confirming runs the command exactly once.

**Acceptance Scenarios**:

1. **Given** Claude proposes deleting a file, **When** policy evaluates it, **Then** a confirmation prompt naming the file appears before anything is deleted.
2. **Given** a confirmation prompt is showing, **When** the user declines, **Then** the action is skipped, an audit row is written, and Claude continues with the next safe step (or stops if blocked).
3. **Given** the user confirms a dangerous action, **When** it completes, **Then** the result (success or failure) is recorded in the audit log and surfaced in the chat.
4. **Given** a phase contains several dangerous steps, **When** it runs, **Then** each one requests its own confirmation; confirming one does not implicitly confirm the others.

---

### User Story 5 — Browse the module catalogue and pick a workflow (Priority: P3)

A user browses the catalogue of installed modules, sees the workflows each module exposes (name, description, declared inputs), and starts one directly without typing free-text into chat.

**Why this priority**: Direct invocation gives a discovery surface for what the platform can do today. Not required for first usable release; materially improves day-to-day usability.

**Acceptance Scenarios**:

1. **Given** modules are installed, **When** the user opens the catalogue, **Then** each module's id, version, name, description, and the workflows it exposes are visible.
2. **Given** a workflow requires inputs, **When** the user starts it, **Then** the app prompts for those inputs and refuses to start until they are supplied.

---

### Edge Cases

- A run container fails to start or crashes mid-session — the chat surface shows a clear "session unavailable" state and offers retry; the persistent volume is intact and a fresh container picks up where it left off.
- A user opens the same phase from two devices — the second device sees a "session in use" state and may take over (terminating the first device's connection) or wait.
- The host runs out of capacity (concurrent run cap or build-step cap reached) — the new session enters a `Waiting` state with a queue position; nothing fails.
- A module manifest references a dependency that is not installed — the module appears as `Unavailable` in the catalogue with the missing dependency named; its workflows cannot start.
- A workflow whose schema version is unknown — refused at load time with a clear error naming the offending file.
- The agency's GitHub App installation is missing or revoked — phases that need GitHub access (init, deploy) fail at attempt time with a recovery prompt directing the admin to reinstall the app.
- Subscription becomes invalid mid-run — in-flight phases finish; new phases cannot start; admin gets a recovery banner; existing data preserved.
- Working directory grows very large — the file-tree viewer pages and lazily loads file contents; only on-demand reads are streamed.
- Two confirmation prompts arrive in quick succession — they queue and present one at a time.
- Per-run cost cap exceeded — the run pauses; admin is notified and may raise the cap or terminate.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST be a browser-based web application; no desktop installation is required.
- **FR-002**: The platform MUST authenticate users with email + password via ASP.NET Core Identity. (External providers via OIDC are deferred.)
- **FR-003**: The platform MUST be multi-tenant: every user belongs to exactly one agency tenant; every row is scoped to that tenant.
- **FR-004**: The platform MUST supply the model API key under the hood; users MUST NOT need to configure their own model token. The platform MUST meter token usage per phase run.
- **FR-005**: The platform MUST present a chat surface that streams Claude's responses incrementally as they are produced.
- **FR-006**: The platform MUST present a read-only file-tree view of the run's working directory that updates within 2 seconds of file changes.
- **FR-007**: The platform MUST load module manifests from a well-known modules location at startup and on explicit refresh, validating each manifest's schema version before exposing it.
- **FR-008**: A module manifest MUST declare at minimum: id, version, name, description, dependencies, **workflows** (each with phases, each phase declaring required role + skill), MCP servers, prompts, and policies.
- **FR-009**: The platform MUST treat each module as **self-orchestrating**: the platform hosts Claude Code with the module's working directory mounted; the module's own pipeline (`ORDER.md`, skills, `SESSION-LOG.md`) drives step order. The platform tracks **current phase**, **assignee**, and **status** only.
- **FR-010**: When a phase becomes ready, the platform MUST create an inbox item for the user(s) holding the required role. Default assignment policy is **primary holder**; alternative policies (round-robin, manual) are configurable per agency post-MVP.
- **FR-011**: Opening a phase MUST spin up a per-run container that hosts a fresh Claude Code session, mounts the run's persistent working directory, and exposes a bridge that proxies chat I/O and file-system events to the web client over WebSocket.
- **FR-012**: The platform MUST evaluate every Claude-initiated action through a policy engine and MUST require explicit user confirmation for any action classified as dangerous, including but not limited to: deleting files, pushing to a remote repository, installing packages, and executing arbitrary shell commands.
- **FR-013**: A dangerous action MUST NOT execute unless the user has confirmed it for that specific occurrence; prior confirmations MUST NOT carry over implicitly.
- **FR-014**: Confirmation prompts MUST name the action, the target, and the originating phase in language the user can understand without reading source code.
- **FR-015**: The platform MUST log every dangerous action — proposed, confirmed, declined, executed, succeeded, failed — to an audit log, scoped to tenant.
- **FR-016**: The platform MUST cap concurrent **build-class** steps (`InstallPackage`, large `RunShell` like `astro build`) at **2 across the host**, regardless of how many sessions are open. Phases blocked by the cap enter a `Waiting` state with a queue position visible in the UI.
- **FR-017**: The persistent working directory of a run MUST survive across phase hand-offs; all state visible to subsequent phases MUST be reachable through the file system. The platform MUST NOT replay chat history into new sessions.
- **FR-018**: Module + workflow definitions MUST be schema-versioned; the platform MUST refuse to load definitions whose declared schema version it does not support.
- **FR-019**: The platform MUST be runnable end-to-end in a test mode that does not require a real Claude Code installation, so automated tests can exercise the chat, workflow, and policy surfaces deterministically. A fake bridge / fake Claude responder MUST be provided.
- **FR-020**: The platform MUST ship with one bundled module: `df-client-launchpad`. The module's manifest declares the workflows the platform exposes; the module's internal pipeline is the module's concern and is NOT replicated at the platform level.
- **FR-021**: The bundled module MUST declare three workflows: `onboard-client`, `launch-ads`, `monthly-report` (definitions inherit from the existing operations of the same names).
- **FR-022**: When a subscription becomes invalid (expired, revoked, or unreachable beyond a configurable grace window), the platform MUST disable starting new runs and MUST surface a clear recovery path; in-flight runs MUST be allowed to finish.
- **FR-023**: The platform MUST meter input tokens, output tokens, cache-read tokens, and cache-write tokens per phase run, attributing each row to its tenant and run.
- **FR-024**: The platform MUST enforce a per-run hard token cost cap; runs that exceed the cap MUST pause and notify the agency admin; nothing further executes until the cap is raised or the run terminated.
- **FR-025**: Agency third-party credentials (GitHub App installation id, Cloudflare API token, Resend, Telegram, GA4, Drive, npm, etc.) MUST live in a secrets vault indexed by tenant, MUST NOT be returned to the web client, and MUST be materialised into run containers as environment variables / config files at container start.
- **FR-026**: The platform MUST integrate with GitHub via a GitHub App, installed per agency on the agency's GitHub organisation. The installation token MUST be minted on demand and MUST NOT be persisted.

### Key Entities

- **Tenant** — An agency. Has a subscription plan, a token budget, and a set of installed modules.
- **User** — A member of a tenant. Has email + password credentials and one or more roles.
- **Role** — A named function inside a tenant: `admin`, `marketer`, `strategist`, `designer`, `engineer`, `media-buyer`. Phases declare the role they require.
- **Module** — An agency-installable product (e.g. `df-client-launchpad`). Versioned; loaded from disk; declares workflows.
- **Workflow Definition** — A graph of phases inside a module. Each workflow has an id, name, description, declared inputs, and an ordered set of phases.
- **Phase Definition** — A single assignable unit. Declares: id, required role, skill reference (into the module), kind (`automated` / `human`), inputs from prior phases (advisory), outputs.
- **Workflow Run** — Live execution of a workflow for a tenant. Has a tenant, the workflow definition pinned by version, supplied inputs, status (`InProgress`, `WaitingAssignment`, `WaitingHumanInput`, `Paused`, `Completed`, `Cancelled`, `Failed`), and a persistent working directory.
- **Phase Run** — Live execution of a phase inside a workflow run. Has assignee, status, started/completed timestamps, and references to artefacts produced (file paths in the working dir, repository commits, etc.).
- **Inbox Item** — A pointer to a phase run that requires the assignee's attention. Has a kind, a state (`Unread`, `InProgress`, `Done`), and timestamps.
- **Confirmation Request** — A pending dangerous-action approval emitted by Claude and surfaced in the web UI. Has the proposed action, classification, decision, decider, and timestamps.
- **Audit Entry** — An immutable record of a workflow event (run started/completed, phase assigned/started/completed, dangerous action proposed/confirmed/declined/executed). Scoped to tenant.
- **Usage Ledger Entry** — Token-usage record per phase run: input/output/cache-read/cache-write tokens, model id, estimated cost, occurred-at.
- **Vault Secret Reference** — A pointer to a secret stored in the vault, indexed by tenant and kind. The platform never persists the secret material itself.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can sign in, start a workflow, and the assigned user can open the chat surface for the first phase in **under 5 minutes**.
- **SC-002**: 95% of chat messages begin streaming a reply within **2 seconds** of being sent on a typical broadband connection.
- **SC-003**: 100% of dangerous actions surface a confirmation prompt before any side effect, verified by automated tests covering every action class in FR-012.
- **SC-004**: 0 dangerous actions execute without an associated, user-attributable confirmation record in the audit log.
- **SC-005**: When a phase completes, the next phase appears in the next assignee's inbox within **5 seconds**.
- **SC-006**: Files written to the working directory during a session appear in the web file tree within **2 seconds**.
- **SC-007**: The bundled `onboard-client` workflow runs end-to-end on a representative client brief, with hand-offs across at least 3 distinct roles, all dangerous actions confirmed through the web UI, and a final commit pushed to the client's GitHub repo.
- **SC-008**: The host (2 cores / 8 GB) sustains **3 concurrent idle runs** without degradation; **2 concurrent build-class steps** without OOM (with the build-step cap enforced); 4th and beyond enter the `Waiting` state cleanly.
- **SC-009**: The full automated test suite passes without a real Claude Code installation, demonstrating that the bridge abstraction is honoured.
- **SC-010**: A module manifest with an unknown schema version is refused at load time in 100% of cases with a clear error naming the offending file.
- **SC-011**: 100% of runs that exceed the per-run token cost cap pause; 0 runs silently overspend.

## Assumptions

- The platform is a multi-tenant web SaaS deployed initially to a customer-controlled Linux VPS (Ubuntu 22.04+, 2 cores, 8 GB RAM, plus 4–8 GB swap). Multi-region, multi-host, and elastic auto-scaling are out of scope for the MVP.
- Concurrent workflow runs at MVP volume are expected to be **≤5**; higher volumes require a host upgrade or migration to elastic container exec (Fly Machines, Fargate, etc.) — feasible without rearchitecting because run execution is behind a container abstraction.
- The platform supplies the model API key (Anthropic) under the hood; agencies pay a subscription that bundles AI cost. Per-tenant metering, per-run cost caps, and prompt caching are required to make this economic.
- Each module owns its own internal pipeline. The platform NEVER reaches inside a module to drive its steps. The launchpad's `ORDER.md`, `SESSION-LOG.md`, and per-skill `SKILL.md` files remain the source of truth for what happens during a phase. This is non-negotiable for the MVP.
- "Dangerous" is defined by the policies declared in module manifests plus a baseline hard-coded in the policy engine (delete files, git push, install packages, run shell). Broader policy authoring is out of scope for the MVP.
- Each new phase session is a **fresh `claude` process**; chat history is **not** replayed. State carries between phases via the persistent working directory only. The launchpad's existing convention (`CLAUDE.md`, `SESSION-LOG.md`, verification keys) is the orientation mechanism.
- Agencies install a single platform-wide GitHub App on their GitHub organisation. The App's installation token is minted per worker invocation and never persisted.
- Email is used for sign-in only; transactional notifications (inbox, hand-off pings) are deferred past the MVP and surfaced in-app only.
- Mobile, native clients, marketplace for third-party modules, and a public partner API are deferred past the MVP.
- Telemetry, crash reporting, and remote diagnostics are deferred past the MVP; observability in the MVP is server-local only (logs + Postgres).
