---
description: "Known issues + polish backlog surfaced during US3 dogfooding"
---

# Known issues & polish backlog

Issues observed during live testing of the US3 (phase hand-off) slice on
2026-04-30. Captured here so they survive between sessions. Each entry has a
root-cause, current state, and a proposed fix path. Not a substitute for
`tasks.md` — these are follow-ups that emerged from real use.

## A. Authorization gaps (per-phase access control)

The role model is correct for hand-off routing (a marketer can't accept a
designer phase, etc.) and for admin-gated operations (reassign, role
management). But several per-phase surfaces are not gated.

### A1. Phase open / close not restricted to the assignee
**Symptoms**: any signed-in tenant user can `POST /api/phases/{id}/open` or
`/close` for any phase in the tenant — including phases assigned to other
users — and chat in the underlying claude session. Mirror security
reasoning: a marketer could open the strategist's in-progress phase and see
the live chat.
**Root cause**: `PhaseEndpoints.OpenAsync` / `CloseAsync` only check
authentication + tenant membership. There's no "is the caller the
assignee, or admin?" check.
**Status**: deferred. Not blocking US3 functionally but a real
authorization hole.
**Fix sketch**: add a check inside `OpenAsync` / `CloseAsync`:
```csharp
var assignment = await repo.GetCurrentAssignmentAsync(tenantId, phaseRunId, ct);
if (assignment is null) return Results.NotFound();
if (assignment.Assignment.AssignedUserId != ctx.UserId
    && !ctx.Roles.Contains(Role.Admin))
{
    return Results.Forbid();
}
```
Same gate goes on `/files` and `/files/content` in `PhaseEndpoints`.

### A2. `POST /api/runs` is not role-gated
**Symptoms**: any signed-in tenant user can start a workflow run. Pricing /
quota implications: a non-admin could exhaust the tenant's monthly token
budget by spamming runs.
**Status**: deferred. Decide whether starts should require Admin or any
non-admin role — depends on agency operating model.
**Fix sketch**: add `if (!ctx.Roles.Contains(Role.Admin)) return Forbid();`
to the start handler in `RunEndpoints`, or define a "can-start-runs"
permission.

### A3. `GET /api/runs` returns all tenant runs to every tenant user
Same shape as A1 — fine for admins, but marketers can read run metadata
they have no business with. Lower-severity than A1 since no chat content is
exposed, only run shape (module, status, started-at).

---

## B. Run-container lifecycle UX

### B1. Cost cap previously didn't persist run status (FIXED in `ad16795`)
**Was**: `PhaseSessionService.PumpUsageAsync` logged "pausing" on cap, but
never set `WorkflowRun.Status = Paused`. Re-opening the phase silently
disconnected because the next `TokenUsage` event from the new container
re-fired the cap and auto-closed the session.
**Now**: status is persisted; `OpenAsync` refuses with a "Run paused" reason
that the Phase view renders as an amber banner; `DisableCostCap` env toggle
for dev.
**Follow-up**: surface a "Resume / Raise cap" admin button in the banner so
ops doesn't have to restart the API to bump the cap.

### B2. No way for a non-engineer to mark a phase verified
**Symptoms**: launchpad skills auto-write `[<skill>: completed]`. Some
phases in the launchpad's contract require a human reviewer to write
`[<skill>: verified]` for downstream skills' preconditions to pass. Today
nothing in our UI writes verified markers — we patched this by treating
`completed` as sufficient (commit `5fee1ed`), but the launchpad's
human-in-the-loop pattern is short-circuited.
**Status**: pragmatic accept-completed workaround in place. Long-term: add
a "Mark as verified" button on the phase view that appends the marker to
`SESSION-LOG.md`.

### B3. Phase status reads as raw enum integer in API responses
**Symptoms**: `GET /api/runs/{id}` returns `"Status": 2` and `"phases":
[{"status": 2, ...}]`. The web has to know that 2 = Running. Today we
display the integer directly on the Run page (`Status: 2`), which is
unhelpful for non-technical users.
**Fix sketch**: `JsonStringEnumConverter` on the Api project's options, or
explicit `.ToString()` mapping in the run/phase endpoints.

---

## C. Launchpad integration mismatches

These exist because the launchpad was authored as a local-CLI workflow,
not a hosted-platform workflow. They belong half to the platform and half
to the module.

