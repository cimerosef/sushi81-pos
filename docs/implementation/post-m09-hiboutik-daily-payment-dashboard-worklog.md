# Post-M09 Hiboutik daily payment dashboard — worklog

**Status:** Authorized — implementation not yet executed  
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

No implementation evidence yet. Codex must append exact commits/tests/CI/candidate evidence after the authorized handoff executes.

## Merge boundary

Implementation completion/manual acceptance never authorizes merge automatically. Separate project-owner merge approval remains required.