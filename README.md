# Agent Platform

Multi-tenant web platform where marketing agencies run packaged AI
workflows end-to-end on a backend that hosts Claude Code per run.
Agency staff with different roles pick up phases from a role-based
inbox, hold a chat conversation in their browser, and watch files
appear in a live read-only file tree. The platform supplies the AI
under the hood (no BYOK).

The first bundled module is `df-client-launchpad` — Digital Fox's
client-onboarding pipeline (intake → research → strategy → site →
deploy → ads → reporting).

## Status

**Pre-MVP** — implementation in progress on `001-agent-platform-mvp`.
The single-user Avalonia desktop MVP is preserved on the `desktop`
branch as a historical snapshot.

See:

- [`specs/001-agent-platform-mvp/spec.md`](specs/001-agent-platform-mvp/spec.md) — product spec
- [`specs/001-agent-platform-mvp/plan.md`](specs/001-agent-platform-mvp/plan.md) — implementation plan
- [`specs/001-agent-platform-mvp/tasks.md`](specs/001-agent-platform-mvp/tasks.md) — task breakdown

## Layout (target)

```
src/
├── AgentPlatform.Domain/         pure domain types
├── AgentPlatform.Application/    use cases, service interfaces
├── AgentPlatform.Infrastructure/ Postgres, vault, Docker, GitHub, Anthropic
├── AgentPlatform.Api/            ASP.NET Core API + WebSocket
└── AgentPlatform.Bridge/         per-run-container process wrapping `claude`

web/                              TypeScript + React + Vite + Tailwind
modules/df-client-launchpad/      bundled module (Git submodule under source/)
tests/                            xUnit (.NET) + Playwright (web)
```

## Quick start

```bash
git submodule update --init --recursive
docker compose up
```

(Quick start will fill in as Phase 1 scaffolding lands.)

## License

[MIT](LICENSE)
