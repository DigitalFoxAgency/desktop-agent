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

### User Story 2 - Delegate a user intent to a module and follow its work in chat (Priority: P2)

A subscriber states an intent in the chat ("build me a landing page
for client X" or picks the "Onboard Client" operation from the
module catalogue). The agent recognises which **module** owns that
intent, **delegates the whole job** to that module, and the module
executes its own internal pipeline end-to-end. The user follows the
module's progress in the chat surface, confirms any dangerous
actions the module proposes, and steps in for any human-only
hand-offs the module asks for.

**Why this priority**: This is what makes the platform more than a
chat client. The platform's value is that it routes a user's intent
to the right module, shows the module's work, and brokers safety
(confirmation + audit) around what the module wants to do. The
**module owns the steps**; the platform owns the chat-as-bridge,
the policy gate, and the audit trail.

**Independent Test**: With Story 1 working, the user invokes the
launchpad's `onboard-client` operation, supplies the required
inputs, and observes the module's progress flow into the chat
in real time. The user confirms each dangerous action the module
asks about, marks each human-only hand-off complete when done, and
sees the final result rendered in the chat.

**Acceptance Scenarios**:

1. **Given** a signed-in user and an installed module that declares
   the requested operation, **When** the user invokes the operation,
   **Then** the platform delegates the whole job to that module and
   the module's progress events stream into the chat surface in
   real time.
2. **Given** a running delegation, **When** the module proposes a
   dangerous action, **Then** the platform's policy engine surfaces
   a confirmation prompt naming the action and target; the module
   only proceeds after the user confirms.
3. **Given** a running delegation, **When** the module reaches a
   human-only step, **Then** a hand-off prompt opens with the
   module's instructions; the module pauses until the user marks
   the step done.
4. **Given** the user cancels a running delegation, **When** they
   confirm cancellation, **Then** the platform signals the module
   to stop; the module finalises any in-flight work, the chat
   records the cancellation, and no further module work occurs.
5. **Given** a delegation fails inside the module, **When** the
   failure is reported back to the platform, **Then** the chat
   shows the module-supplied error and offers retry or cancel; no
   further work occurs without explicit user action.
6. **Given** a running delegation has a pending question from
   the module (the module called `AskUserAsync`), **When** the
   user types in chat, **Then** the message is routed to the
   module as the answer to its pending question; no new chat
   turn begins until the delegation has no pending question.
   Cancellation mid-delegation is a UI affordance (Cancel button
   on the operation panel), not something the user types in
   chat.

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

### User Story 4 - Browse the module catalogue and pick an operation (Priority: P3)

A subscriber browses the catalogue of installed modules, sees the
operations each module exposes (with names, descriptions, declared
inputs), and launches one directly without typing free-text into
the chat.

**Why this priority**: Direct invocation from the catalogue gives a
clear discovery surface for what the platform can do today. It is
not required for the first usable release but materially improves
day-to-day usability and is the path power users take when they
already know which module they want.

**Independent Test**: The user opens the module catalogue, sees the
launchpad's three operations (`onboard-client`, `launch-ads`,
`monthly-report`), picks one, supplies its inputs, and the
corresponding delegation begins.

**Acceptance Scenarios**:

1. **Given** modules are installed, **When** the user opens the
   module catalogue, **Then** each module's id, version, name,
   description, and the list of operations it exposes are visible.
2. **Given** an operation requires inputs, **When** the user runs
   it, **Then** the app prompts for those inputs and refuses to run
   until they are supplied.

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
  missing dependency named; the module's operations cannot start.
- A delegation is requested for an operation that the loaded module
  no longer declares — the platform refuses the delegation and
  surfaces a clear "operation not available in installed module
  version" error; the user can pick another operation.
- The local agent runtime is not installed or fails to start — the
  chat surface shows a clear "runtime unavailable" state and offers
  a retry; no operation can be delegated until the runtime is healthy.
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
  name, description, dependencies, **operations** (named entry
  points the platform can delegate to), MCP servers, prompts, and
  policies. Modules MAY additionally declare internal skills for
  introspection / advanced direct invocation, but skills are an
  internal implementation detail of the module — the platform does
  NOT orchestrate them.
- **FR-008**: The platform MUST treat each module as **self-orchestrating**:
  the platform delegates an entire operation to the owning module,
  the module executes its own internal pipeline, and the platform
  observes a stream of progress events from the module without
  driving the module's internal step order.
