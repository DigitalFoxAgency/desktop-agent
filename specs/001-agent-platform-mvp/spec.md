# Feature Specification: Desktop AI Agent Platform (MVP)

**Feature Branch**: `001-agent-platform-mvp`
**Created**: 2026-04-26
**Status**: Draft
**Input**: User description: "Cross-platform desktop AI agent platform with module/scenario registries, policy engine, and a sandboxed local agent runtime. MVP scope includes desktop shell, live chat, local chat history, three initial modules (filesystem, github, frontend-angular-assistant), three initial scenarios (review-pr, fix-angular-bug, generate-tests), and a confirmation gate for dangerous actions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in, configure model token, and chat with the agent (Priority: P1)

A subscriber installs the desktop app, signs in with their subscription
account, supplies their own model API token, and immediately holds a
live chat conversation with the agent. The conversation is preserved
locally so the user can return to it after restarting the app.

**Why this priority**: This is the smallest end-to-end slice that proves
the product exists. Without a working chat backed by the user's own
model token and a persisted history, none of the higher-value features
(modules, scenarios, policies) have a surface to plug into.

**Independent Test**: A fresh installer is launched on a supported OS;
the user signs in with a valid subscription account, pastes a model
API token, asks the agent a question, receives a streamed reply, closes
the app, reopens it, and sees the prior conversation in the history
list.

**Acceptance Scenarios**:

1. **Given** a fresh install on a supported OS, **When** the user enters
   valid subscription credentials and a valid model API token, **Then**
   the chat surface becomes available and the user can send a message.
2. **Given** a signed-in user with a valid token, **When** the user
   sends a message, **Then** the agent's response streams into the chat
   surface in real time.
3. **Given** an active conversation, **When** the user closes and
   reopens the app, **Then** the conversation appears in the history
   list with the same messages in the same order.
4. **Given** an invalid or expired subscription, **When** the user
   attempts to sign in, **Then** the app refuses and shows a clear
   recovery message.

---

### User Story 2 - Run a multi-step scenario that composes module skills (Priority: P2)

A subscriber selects the "Onboard Client" scenario, supplies the
required inputs (niche, city, client name), and the agent executes
the scenario by chaining the bundled module's skills in the order
defined by the module — initialising the client workspace, running
pre-research, holding the Discovery brief, generating strategy,
building the site, wiring integrations, and deploying — while the
user follows progress in the chat surface and approves each
human-in-the-loop step.

**Why this priority**: Scenarios are the product's differentiated
value: pre-composed, repeatable workflows that turn the chat surface
into a reliable assistant for concrete tasks. Without them the product
is "just another chat client".

**Independent Test**: With Story 1 working, the user picks the
"onboard-client" scenario from the catalogue, enters the required
inputs, and observes the scenario step through each module skill,
ending with a deployed client site and a recorded session log.

**Acceptance Scenarios**:

1. **Given** a signed-in user with the required modules installed,
   **When** the user launches the "Review Pull Request" scenario,
   **Then** the scenario executes its declared steps in order and
   surfaces progress for each step.
2. **Given** a scenario step fails, **When** the failure is detected,
   **Then** the scenario halts, the failing step and error are shown,
   and the user is offered retry or cancel.
3. **Given** the user cancels a running scenario, **When** they
   confirm, **Then** in-flight work stops, no further steps execute,
   and the cancellation is recorded in the chat history.

---

### User Story 3 - Confirm dangerous actions before they happen (Priority: P1)

When the agent (or a scenario) is about to perform an action classified
as dangerous — deleting files, pushing to a remote repository,
installing packages, or running shell commands — the app pauses and
asks the user to explicitly confirm before proceeding. Without
confirmation the action does not run.

**Why this priority**: A desktop agent that can touch the filesystem
and execute commands is dangerous by default. Trust in the product
collapses the first time it deletes work or pushes a bad commit
without permission, so the confirmation gate is required for the very
first release.

**Independent Test**: The user issues a request that requires the
agent to delete a file or push to a remote; the app surfaces a
confirmation prompt naming the exact action and target; declining
prevents the action and the chat shows the action was skipped;
confirming runs the action exactly once.

**Acceptance Scenarios**:

1. **Given** the agent intends to delete a file, **When** the policy
   engine evaluates the action, **Then** the user sees a confirmation
   prompt that names the file and the operation before anything is
   deleted.
2. **Given** a confirmation prompt is showing, **When** the user
   declines, **Then** the action is skipped, the chat records the
   skip, and the agent continues with the next safe step (or stops if
   the action was required).
3. **Given** the user confirms a dangerous action, **When** the action
   completes, **Then** the result (success or failure) is recorded in
   the chat and the agent proceeds.
4. **Given** a scenario contains several dangerous steps, **When** it
   runs, **Then** each dangerous step requests its own confirmation;
   confirming one does not implicitly confirm the others.

---

### User Story 4 - Discover and run individual module skills (Priority: P3)

