# Subsystem Boundaries — DEFERRED FUTURE STATE

> ⚠️ **STATUS: DEFERRED. DO NOT IMPLEMENT YET.** ⚠️
>
> This document describes the **post-MVP** architectural target,
> not the current state of the codebase. The MVP uses the simpler
> layered Clean Architecture documented in
> `specs/001-agent-platform-mvp/plan.md`. The deferral decision
> and the triggers for revisiting it are recorded in
> `specs/001-agent-platform-mvp/research.md` R16.
>
> Open this file only when one of the R16 "when to revisit"
> triggers has fired and you are about to migrate the codebase
> from layered to modular-monolith. Until then, treat this as
> documented future homework.

---

This is the **operational** rulebook for the modular-monolith
boundaries we will adopt **if/when** we migrate. Read it for
context when reviewing the migration PR; do not write code today
that assumes any of these rules are in effect.

## Vocabulary

- **Subsystem** — an architectural bounded context. One project
  per subsystem under `src/Subsystems/<Name>/`. Six exist at MVP:
  `Chat`, `Modules`, `Scenarios`, `Policies`, `Runtime`,
  `Identity`. Plus `SharedKernel` (not a subsystem; primitives only)
  and the two hosts (`Desktop`, `Api`).
- **Module** — a **product** concept (`df-client-launchpad`). Loaded
  from `modules/<id>/module.json`. Owned by the `Modules`
  subsystem. **Never** synonymous with "subsystem".
- **Contracts** — the public surface of a subsystem. Lives in the
  subsystem's `Contracts/` folder/namespace. The only types
  referenceable from outside the subsystem.

## Project layout

```
src/SharedKernel/AgentDesktop.SharedKernel/
src/Subsystems/<Name>/AgentDesktop.<Name>/
src/Hosting/AgentDesktop.Desktop/
src/Hosting/AgentDesktop.Api/
```

Each subsystem assembly contains four top-level folders:

| Folder | Visibility | Contents |
|--------|------------|----------|
| `Contracts/` | `public` | Interfaces and DTOs other subsystems may depend on. Stable; changing a public type is a breaking change. |
| `Domain/` | `internal` | Pure domain types, invariants, value objects. No I/O. |
| `Application/` | `internal` | Use cases, application services, ports (e.g. `IChatRepository`) the Infrastructure layer implements. |
| `Infrastructure/` | `internal` | Adapters: SQLite, file system, HTTP, OS APIs, processes. |

## Boundary rules

1. **No type outside `Contracts/` may be referenced from another
   subsystem.** Enforced by `internal` modifier + banned-symbols
   analyzer. Compile error if violated.
2. **`SharedKernel` may be referenced by any subsystem and any
   host.** It is the *only* assembly that may freely cross
   boundaries. Keep it small — types live there only when **two
   or more subsystems already need them today**, never speculatively.
3. **A subsystem's tests project (`tests/Subsystems/<Name>.Tests`)
   is granted `[InternalsVisibleTo]` for its own subsystem
   only.** No test project may see another subsystem's internals.
4. **Cross-subsystem behaviour tests** live in
   `tests/AgentDesktop.Contracts.Tests/` and depend on
   `Contracts/` types only, exercising fakes provided by that
   project.
5. **`Desktop` is the composition root.** It is the *only*
   project allowed to reference every subsystem. It wires
   `Contracts/` interfaces to their implementations via DI.
6. **`Api` references at most the subsystems whose contracts it
   exposes** — at MVP, only `Identity.Contracts`. It must not
   reference `Chat`, `Scenarios`, `Modules`, `Policies`, or
   `Runtime`.
7. **Reverse references are forbidden.** A subsystem must not
   reference a host project. The dependency arrow always points
   from hosts toward subsystems and from subsystems toward
   `SharedKernel`.

## Reference matrix

Read each row as "may this assembly reference that assembly?".

