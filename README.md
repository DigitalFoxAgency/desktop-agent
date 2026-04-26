# Desktop Agent

Cross-platform desktop AI agent platform built with .NET 9 + Avalonia.

## Status

**Pre-MVP — implementation in progress on `001-agent-platform-mvp`.**
See `specs/001-agent-platform-mvp/spec.md` for the product spec and
`specs/001-agent-platform-mvp/plan.md` for the implementation plan.

## Quick start

For development setup, build, and test instructions, see
[`specs/001-agent-platform-mvp/quickstart.md`](specs/001-agent-platform-mvp/quickstart.md).

Short version:

```bash
git submodule update --init --recursive
dotnet restore AgentDesktop.sln
dotnet build  AgentDesktop.sln -warnaserror
dotnet test   AgentDesktop.sln --collect:"XPlat Code Coverage"
```

## Layout

```
src/
├── AgentDesktop.Domain/         pure domain types (no external deps)
├── AgentDesktop.Application/    use cases, service interfaces
├── AgentDesktop.Infrastructure/ adapters (SQLite, secret stores, runtime, MCP, HTTP)
├── AgentDesktop.Desktop/        Avalonia UI + composition root
└── AgentDesktop.Api/            optional ASP.NET Core subscription endpoint

tests/
├── AgentDesktop.Domain.Tests/
├── AgentDesktop.Application.Tests/
├── AgentDesktop.Infrastructure.Tests/
├── AgentDesktop.Desktop.Tests/  Avalonia.Headless
├── AgentDesktop.Contracts.Tests/ cross-project contract tests
└── AgentDesktop.Bench/           BenchmarkDotNet

modules/df-client-launchpad/      bundled product Module (Git submodule + manifest)
scenarios/                        bundled scenario definitions

specs/001-agent-platform-mvp/     spec, plan, research, data model, contracts, tasks
docs/architecture/                deferred future-state docs
```

## Documents to read

| Audience | Start with |
|----------|-----------|
| Reviewing the product | [spec.md](specs/001-agent-platform-mvp/spec.md) |
| Implementing | [plan.md](specs/001-agent-platform-mvp/plan.md) → [tasks.md](specs/001-agent-platform-mvp/tasks.md) |
| Building / testing locally | [quickstart.md](specs/001-agent-platform-mvp/quickstart.md) |
| Project rules | [CONSTITUTION](.specify/memory/constitution.md) |

## License

[MIT](LICENSE)
