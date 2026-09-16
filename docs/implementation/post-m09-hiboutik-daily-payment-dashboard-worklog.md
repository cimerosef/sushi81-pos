# Post-M09 Hiboutik daily payment dashboard — worklog

**Status:** Implementation complete — owner Windows/WPF manual acceptance pending
**Opened:** 2026-09-16  
**Issue:** #18  
**Branch:** `codex/post-m09-hiboutik-daily-payment-dashboard`  
**Execution gate:** GitHub Issue #4

## Control baseline

- M09 Passed / merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`.
- Approved decision: `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`.
- Approved acceptance amendment: `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`.
- Implementation authorization: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-authorization.md`.
- Implementation contract: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard.md`.
- Owner manual checklist: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-manual-acceptance.md`.
- M10 and later milestones remain unauthorized.

## Authorized outcome

Exactly two passive top-Caisse values:

- Hiboutik CB today;
- Hiboutik Espèce today.

Both use signed `PaymentAdjustment` deltas attributed by effective business date, include only non-Cancelled `HIBOUTIK_PASTE` orders, remain separate from ordinary POS dashboard amounts, and add no schema/write workflow.

## Evidence log

The mandatory docs-first reconciliation was completed and pushed in the first commit of this handoff, `ecee6a1ec9b63e1139f62131a01f131b87b1fdb3`. It records the M09 merge baseline, the authorized independent post-M09 scope, the 2026-09-16 freeze amendment, the narrow product/acceptance/lifecycle/data-model semantics and the current PR #19 / Issue #4 execution state. No production code, tests, schema, migration, dependency, workflow or business behavior was changed by that commit.

The production implementation then extended the existing operational-summary seam with two signed, effective-business-date Hiboutik payment aggregates, read-only view-model projection, two passive Caisse bindings and FR/zh-CN resources. The existing POS summary query and payment/write paths remain unchanged; no schema, migration, dependency, new entity or Hiboutik workflow was added.

Focused evidence passed:

- Post-M09 SQLite reporting test: 1/1 passed, including POS/Hiboutik separation, Open/Closed eligibility, cancellation exclusion with retained rows, signed corrections, effective-date attribution and recorded-date non-attribution.
- Relevant M05 integration tests: 8/8; M09 WP1: 4/4; M09 WP5: 7/7; M05 desktop: 53/53.
- Presentation/fallback/resource/XAML checks passed: 2 presentation refresh tests, 1 application fallback test, 1 passive-binding test and 1 FR/zh-CN localization test.
- Full Release solution suite: 654/654 passed, 0 failed, 0 skipped. Release build: 0 warnings, 0 errors.
- Self-contained `win-x64` `PublishSingleFile=false` verification publish passed. The exact candidate artifact hashes and sizes are recorded in the completion handoff comment.

Owner manual acceptance remains pending and must be performed against the exact candidate. Codex does not declare the owner checklist passed.

## Merge boundary

Implementation completion/manual acceptance never authorizes merge automatically. Separate project-owner merge approval remains required.