A subscriber browses the catalogue of installed modules, sees what
skills each module exposes, and invokes a single skill directly from
the chat without going through a full scenario.

**Why this priority**: Direct skill invocation gives power users a
faster path for one-off tasks and is also how new scenarios are
prototyped. It is not required for the very first usable release but
materially increases day-to-day value.

**Independent Test**: The user opens the module catalogue, picks the
filesystem module's "list directory" skill, supplies a path, and sees
the listing rendered in the chat surface.

**Acceptance Scenarios**:

1. **Given** modules are installed, **When** the user opens the module
   catalogue, **Then** each module's id, version, name, description,
   and skills are visible.
2. **Given** a skill requires inputs, **When** the user runs it,
   **Then** the app prompts for those inputs and refuses to run until
   they are supplied.

---

### Edge Cases

- The user enters a model API token that is syntactically valid but
  rejected by the model provider — the app surfaces the provider's
  error verbatim and keeps the user signed in so they can correct the
  token.
- The user loses network connectivity mid-conversation — the in-flight
  message fails with a clear error, prior history remains intact, and
  the next attempt succeeds once connectivity returns.
- A module manifest references a dependency that is not installed —
  the module is shown in the catalogue as "unavailable" with the
  missing dependency named; scenarios that need it cannot start.
- A scenario references a skill that no longer exists in the latest
  version of a module — the scenario is shown as "incompatible" with
  the offending step highlighted; the scenario cannot start until
  resolved.
- The local agent runtime is not installed or fails to start — the
  chat surface shows a clear "runtime unavailable" state and offers a
  retry; no skill or scenario can run until the runtime is healthy.
- Two confirmation prompts arrive in quick succession — they are
  queued and presented one at a time so the user always knows which
  action they are approving.
- The local history database is corrupted on startup — the app starts
  with an empty history, surfaces a non-blocking warning, and offers
  to export or discard the corrupted file rather than failing to
  launch.
- The user signs out — the local chat history remains on disk but is
  inaccessible until a user signs back in on the same machine.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST run as an installable desktop application
  on Windows, macOS, and Linux from a single codebase.
- **FR-002**: The app MUST require the user to sign in with a
  subscription account before any agent capability is available.
- **FR-003**: The app MUST allow the user to supply, update, and
  remove their own model API token, and MUST persist that token using
  the operating system's secure credential storage where available.
- **FR-004**: The app MUST present a live chat surface that streams
  agent responses incrementally as they are produced.
- **FR-005**: The app MUST persist chat history locally so that prior
  conversations are available across restarts of the app and the
  machine.
- **FR-006**: The app MUST load module definitions from a well-known
  modules location at startup and on explicit refresh, validating each
  module's manifest before exposing it.
- **FR-007**: A module manifest MUST declare at minimum: id, version,
  name, description, dependencies, skills, MCP servers, prompts, and
  policies.
- **FR-008**: The app MUST load scenario definitions from a well-known
  scenarios location, validate them, and expose only those whose
  required modules and skills are present and compatible.
- **FR-009**: A scenario definition MUST declare an ordered sequence
  of steps, each step naming a module and a skill from that module.
- **FR-010**: The app MUST execute scenarios step-by-step, surfacing
  progress, intermediate results, and errors for each step in the
  chat surface.
- **FR-011**: The user MUST be able to cancel a running scenario at
  any time; cancellation MUST stop further steps and record the
  outcome in the chat history.
- **FR-012**: The app MUST evaluate every agent-initiated action
  through a policy engine and MUST require explicit user confirmation
  for any action classified as dangerous, including but not limited
  to: deleting files, pushing to a remote repository, installing
  packages, and executing arbitrary shell commands.
- **FR-013**: A dangerous action MUST NOT execute unless the user has
  confirmed it for that specific occurrence; prior confirmations MUST
  NOT carry over implicitly to subsequent actions.
- **FR-014**: Confirmation prompts MUST name the action, the target,
  and the originating module or scenario step in language the user
  can understand without reading source code.
- **FR-015**: The app MUST manage the lifecycle of the local agent
  runtime (install, start, health-check, stop) without requiring the
  user to interact with runtime tooling directly.
- **FR-016**: The chat surface MUST NOT communicate with the local
  agent runtime directly; all interaction MUST flow through the
  application's own services.
- **FR-017**: All agent-initiated activity that touches the user's
  machine MUST run inside the sandboxed runtime layer; activity that
  bypasses the sandbox MUST be refused.
- **FR-018**: Module and scenario definitions MUST be versioned, and
  the app MUST refuse to load definitions whose declared schema
  version it does not support.
- **FR-019**: The app MUST be runnable end-to-end in a test mode that
  does not require the real local agent runtime to be installed, so
  that automated tests can exercise the chat, module, scenario, and
  policy surfaces deterministically.
