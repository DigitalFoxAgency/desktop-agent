---
description: Final Constitution Check (T163) for Agency Workflow Platform MVP
---

# Final Constitution Check

Re-run of the gates declared in `plan.md` §Constitution Check, executed at the end of Phase 8 against the actual implementation. Verdict per principle, with drift called out.

## I. Code Quality — **PASS (with minor drift noted)**

- `Directory.Build.props` enforces `TreatWarningsAsErrors=true`, `Nullable=enable`, `LangVersion=latest`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=Recommended`. Verified: `dotnet build` is `0 Warning(s) 0 Error(s)`.
- `.editorconfig` + `.globalconfig` carry per-rule severities. The IDE0290 (primary constructor) rule was added late in the slice and triggered cleanup across ~30 pre-existing classes (committed in `45c949f`).
- Public service interfaces have XML doc comments. **Drift**: `contracts/` directory is stale per the pivot note; the contract-test fakes (`FakeBridgeChannel`, `FakeClaudeWrapper`, `FakeRunContainerDriver`) and the `IPhaseSessionService`, `IModulePolicyResolver`, `IConfirmationRepository` interfaces added in Phase 6 have NOT yet been mirrored into `contracts/`. T161 is the bookmark.
- Web client: `tsc --strict` is on; `npx tsc --noEmit` is clean. ESLint + Prettier configured at scaffold (`web/eslint.config.js`, `web/.prettierrc.json`).

## II. Testing Standards — **PARTIAL**

- TDD spirit was followed for new application/bridge logic (tests + impl in the same slice, behaviour-first). For US2/US3/US4 the heavy `WebApplicationFactory` hub tests were deferred to focused integration PRs because they need a live Postgres which isn't available in the local dev loop. Same gating reason in T094–T097, T117–T119, T129–T131, T144.
- **Coverage gate not yet enforced in CI.** `tests/AgentPlatform.Bench/` exists but no coverlet thresholds are wired into `.github/workflows/ci.yml`. Today's count: 26 unit/contract tests pass (Application 15, Bridge 7, Contracts 4) plus the Postgres-bound Api/Infrastructure suites that run in CI. Numbers below 100% branch coverage on critical modules — verified by inspection of the test files; coverlet run is the next step.
- Integration tests against Testcontainers exist (`tests/AgentPlatform.Infrastructure.Tests/PostgresFixture.cs`) and `FakeRunContainerDriver` lives in `tests/AgentPlatform.Contracts.Tests/Fakes/`.
- E2E via Playwright not yet wired (T149/T150 deferred under the cherry-picked Phase 8 scope).

**Recommendation before early-access:** wire the coverage gate (T148-adjacent, ~half-day) and add at least one `WebApplicationFactory` integration test for the confirmation flow (T129 properly).

## III. UX Consistency — **PARTIAL**

- `ConfirmationDialog` is a single shared component; every dangerous action funnels through it (T141/T142 acceptance criterion).
- Tailwind tokens used directly in component classes; no formal `web/src/design/` token module exists yet. **Drift**: design-system module deferred — pragmatic for an MVP but should land before adding a second product surface.
- A11y audit (axe-core in Playwright) deferred (T151).
- Copy is hard-coded in components (English-only is fine at MVP per plan; i18n-readiness is technical debt).

**Recommendation:** if you ship to multiple agencies, extract the design tokens before the styling drift compounds.

## IV. Performance — **DEFERRED**

- `tests/AgentPlatform.Bench/` project exists but has no benchmarks landed (T148 deferred under cherry-pick).
- No CI perf gate. Budgets in plan.md are aspirational, unmeasured.
- No watchdog on synchronous I/O on the API thread.

**Recommendation:** run a manual baseline once dogfooded, then write the regression-only bench suite. The plan's "10% regression CI gate" requires a baseline first.

## Other gates landed in this slice

- **Operational hygiene**: structured logs (Serilog JSON to stdout, T152), health endpoints (`/healthz` + DB-pinging `/readyz`, T155), Postgres backup script (`scripts/backup.sh`, T158), run-container hardening (non-root with pinned uid 1000, capability drops, `no-new-privileges`, PIDs limit, T159).
- **Web**: 404/500/offline error surfaces (T153).

## Summary

Gate verdict for early-access: **conditional pass**. The quality, security, and ops floor is in. The testing and performance gates have known gaps that the team should close before scaling beyond a handful of design-partner agencies — specifically:

1. Wire coverage thresholds in CI (Constitution II).
2. Add at least one `WebApplicationFactory` end-to-end test for the dangerous-action flow (Constitution II + safety-critical path).
3. Land a baseline bench run + regression gate (Constitution IV).
4. Refresh the stale Phase 0/1 artefacts (T161).
5. Wire claude's `--permission-mode=ask` stream-json acceptance protocol so US4 declines genuinely block the action (caveat carried from Phase 6).

None of these are blocking for the first design-partner deployment; all are blocking for "broad early-access" in the spec's sense.
