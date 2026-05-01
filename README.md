# Agent Platform

Multi-tenant web platform where marketing agencies run packaged AI workflows
end-to-end on a backend that hosts Claude Code per run. Agency staff with
different roles pick up phases from a role-based inbox, hold a chat
conversation in their browser, and watch files appear in a live read-only file
tree. The platform supplies the AI under the hood (no BYOK).

The first bundled module is `df-client-launchpad` — Digital Fox's
client-onboarding pipeline (intake → research → strategy → site → deploy →
ads → reporting).

## Status

**Pre-MVP** — implementation in progress on `001-agent-platform-mvp`. The
single-user Avalonia desktop MVP is preserved on the `desktop` branch.

- [`specs/001-agent-platform-mvp/spec.md`](specs/001-agent-platform-mvp/spec.md) — product spec
- [`specs/001-agent-platform-mvp/plan.md`](specs/001-agent-platform-mvp/plan.md) — implementation plan
- [`specs/001-agent-platform-mvp/tasks.md`](specs/001-agent-platform-mvp/tasks.md) — task breakdown
- [`specs/001-agent-platform-mvp/known-issues.md`](specs/001-agent-platform-mvp/known-issues.md) — debugging + papercut log
- [`specs/001-agent-platform-mvp/constitution-check.md`](specs/001-agent-platform-mvp/constitution-check.md) — quality-gate review

## Layout

```
src/
├── AgentPlatform.Domain/         pure domain types
├── AgentPlatform.Application/    use cases, service interfaces
├── AgentPlatform.Infrastructure/ Postgres, vault, Docker, Anthropic
├── AgentPlatform.Api/            ASP.NET Core API + WebSocket hubs
└── AgentPlatform.Bridge/         per-run-container process wrapping `claude`

web/                              TypeScript + React + Vite + Tailwind
modules/df-client-launchpad/      bundled module (Git submodule under source/)
tests/                            xUnit (.NET) + Playwright (web)
scripts/                          backup.sh, claude-login.sh
```

## Architecture in one paragraph

The **API** (5080) serves auth, runs, and inbox. To open a phase it talks to
the host Docker daemon and spawns one `agentplatform/run-base` container per
phase — that container runs the **Bridge** binary, which fork-execs the
`claude` CLI in stream-json mode and proxies every chat token / file event /
confirmation request back to the API over a WebSocket. The **web client**
(5173 in dev) is a Vite/React SPA that talks to the API over REST + a
per-phase WebSocket. Per-run filesystem state lives in a host-side volume that
gets re-mounted as `/workspace` on every phase open, so files written by
phase N are visible to phase N+1.

## Prerequisites

- macOS or Linux
- **Docker** (Docker Desktop on macOS) — used for the Postgres container and
  for spawning per-run claude containers
- **.NET 9 SDK** — the API + Bridge are .NET 9
- **Node 20+** (and `npm`) — the web client is Vite/React/TS
- An **Anthropic API key with credits**, or be ready to subscription-login
  via `scripts/claude-login.sh`

## Quick start (host dev loop with hot reload)

The fast iteration loop runs Postgres in Docker, the API via `dotnet run`, and
the web via `vite dev`. Three terminals.

### One-time setup

```bash
git clone <this-repo>
cd desktop-agent
git submodule update --init --recursive

# Build the run-base image (~5–10 min first time, ~1.25 GB on disk)
docker build -t agentplatform/run-base:latest -f Dockerfile.run-base .

# Copy the env template, fill in real values
cp .env.local.example .env.local
$EDITOR .env.local

# Make sure the local writable dirs you put in .env.local exist
mkdir -p .local/vault .local/runs .local/archive
```

The values you must change in `.env.local`:

| Key | Generate with |
|---|---|
| `Jwt__SigningKey` | `openssl rand -base64 48` |
| `AgentPlatform__Vault__KeyBase64` | `openssl rand -base64 32` |
| `AgentPlatform__PhaseSession__AnthropicApiKey` | from console.anthropic.com |
| `AgentPlatform__ModulesRoot` | absolute path to `<repo>/modules` |
| `AgentPlatform__Vault__Directory` | absolute path to `<repo>/.local/vault` |
| `AgentPlatform__Runs__Root` | absolute path to `<repo>/.local/runs` |
| `AgentPlatform__Runs__ArchiveRoot` | absolute path to `<repo>/.local/archive` |

> ⚠️ The Postgres connection string MUST be quoted (`"..."`). Bash treats
> `;` as a command separator inside `set -a; source`, so an unquoted value
> silently drops everything after the first `;` and you'll see a 28P01 auth
> error against `postgres/postgres` from the hardcoded fallback.

### Run it

```bash
# Terminal 1 — Postgres only (uses docker-compose.yml's postgres service)
docker compose up postgres

# Terminal 2 — API
set -a; source .env.local; set +a
dotnet run --project src/AgentPlatform.Api
# → http://localhost:5080/healthz returns {"status":"ok"}
# → http://localhost:5080/readyz pings the DB

# Terminal 3 — Web (Vite dev server with hot reload)
cd web
npm install                # first time only
npm run dev
# → http://localhost:5173
```

Open `http://localhost:5173`. Sign up to create a tenant + admin user. From
the dashboard you can browse the catalogue, start an `Onboard Client` run,
and step through phases from the inbox.

### Tail the API logs nicely

The API uses Serilog and writes JSON to stdout. To make it readable:

```bash
dotnet run --project src/AgentPlatform.Api 2>&1 \
  | jq -R '. as $line | try fromjson catch $line'
```

## Demo to a colleague (Cloudflare Tunnel, free, ~5 min)