- **FR-009**: An operation declaration in a module manifest MUST
  declare its id, name, description, and inputs. The execution of
  the operation is the responsibility of the owning module; the
  platform stores no platform-side step definitions. **Operation
  inputs are advisory**: the platform passes whatever it has to
  the module (possibly empty / partial), and the module is
  responsible for asking the user (via callback) for any missing
  inputs interactively. The catalogue UI MAY collect inputs
  upfront when the user explicitly picks an operation, but the
  module MUST still tolerate incomplete inputs from a chat-driven
  delegation.
- **FR-010**: The platform MUST surface the module's progress events
  (text updates, intermediate results, errors) in the chat surface
  in real time, MUST broker any dangerous-action confirmations the
  module asks for through the platform's policy engine, and MUST
  surface human-only hand-off requests the module emits.
- **FR-011**: The user MUST be able to cancel a running delegation
  at any time; cancellation MUST be signalled to the running module,
  the module MUST stop initiating new work, the in-flight work MUST
  finalise cleanly, and the cancellation MUST be recorded in the
  chat history.
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
  box: `df-client-launchpad`. The module's manifest declares the
  operations the platform exposes; the module's internal pipeline
  (its own constitution, `ORDER.md`, skills, `SESSION-LOG.md`
  verification) is the module's concern and is NOT replicated at
  the platform level.
- **FR-021**: The bundled module MUST declare three operations the
  platform delegates to:
  - `onboard-client` — full Phase 1 client onboarding (intake →
    Discovery → strategy → site build → integrations → deploy);
  - `launch-ads` — Phase 2 ads kickoff;
  - `monthly-report` — Phase 2 reporting.
  The platform MUST NOT define its own version of these operations.
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
- **Skill** *(internal to a module)*: A named capability inside a
  module's pipeline. Modules MAY expose their internal skills for
  introspection or advanced direct invocation, but the platform
  does NOT orchestrate skills — it delegates whole operations and
  the module sequences its own skills internally.
- **Top-level agent**: The single OpenClaw-driven agent that owns
  the chat surface. On every user message it runs a chat turn,
  decides whether to reply in plain text or delegate to a module,
  and (when delegating) becomes the bridge between the user and
  the module. Router-only: its tool surface is `list_modules` and
  `delegate_operation`. It does NOT ask the user clarifying
  questions itself — clarification is the module's job during
  delegation (via `AskUserAsync`).
- **Operation**: A high-level user-facing entry point declared by a
  module's manifest. Has an id (unique within its owning module),
  name, description, and declared inputs. The platform delegates
  whole operations to modules; it does NOT orchestrate their
  internals.
- **Delegation**: A live execution of an operation. Has the
  (module, operation) pair, supplied inputs, the conversation it
  belongs to, an event stream surfaced to the chat, and a
  current status (Running / AwaitingConfirmation / AwaitingHumanHandoff
  / Cancelling / Completed / Cancelled / Failed).
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
- **SC-005**: The bundled module's `onboard-client` operation runs
  end-to-end on a representative client brief, with the module
  emitting human-handoff requests for each declared human-only
  step (Discovery call, content approvals, dashboard work) and
  resuming to completion once the human marks each step done — all
  without the platform driving any individual module step.
- **SC-006**: Reopening the app restores the most recent
  conversation, in full and in order, in under 2 seconds on the
  reference machine.
- **SC-007**: The full automated test suite passes without the real
  local agent runtime installed, demonstrating that the runtime
  abstraction is honoured.
- **SC-008**: A module manifest whose schema version is unknown is
  refused at load time in 100% of cases, with a clear error naming
  the offending file.
- **SC-009**: 90% of users in a usability study can launch a module
  operation, follow its progress in chat, and locate the final
  report without external help.

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
- The MVP bundles a single module (`df-client-launchpad`). The
  platform is multi-module by design (registry, dependency
  resolution, intent → module routing all generic) but the second
  module will arrive post-MVP. The single module is sourced from a
  separate Git repository and brought into the desktop-agent build
  via Git submodule; a remote module marketplace, signing
  infrastructure, and update channel are out of scope for the MVP.
- Each module owns its own internal pipeline (the launchpad has
  `ORDER.md`, `SESSION-LOG.md`, its own skill ordering, possibly
  its own subagents). The platform never reaches inside a module
  to drive its steps — the platform delegates whole operations
  and observes the module's progress events. This is recorded in
  research.md R18 and is non-negotiable for the MVP.
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