- **FR-020**: The app MUST ship with one bundled module out of the
  box: `df-client-launchpad`. The module exposes the full set of
  Digital Fox client-onboarding skills (init, pre-research,
  keywords, brief, research, semantics, strategy, strategy-pdf,
  offer, architecture, design, site, integrations, seo, deploy,
  ads, reporting).
- **FR-021**: The app MUST ship with three bundled scenarios out
  of the box, each composing skills of the bundled module:
  `onboard-client` (Phase 1, the full onboarding pipeline in
  `ORDER.md` order), `launch-ads` (Phase 2 ads kickoff),
  `monthly-report` (Phase 2 reporting).
- **FR-022**: The app MUST log every dangerous action — proposed,
  confirmed, declined, executed, succeeded, failed — in a form the
  user can review after the fact.
- **FR-023**: When a subscription becomes invalid (expired, revoked,
  unreachable for longer than the offline grace window), the app
  MUST disable agent capabilities and surface a clear recovery path,
  while preserving local chat history.

### Key Entities

- **User Account**: The subscriber identity used to authenticate. Has
  a subscription status and a set of stored credentials (subscription
  session, model API token).
- **Conversation**: An ordered sequence of messages between the user
  and the agent, persisted locally. Has an id, a title, a created
  timestamp, and a last-activity timestamp.
- **Message**: A single utterance in a conversation. Has an author
  (user or agent), a body, a timestamp, and optional references to
  the scenario step or skill invocation that produced it.
- **Module**: A versioned package of agent capabilities. Has an id,
  version, name, description, dependencies, skills, MCP servers,
  prompts, and policies.
- **Skill**: A named capability exposed by a module. Has an id within
  its module, a description, declared inputs, declared outputs, and a
  policy classification (safe vs dangerous).
- **Scenario**: A versioned, ordered workflow that composes skills
  across modules. Has an id, version, name, description, declared
  inputs, and an ordered list of steps; each step names the module
  and skill it invokes and how its inputs map from prior steps.
- **Policy Decision**: A record produced by the policy engine for a
  proposed action. Has the proposed action, its classification, the
  user's response (confirmed/declined/skipped), and a timestamp.
- **Runtime Status**: The current health of the local agent runtime
  (not installed, installing, starting, ready, degraded, stopped),
  surfaced to the UI without exposing runtime internals.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can install the app, sign in, supply a model
  API token, and receive a streamed reply to their first message in
  under 5 minutes on a supported OS.
- **SC-002**: 95% of chat messages begin streaming a reply within 2
  seconds of being sent, on a typical broadband connection.
- **SC-003**: 100% of dangerous actions surface a confirmation prompt
  before any side effect, verified by automated tests covering every
  action class listed in FR-012.
- **SC-004**: 0 dangerous actions execute without an associated,
  user-attributable confirmation record in the audit log.
- **SC-005**: The "Onboard Client" scenario runs end-to-end on a
  representative client brief, halting at each declared
  human-in-the-loop step (Discovery call, content approvals,
  dashboard work) with a clearly-named hand-off prompt, and resumes
  to completion once the human marks the step done.
- **SC-006**: Reopening the app restores the most recent
  conversation, in full and in order, in under 2 seconds on the
  reference machine.
- **SC-007**: The full automated test suite passes without the real
  local agent runtime installed, demonstrating that the runtime
  abstraction is honoured.
- **SC-008**: A module or scenario whose schema version is unknown is
  refused at load time in 100% of cases, with a clear error naming
  the offending file.
- **SC-009**: 90% of users in a usability study can launch a
  scenario, follow its progress, and locate the final report without
  external help.

## Assumptions

- The desktop application runs as a single-user surface per OS
  account; multi-tenant use on one OS account is out of scope for the
  MVP.
- Subscription validation requires network access at sign-in; once
  signed in, an offline grace window is acceptable but its exact
  duration is a configuration detail rather than a product
  requirement.
- The user supplies their own model API token and pays the model
  provider directly; the platform does not proxy or rebill model
  usage in the MVP.
- The MVP bundles a single module (`df-client-launchpad`) — every
  scenario at MVP composes only skills of that module. The platform
  is multi-module by design (registry, scenario engine, dependency
  resolution all generic), but cross-module composition will only
  be exercised once a second module is added post-MVP. The single
  module is sourced from a separate Git repository and brought into
  the desktop-agent build via Git submodule; a remote module
  marketplace, signing infrastructure, and update channel are out
  of scope for the MVP.
- "Dangerous" is defined by the policies declared in module manifests
  plus a baseline set hard-coded in the policy engine (delete files,
  git push, install packages, run shell commands); broader policy
  authoring tools are out of scope for the MVP.
- The local agent runtime layer is a single logical concern with two
  cooperating components (orchestrator + sandbox); the product
  surface treats it as one runtime and never exposes the components
  separately to end users.
- Telemetry, crash reporting, and remote diagnostics are deferred
  past the MVP; observability in the MVP is local-only.
