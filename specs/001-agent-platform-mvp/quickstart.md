# Quickstart — Desktop AI Agent Platform (MVP)

This is the developer onramp for working on this feature. End-user
documentation lives elsewhere.

## Prerequisites

- .NET 9 SDK (LTS-track) on the build machine.
- Git.
- Optional: OpenClaw + NemoClaw runtimes for end-to-end testing of
  the real `ProcessRuntimeManager`. The full automated suite passes
  without them via `FakeRuntimeManager` (FR-019, SC-007).

## First build

```bash
git switch 001-agent-platform-mvp
git submodule update --init --recursive   # pulls modules/df-client-launchpad
dotnet restore AgentDesktop.sln
dotnet build  AgentDesktop.sln -warnaserror
dotnet test   AgentDesktop.sln --collect:"XPlat Code Coverage"
```

> The MVP bundles one module — `df-client-launchpad` — sourced from
> `github.com/DigitalFoxAgency/df-client-launchpad` (`refactor`
> branch) as a Git submodule under `modules/df-client-launchpad/`.
> Skipping the submodule init step leaves the bundled scenarios
> unable to load (every step references a skill in that module).

CI gates (must be green before merge):

| Gate | Command |
|------|---------|
| Static analysis | `dotnet build -warnaserror` |
| Tests + coverage | `dotnet test --collect:"XPlat Code Coverage"` |
| Benchmarks (PR smoke) | `dotnet run -c Release --project tests/AgentDesktop.Bench -- --filter *Smoke*` |
| Accessibility | included in `AgentDesktop.Desktop.Tests` |

## Run the desktop app

```bash
dotnet run --project src/AgentDesktop.Desktop
```

On first launch the app:
1. Asks the user to sign in with their subscription account.
2. Asks for a model API token, which is stored in the OS secure
   credential store via `ISecretStore`.
3. Copies the bundled modules and scenarios from the install
   directory into `<userData>/modules/` and `<userData>/scenarios/`
   (so users can edit and add more without touching install files).
4. Starts the local agent runtime through `IRuntimeManager`. With
   the real runtime missing, the app shows a "runtime unavailable"
   banner; with `--fake-runtime` the app uses the in-memory
   `FakeRuntimeManager` for development.

## Layering rules — enforced at the project level

| Project | May reference |
|---------|---------------|
| `AgentDesktop.Domain` | (nothing) |
| `AgentDesktop.Application` | `Domain` |
| `AgentDesktop.Infrastructure` | `Application`, `Domain` |
| `AgentDesktop.Desktop` | `Application` (the composition root in `Program.cs` is the only place that also references `Infrastructure`, via DI registration) |
| `AgentDesktop.Api` | `Domain` |

Adding a forbidden reference is a compile error. Do not work around it.

## Where things live

- **Service interfaces and use cases**: `src/AgentDesktop.Application/`.
- **Domain types** (no external deps): `src/AgentDesktop.Domain/`.
- **Adapters** (SQLite, secrets, runtime, MCP, HTTP): `src/AgentDesktop.Infrastructure/`.
- **UI** (Avalonia, view-models, theme): `src/AgentDesktop.Desktop/`.
- **Optional subscription API**: `src/AgentDesktop.Api/`.
- **Bundled modules**: `modules/` (validated against `contracts/module.schema.json`).
- **Bundled scenarios**: `scenarios/` (validated against `contracts/scenario.schema.json`).

## Common workflows

- **Add a new module**: drop a `module.json` under
  `<userData>/modules/<id>/` (or under `modules/` to bundle it),
  validate against the schema, restart or trigger Refresh in the UI.
- **Add a new scenario**: drop a YAML file under
  `<userData>/scenarios/`. Forward step-binding references are
  rejected at load time, not at runtime.
- **Add a dangerous action class**: extend `DangerousActionKind` and
  the baseline classification table in `DefaultPolicyEngine`.
  Coverage gate requires tests for both confirm and decline paths.

## Verifying the constitution gates locally

- Coverage: `dotnet test /p:CollectCoverage=true /p:Threshold=90`
  (Domain + Application). Lower thresholds for `Infrastructure` and
  `Desktop`. Tooling fails the build below threshold.
- Performance: BenchmarkDotNet smoke run on PRs; full suite nightly.
  The committed baseline lives under `tests/AgentDesktop.Bench/baseline/`.
- Accessibility: Avalonia.Headless tests assert focus order,
  keyboard reachability, and contrast on every shipped view.

## Test mode without the real runtime

Set the environment variable `AGENTDESKTOP_RUNTIME=fake` (or pass
`--fake-runtime`) to bind `FakeRuntimeManager` in the composition
root. This is also what CI uses. Any test that needs the real
runtime MUST be tagged with the `RequiresLiveRuntime` xUnit trait so
default CI can skip it cleanly.