Gives you a public HTTPS URL → your laptop. The web SPA's relative URLs go
through Vite's dev-server proxy to the API, so you only need to expose port
5173.

```bash
# Install once
brew install cloudflared      # macOS
# (linux: see https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/install-and-setup/installation/)

# Make sure web/.env.local does NOT set VITE_API_BASE (so the SPA uses
# same-origin URLs and Vite proxies /api + /ws to localhost:5080).
# vite.config.ts has `allowedHosts: true` so any tunneled hostname works.

# With the dev loop already running (Postgres + API + Vite):
cloudflared tunnel --url http://localhost:5173
# → prints something like https://random-words.trycloudflare.com
```

Share that URL with your colleague. Caveats:

- ⚠️ Anyone with the URL can sign up and start runs **on your laptop's
  Anthropic key** — don't post the URL publicly.
- ⚠️ The tunnel + your laptop must stay on. Closing either kills the URL.
- ⚠️ The URL is random per-run. Re-running `cloudflared` gives a new one.

To bring it down: `Ctrl+C` the tunnel, then `Ctrl+C` Vite + the API.

## Production-ish deployment (single VPS, ~$5/mo)

The `docker-compose.yml` is designed for this. Spin up an Ubuntu 22.04 box on
Hetzner / DigitalOcean / Linode (4 GB RAM minimum — each phase container
reserves 2 GB), install Docker, and run the whole stack from compose.

```bash
ssh root@<your-box>
apt-get update
apt-get install -y docker.io docker-compose-plugin git

git clone <this-repo>
cd desktop-agent
git submodule update --init --recursive

# Build the run-base image (slow, ~5–10 min)
docker build -t agentplatform/run-base:latest -f Dockerfile.run-base .

# Production env. Different file from .env.local — values feed compose
# variable substitution, not the .NET config binding directly. See the
# `environment:` block in docker-compose.yml for the exact mapping.
cat > .env <<EOF
POSTGRES_USER=agentplatform
POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d /+= | head -c 32)
JWT_SIGNING_KEY=$(openssl rand -base64 48)
AGP_VAULT_KEY_BASE64=$(openssl rand -base64 32)
ANTHROPIC_API_KEY=sk-ant-api03-...
EOF

docker compose up -d --build
```

For TLS + a real domain: put **Caddy** in front. Add to `docker-compose.yml`:

```yaml
  caddy:
    image: caddy:2-alpine
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy-data:/data
    depends_on: [api, web]
volumes:
  caddy-data:
```

with a `Caddyfile`:

```
agent.yourdomain.com {
    reverse_proxy /api/* api:5080
    reverse_proxy /ws/*  api:5080
    reverse_proxy *      web:80
}
```

Point your domain at the box's IP, Caddy handles Let's Encrypt automatically.

### Postgres backups

```bash
# scripts/backup.sh runs a pg_dump | gzip with retention pruning.
# Wire into a systemd timer or a docker-compose oneshot.
PGPASSWORD=... bash scripts/backup.sh
```

## Troubleshooting

The most common papercuts surfaced during real use:

| Symptom | Cause | Fix |
|---|---|---|
| `28P01: password authentication failed for user "postgres"` | `.env.local` is unquoted; bash split the connection string at `;` | Quote it: `ConnectionStrings__Postgres="Host=...;Port=...;..."` |
| Catalogue is empty (0 modules loaded) | `dotnet run --project src/AgentPlatform.Api` cwd is the project dir, default `"modules"` resolves to `src/AgentPlatform.Api/modules` | Set `AgentPlatform__ModulesRoot` to an absolute path in `.env.local` |
| Phase open returns 500 with `Access to the path '/var/lib/agency' is denied` | The compose-baked vault/runs/archive defaults aren't writable on host dev | Set `AgentPlatform__Vault__Directory` etc. to absolute paths under your repo and `mkdir -p` them |
| Claude in the container says **"Not logged in · run /login"** or **"Credit balance is too low"** | `AgentPlatform__PhaseSession__AnthropicApiKey` not set, or the key is out of credits | Set the key. Check console.anthropic.com for credit balance |
| Phase is in `Running` for 10+ minutes with no chat / file activity | Claude is stalled (rate limit, credit, or stdin EOF expected) | The new idle watchdog auto-pauses the run after 10 min and writes a `phase.stalled` audit. Also see `GET /api/phases/{id}/diagnostics` |
| CORS error in the browser console for `/api/...` | The API actually 500'd and returned without CORS headers — the CORS message is misleading | Check the API logs for the underlying exception |
| `EXEC failed: exec: "ps": executable file not found` when debugging containers | Run-base image is minimal | Use `cat /proc/<pid>/cmdline` instead of `ps` |
| Tunnel demo: page loads but `Blocked request. This host ... is not allowed` | Vite's host check rejected the tunnel hostname | Already fixed via `allowedHosts: true` in `vite.config.ts` |

Open issues tracked in
[`specs/001-agent-platform-mvp/known-issues.md`](specs/001-agent-platform-mvp/known-issues.md)
(E1–E13 from the 2026-05-01 e2e walk).

## Tests

```bash
# All .NET unit + contract tests (no infra needed)
for p in Domain Application Bridge Contracts; do
  dotnet test tests/AgentPlatform.$p.Tests/AgentPlatform.$p.Tests.csproj --nologo
done

# WebApplicationFactory + Testcontainers integration tests (need Docker)
dotnet test tests/AgentPlatform.Api.Tests/AgentPlatform.Api.Tests.csproj
dotnet test tests/AgentPlatform.Infrastructure.Tests/AgentPlatform.Infrastructure.Tests.csproj

# Web type-check
cd web && npx tsc --noEmit
```

## License

[MIT](LICENSE)