### C1. Launchpad's `init` SKILL.md assumes `cwd = launchpad root` (PARTIALLY FIXED)
**Was**: `/init` told claude to write to `clients/<slug>/` relative to the
launchpad root. In our container, cwd is `/workspace` (empty) and the
launchpad source is at `/opt/modules/...` (read-only). Claude tried to
write to the read-only mount and got blocked.
**Now**: orientation system prompt at session open (commit `5fee1ed`)
tells claude to treat `/workspace` as the launchpad root and never write
to `/opt/modules`. Plus run-level inputs are seeded into the first
message so claude doesn't ask the user.
**Follow-up**: launchpad-side improvement — make the SKILL.md path-aware
(e.g. honour `AGP_OUTPUT_DIR` if set). Coordinate with launchpad
maintainers in
`github.com/DigitalFoxAgency/df-client-launchpad`.

### C2. Launchpad `init` stamps out the entire `template/` (PARTIALLY FIXED)
**Symptoms**: full Astro site code (`07-site/`), build scripts, configs,
mcp.json, `.claude/skills/` show up under
`/workspace/clients/<slug>/`, alongside the human-readable business
deliverables (research, brief, strategy, design, ads). Non-technical
users see a confusing mix of business artifacts and raw code.
**Status**: per-role file tree filter shipped (commit `5fee1ed`).
Marketer/strategist/designer/media-buyer see only business folders;
admin/engineer see everything.
**Follow-up options** (not yet picked up):
- Module-side restructure: split `template/business/` from
  `template/site/` so the file tree filter doesn't depend on a
  hard-coded numbered-folder convention.
- Manifest-side tagging: declare per-folder visibility in `module.json`
  (`{"path": "07-site", "visibility": "engineer"}`) so the platform's
  filter is config-driven, not module-hardcoded.

### C3. `claude --permission-mode bypassPermissions` is currently the default for dev
**Symptoms**: every Write/Edit/Bash claude wants to do is auto-approved.
Fine for dev/demo. Not safe for any environment that hosts real client
data.
**Resolution path**: blocked by **US4 (Dangerous-action confirmations,
T129–T143)**. Once the platform's `ConfirmationGate` + UI dialog land,
flip `--permission-mode` back to `ask` (or remove it entirely so claude
falls back to its default), and wire its tool-use prompts into our
`/api/confirmations/{id}/decide` endpoint.

### C4. Verification key alignment (FIXED in `5fee1ed`)
**Was**: `module.json` declared `"verificationKey": "init: verified"` but
the launchpad auto-writes `[init: completed]` (a human reviewer is
expected to promote `completed` → `verified`). On an automated run, no
human ever wrote `verified`, so `CompletionDetector` never fired.
**Now**: `CompletionDetector` accepts either `<skill>: completed` or
`<skill>: verified`. The init→pre-research hand-off was observed firing
end-to-end during the 2026-04-30 dogfood session.

---

## D. Cosmetic / UX

