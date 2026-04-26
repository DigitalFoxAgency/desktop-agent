<!--
SYNC IMPACT REPORT
==================
Version change: [unversioned template] → 1.0.0
Bump rationale: MAJOR — initial ratification of the constitution; first
concrete definition of project principles and governance.

Modified principles: N/A (initial definition). The five-principle template
has been reshaped into four principles aligned with the user mandate
(code quality, testing standards, UX consistency, performance).

Added sections:
  - Core Principles (I–IV)
  - Quality Gates & Engineering Standards
  - Development Workflow & Review Process
  - Governance

Removed sections:
  - Template placeholder fifth principle (intentional; user requested four)

Templates requiring updates:
  - .specify/templates/plan-template.md             ✅ aligned (Constitution
    Check section delegates to this file; no edits required)
  - .specify/templates/spec-template.md             ⚠ pending review for any
    new mandatory non-functional sections (UX consistency, performance budgets)
  - .specify/templates/tasks-template.md            ⚠ pending review to ensure
    task categorisation reflects testing-discipline & performance principles
  - .specify/templates/checklist-template.md        ⚠ pending review for
    quality / a11y / performance checklist coverage
  - README.md / docs/quickstart.md                  ⚠ not yet present — create
    when project documentation is bootstrapped

Follow-up TODOs:
  - TODO(PROJECT_SCOPE): Confirm whether Desktop Agent ships as a single
    desktop application or a multi-surface product; refine UX-consistency
    scope accordingly.
-->

# Desktop Agent Constitution

## Core Principles

### I. Code Quality (NON-NEGOTIABLE)

All code MUST be readable, reviewed, and maintainable before it lands on
the main branch.

- Every change MUST pass the project's configured linter, formatter, and
  static type checks with zero warnings; suppressions MUST cite a
  justification inline.
- Every change MUST be reviewed by at least one engineer who did not
  author it; self-merges are prohibited except for emergency hotfixes
  that MUST be retroactively reviewed within one business day.
- Functions and modules MUST have a single, documented responsibility;
  cyclomatic complexity SHOULD remain ≤10 per function and refactors
  MUST be opened as separate changes rather than smuggled into feature
  work.
- Public APIs (CLI flags, IPC surfaces, exported library symbols) MUST
  ship with reference documentation in the same change that introduces
  or modifies them.

**Rationale**: Desktop Agent is intended to run unattended on user
machines. Defects compound silently in that environment, so legibility
and review discipline are the primary defence against regressions.

### II. Testing Standards (NON-NEGOTIABLE)

Tests are a contract, not an afterthought. Behaviour without an
automated test does not exist.

- Test-first development is mandatory for new behaviour: tests MUST be
  written and demonstrated to fail before the implementation that makes
  them pass is authored.
- The Red → Green → Refactor cycle MUST be visible in commit history for
  any non-trivial change (more than ~20 lines of production code).
- Coverage thresholds: ≥90% line coverage for libraries, ≥80% for
  application code, and 100% branch coverage for any module that handles
  user data, IPC boundaries, or filesystem mutations. Coverage MUST be
  enforced in CI; drops below threshold MUST fail the build.
- Every feature MUST include integration tests that exercise the real
  IPC, storage, and OS surfaces it touches; mocks are permitted only for
  third-party network services and MUST be paired with at least one
  contract test against the real surface.
- Flaky tests MUST be quarantined within 24 hours and either fixed or
  deleted within five business days; "retry until green" is forbidden.

**Rationale**: Desktop software ships to heterogeneous machines we do
not control. The test suite is the only reliable signal that a release
is safe; weakening it shifts the cost onto end users.

### III. User Experience Consistency

Every user-visible surface MUST feel like one product, not a federation
of features.

- A single design system (tokens, components, copy voice, iconography)
  MUST be the source of truth for all UI; ad-hoc styles or one-off
  components MUST be promoted into the design system before shipping or
  rejected at review.
- Interaction patterns (keyboard shortcuts, focus order, modal
  semantics, error recovery, empty/loading/error states) MUST be
  consistent across surfaces; new patterns require an explicit design
  review and an update to the pattern catalogue.
- Accessibility is a baseline, not a feature: every shipped surface MUST
  meet WCAG 2.2 AA (contrast, keyboard navigation, screen-reader labels,
  reduced-motion preference, target size). Violations block release.
- User-facing text MUST be localisable from day one (no concatenated
  strings, all copy externalised to message catalogues), and platform
  conventions (macOS / Windows / Linux menu order, capitalisation,
  modifier keys) MUST be respected on each target.
