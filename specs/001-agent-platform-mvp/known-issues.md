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