### D1. Claude's "thinking aloud" leaks into the chat
**Symptoms**: when claude reasons through tool failures (e.g. "Let me try
rsync instead of cp because cp is restricted…") the reasoning text
streams into the chat. For non-technical users this is noise.
**Fix sketch**: claude has an `extended_thinking` output mode that puts
internal reasoning in a separate block. Wire it in `StreamJsonClaudeWrapper`
and only forward the final assistant text to the UI; expose internal
reasoning behind a "Show reasoning" toggle for engineers.

### D2. No "run paused" / "phase complete" toast notifications
**Symptoms**: when a phase completes (and the next one queues into the
inbox), the phase view doesn't tell the user. They have to navigate back
to the inbox to see the new item.
**Fix sketch**: subscribe the phase view to the same `/ws/inbox`
push that powers live inbox updates, and show a toast when a new item
arrives whose `phaseRunId` isn't the currently-open one.

### D3. Stale supporting artefacts (T161 in `tasks.md`)
`research.md`, `data-model.md`, `quickstart.md`, `contracts/` are still
desktop-MVP-flavoured. Tracked as T161 in the plan, not blocking US4
work.

---

## Findings from full-pipeline e2e walk (2026-05-01)

Drove `onboard-client` end-to-end via Playwright + an API-driven phase
walker. The single-tenant happy path worked through phase 5; phase 5
(`semantics`) silently stalled. Root cause turned out to be Anthropic
"Credit balance is too low" — visible in the chat only after we
reopened the phase post-fix. Without observability this was invisible.

### E1. No phase-progress timeout — DONE in commit `89cdcad`
**Symptoms**: `semantics` container stayed up 39+ min, claude PID alive,
0 token-usage events, no file writes, CPU 2.56 %. The platform had no
upper bound on phase duration; a stuck claude session held the run
indefinitely.
**Fix shipped**: `PhaseSessionService` tracks last-event timestamp on
the active handle. A 30s ticker checks idle duration; past
`IdleStallSeconds` (default 600) it pauses the run, writes a
`phase.stalled` audit row, and closes the container. New
`GET /api/phases/{id}/diagnostics` exposes idle state. UI shows
"Last activity X ago" → "⚠ Stalled" badge in the orientation bar.

### E2. Run-start defaults break host dev loop
**Symptoms**: `/var/lib/agency/{vault,runs,archive}` baked in as
defaults, only writable inside the API container. On host `dotnet run`
the first phase open 500s on `UnauthorizedAccessException`.
**State**: worked around by setting explicit paths in `.env.local`.
The auto-resolve patch in `Program.cs` was reverted — operator should
know what they're configuring. **Still TODO**: README quickstart must
list the required env vars or ship `.env.local.example`.

### E3. Port inconsistency between dev and compose — DONE in commit `a7043a6`
API was bound to 5000 by Kestrel default, bridge URL / web client /
docker-compose all expected 5080. Caused first-time-runner CORS errors
(really 500s missing CORS headers due to a downstream `/var/lib/agency`
permission failure). Pinned to 5080 everywhere.

### E4. Module registry default path resolves wrong from `dotnet run`
**Symptoms**: `AgentPlatform__ModulesRoot` defaults to relative
`"modules"`, which resolves against the API project dir
(`src/AgentPlatform.Api/modules`), not repo root → 0 modules loaded →
empty catalogue → no runs can start.
**State**: `.env.local` carries an absolute path. Auto-resolve patch
reverted (same reasoning as E2). **Still TODO**: README coverage.

### E5. .env.local + bash quoting traps
**Symptoms**: `ConnectionStrings__Postgres="Host=...;Port=...;..."` must
be quoted because bash treats `;` as a command separator inside
`set -a; source`. Unquoted, the first run silently lost everything
after the first `;`, hit the hardcoded `postgres/postgres` default,
and 500'd on auth. Subtle and easy to miss.
**Fix**: ship a `.env.local.example` with quoted values + a comment.
Documented in the live `.env.local` for now.

### E6. Inbox accumulates forever — DONE in commit `89cdcad`
Confirmed live: completed phases stayed in the inbox alongside pending
ones with no visual difference.
**Fix shipped**: API now exposes `phaseStatus` + `inputs` per inbox
row (`ListInboxWithContextAsync`). Web shows tabs Active / Done / All
defaulting to Active, with each row labelled by client name and a
status badge.

### E7. ANTHROPIC_API_KEY mapping ambiguity
**Symptoms**: Setting `ANTHROPIC_API_KEY` (the natural ops-friendly
name) didn't work because the .NET config binding wants
`AgentPlatform__PhaseSession__AnthropicApiKey`. Until both are set,
claude inside the run container 401s with "Not logged in / run /login".
**State**: documented in `.env.local` comment; alias-fallback in
`Program.cs` was reverted (kept the codebase honest to canonical
names instead).

### E8. Status enum integers leaked to UI — DONE in commit `3c196e9`
Replaced raw `Status: 2` / `Order: 0` displays with status badges +
display names sweep across dashboard / inbox / run detail.

### E9. Phase live view had no orientation — DONE in commit `3c196e9`
Marketers opening a phase had no way to know which step (5 of 14),
what role, what's expected. Orientation banner + breadcrumb + file
preview pane wired in.

### E10. Run inputs invisible everywhere — DONE in commit `89cdcad`
Two runs for different clients were indistinguishable. Now surfaced
on dashboard (Client column), inbox (primary heading), and run detail
(input strip). Backed by `inputs` field on the run list/detail
responses.

### E11. SESSION-LOG marker hygiene drifts
**Symptoms**: On the live run, `init` and `pre-research` wrote
`[<skill>: completed]` to SESSION-LOG. `brief` wrote a "Next: добавить
[brief: completed]" template line (interpolated, not literal) but
never the actual marker. `research` and `semantics` wrote nothing.
The platform still marked each phase Completed via the session-exit
fallback (T120-era marker fallback).
**Fix path**: stricter prompt enforcement (skill SKILL.md should make
the marker line non-optional), and surface in audit which phases
ended via marker vs fallback so we can detect drift.

### E12. Bridge observability gaps — partially DONE in commit `89cdcad`
**Symptoms**: When `semantics` stalled, `docker logs` only showed
"Bridge connecting" / "Wired skills" — nothing about claude's
activity. We had no fighting chance of debugging from outside.
**Fix shipped (partial)**: API tracks `LastEventKind` + `LastEventAt`
per phase, exposed via diagnostics endpoint and surfaced in the UI.
**Still TODO**:
- Log every `ClaudeStreamEvent` at `Info` level in the Bridge
  (currently only emitted to the WebSocket).
- Bump claude **stderr** from `Debug` → `Warning` so Anthropic API
  errors / rate-limit notices surface in `docker logs`.
- Idle heartbeat in the Bridge: every 60s log "claude idle for {n}s;
  last event {type}".
- Recent-events ring buffer in the Bridge (last N) accessible via
  the diagnostics endpoint for postmortem.

### E13. Synthetic-input session lifecycle — root cause for E1 stall
**Symptoms**: claude is invoked with `--output-format stream-json
--input-format stream-json --print`. In this mode claude reads user
messages from stdin, streams responses, and exits when stdin closes.
`StreamJsonClaudeWrapper` writes one seed message but never closes
stdin (kept open for the user-typing case). For phases where the
skill self-terminates the process, claude exits cleanly. For phases
that finish a turn without a user follow-up (autonomous walker) or
hit an Anthropic error, claude sits idle on stdin forever.
**Fix paths** (deferred):
- Cheap: `BridgeOptions.AutoEndAfterSeed` (env `AGP_AUTO_END_AFTER_SEED`)
  closes stdin right after the seed; autonomous runs use it,
  user-driven runs don't.
- Right: API tracks "is a browser WebSocket attached?". If no client
  connects within N seconds AND no `user_input` has been sent, instruct
  the bridge to close stdin (autonomous mode). Otherwise keep stdin
  open (interactive mode).
The new idle watchdog (E1) catches the stall regardless, but doesn't
prevent it.

---

## Resolution map

| Issue | Status | Where |
|--|--|--|
| A1 phase open assignee gate | deferred | `PhaseEndpoints` |
| A2 run-start role gate | deferred | `RunEndpoints` |
| A3 run-list role visibility | deferred | `RunEndpoints` |
| B1 cost cap persists pause | done | commit `ad16795` |
| B2 verify button | deferred | new UI on phase view |
| B3 enum-as-string | deferred | `Program.cs` JSON config |
| C1 cwd orientation | done | commit `5fee1ed` |
| C2 file tree filter | done (basic) | `RoleFileVisibility` |
| C3 permission-mode strict | blocked on US4 | T129–T143 |
| C4 verification keys | done | `CompletionDetector` |
| D1 hide internal reasoning | deferred | `StreamJsonClaudeWrapper` |
| D2 phase-complete toast | deferred | `Phase.tsx` + ws/inbox |
| D3 refresh stale artefacts | tracked as T161 | `tasks.md` |
| E1 idle stall watchdog | done | commit `89cdcad` |
| E2 dev-loop default paths | docs only | README quickstart |
| E3 port inconsistency | done | commit `a7043a6` |
| E4 modules root resolution | docs only | README quickstart |
| E5 .env.local quoting | docs only | `.env.local.example` |
| E6 inbox active/done + client | done | commit `89cdcad` |
| E7 ANTHROPIC_API_KEY mapping | docs only | `.env.local` comment |
| E8 status integers in UI | done | commit `3c196e9` |
| E9 phase view orientation | done | commit `3c196e9` |
| E10 run inputs invisible | done | commit `89cdcad` |
| E11 SESSION-LOG marker hygiene | deferred | skill prompts + audit |
| E12 bridge observability | partial | commit `89cdcad` |
| E13 synthetic-input lifecycle | deferred | `StreamJsonClaudeWrapper` |