- All UX-affecting changes MUST include before/after screenshots or
  recordings in the PR description.

**Rationale**: A desktop agent earns trust through predictability.
Inconsistency reads as instability and trains users to distrust the
product even when the underlying logic is correct.

### IV. Performance Requirements

Performance is a feature with explicit budgets, measured continuously
and enforced at the gate.

- Default budgets (override only with documented justification):
  - Cold start to interactive: ≤2.0 s on the reference machine.
  - Warm interaction p95 latency: ≤100 ms; p99 ≤250 ms.
  - Steady-state idle CPU: ≤1% on the reference machine.
  - Resident memory after 1 hour of typical use: ≤300 MB.
  - Installer/binary size growth per release: ≤5%.
- Every feature plan MUST declare its performance budget and the
  benchmark that proves it; features without a budget MUST NOT merge.
- Performance regressions ≥10% on any tracked metric MUST block release
  until either the regression is fixed or the budget is formally
  amended through the governance process.
- Hot paths MUST be profiled, not guessed: optimisation work MUST cite
  a profile, and micro-optimisations without supporting measurement
  MUST be rejected.
- Long-running or blocking work MUST run off the UI thread; the UI
  thread MUST never exceed 50 ms of synchronous work per frame.

**Rationale**: Desktop agents compete for the user's machine with
everything else they care about. A sluggish or heavy agent is
uninstalled regardless of how correct its behaviour is.

## Quality Gates & Engineering Standards

The following gates MUST be enforced in CI and MUST be green before any
merge to the main branch:

1. **Static analysis gate**: lint, format, and type checks pass with
   zero warnings.
2. **Test gate**: full test suite passes; coverage thresholds from
   Principle II are met.
3. **Accessibility gate**: automated a11y checks (axe-core or
   equivalent) pass for every changed UI surface.
4. **Performance gate**: benchmark suite runs on each PR; tracked
   metrics within budget and within 10% of baseline.
5. **Security gate**: dependency vulnerability scan and secret scan
   produce no high-severity findings.

Pre-release gates additionally require: manual exploratory testing on
each supported OS, a localisation smoke test, and a signed/notarised
build artifact.

## Development Workflow & Review Process

- Work MUST flow through the Spec Kit pipeline: `/speckit-specify` →
  `/speckit-clarify` (when needed) → `/speckit-plan` → `/speckit-tasks`
  → `/speckit-implement`. Skipping stages requires explicit
  justification in the PR description.
- Every plan's "Constitution Check" section MUST enumerate which
  principles the change touches and how each is satisfied; unaddressed
  principles MUST be listed under Complexity Tracking with a
  justification.
- PRs MUST be small enough to be reviewed in one sitting (target ≤400
  lines of diff excluding generated files); larger changes MUST be
  split or accompanied by a written reviewer guide.
- Reviewers MUST verify principle compliance, not just code
  correctness, and MUST request changes when a principle is violated
  even if the code "works".
- Production incidents MUST trigger a blameless post-mortem within five
  business days; resulting action items MUST be tracked to closure.

## Governance

This constitution supersedes ad-hoc practice. Where this document and
any other guideline disagree, this document wins until formally
amended.

- **Amendment procedure**: Open a PR that modifies
  `.specify/memory/constitution.md`, includes an updated Sync Impact
  Report, and updates every dependent template. Amendments require
  approval from at least two maintainers and a 48-hour comment window.
- **Versioning policy**: Semantic versioning of the constitution
  itself.
  - MAJOR — backward-incompatible governance change or removal /
    redefinition of a principle.
  - MINOR — new principle or materially expanded section.
  - PATCH — clarifications, wording, typo fixes, non-semantic edits.
- **Compliance review**: Every PR MUST self-attest compliance via the
  plan's Constitution Check. A quarterly audit MUST sample merged
  changes and verify attestations were accurate; systematic failures
  trigger a mandatory amendment proposal.
- **Exceptions**: Time-boxed exceptions MAY be granted by maintainer
  consensus; each exception MUST have an expiry date and a tracking
  issue. Expired exceptions automatically revert to strict enforcement.
- **Runtime guidance**: Day-to-day agent guidance lives in `CLAUDE.md`
  and the Spec Kit templates under `.specify/templates/`. Those files
  MUST stay consistent with this constitution; drift is treated as a
  bug.

**Version**: 1.0.0 | **Ratified**: 2026-04-26 | **Last Amended**: 2026-04-26