| From ↓ \ To → | SharedKernel | Chat | Modules | Scenarios | Policies | Runtime | Identity | Desktop | Api |
|---|---|---|---|---|---|---|---|---|---|
| **SharedKernel** | — | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Chat** | ✅ | — | ❌ | ❌ | ✅ Contracts | ✅ Contracts | ❌ | ❌ | ❌ |
| **Modules** | ✅ | ❌ | — | ❌ | ✅ Contracts | ❌ | ❌ | ❌ | ❌ |
| **Scenarios** | ✅ | ✅ Contracts | ✅ Contracts | — | ✅ Contracts | ✅ Contracts | ❌ | ❌ | ❌ |
| **Policies** | ✅ | ❌ | ❌ | ❌ | — | ❌ | ❌ | ❌ | ❌ |
| **Runtime** | ✅ | ❌ | ❌ | ❌ | ✅ Contracts | — | ❌ | ❌ | ❌ |
| **Identity** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | — | ❌ | ❌ |
| **Desktop** | ✅ | ✅ Contracts | ✅ Contracts | ✅ Contracts | ✅ Contracts | ✅ Contracts | ✅ Contracts | — | ❌ |
| **Api** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ Contracts | ❌ | — |

✅ Contracts = "may reference public types in the target's
`Contracts/` namespace **only**"; analyzer + `internal` enforce
this.

## Cross-subsystem communication

### Synchronous (default)

A consumer subsystem references the producer's `Contracts/`
interfaces and calls them directly via DI. Example: `Scenarios`
calls `IModuleRegistry` (from `Modules.Contracts`) and
`IPolicyEngine` (from `Policies.Contracts`).

No mediator. No message bus. No MediatR.

### Domain events (one-to-many)

For broadcast-shaped notifications (e.g. "subscription expired",
"runtime degraded"), use `IDomainEventPublisher` /
`IDomainEventHandler<TEvent>` from `SharedKernel`. Implementation
is ~80 LoC of in-process pub/sub. Handlers are registered in
the host's composition root.

Use this only when:

1. The publisher does not need to know its consumers.
2. Multiple subsystems would otherwise install identical hooks.

If only one subsystem cares, use a direct synchronous call instead.

### Forbidden patterns

- A subsystem reaching into another's `Application/` or
  `Infrastructure/` types (compile error).
- Two subsystems sharing a type by both adding a project
  reference to the same internal helper. If a type is shared,
  promote it to `SharedKernel` via PR review (and only when ≥2
  subsystems already need it).
- "Anaemic shared kernel" — every type that could be shared
  pre-emptively migrating to `SharedKernel`. The kernel becomes
  a dumping ground; resist this.
- Service locator (`IServiceProvider` injection) inside a
  subsystem. Inject the specific contracts you need.

## Adding a new subsystem

1. Create `src/Subsystems/<Name>/AgentDesktop.<Name>/<Name>.csproj`.
2. Create the four folders (`Contracts/`, `Domain/`,
   `Application/`, `Infrastructure/`).
3. Mark every non-`Contracts` type `internal`.
4. Add `<Name>` to the analyzer config and write its
   `BannedSymbols.txt` (initially: ban every type from this
   assembly that is not under the `Contracts/` namespace).
5. Add `[InternalsVisibleTo("AgentDesktop.<Name>.Tests")]` to
   the assembly.
6. Create `tests/Subsystems/AgentDesktop.<Name>.Tests/`.
7. Update this document's reference matrix.

## Promoting a type to `SharedKernel`

A type joins `SharedKernel` only when:

- Two or more subsystems already depend on it today.
- It is genuinely primitive — no behaviour beyond invariants.
- It does not pull a third-party dependency along with it.

Promote with a PR that:

1. Moves the type to `AgentDesktop.SharedKernel`.
2. Updates every subsystem that referenced the old location.
3. Notes the promotion in the PR body.

## Splitting a subsystem

If a subsystem grows beyond comprehension:

1. Identify a clean cut along bounded contexts.
2. Create the new subsystem (above).
3. Move types in atomic PRs, fixing analyzer errors as they
   appear.
4. Keep public Contracts stable across the split where possible
   (consumers should not need to update unless behaviour
   changed).

## Extracting a subsystem to a separate process

If a subsystem must run out-of-process (e.g. `Runtime` for
hard-isolation reasons):

1. Inside the subsystem, split `Domain` / `Application` /
   `Infrastructure` into three projects (heavy layout).
2. Replace the in-process implementation registered in
   `Desktop/Program.cs` with an IPC adapter (e.g. JSON-RPC over
   a named pipe).
3. Public `Contracts/` stay byte-compatible; consumer subsystems
   need no code change.

This is the explicit reason we designed for one-day extraction
even though the MVP is purely in-process.
