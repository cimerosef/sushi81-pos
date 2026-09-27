# M14 — PreProd Foundation — worklog

**Status:** ACTIVE CONTROL LEDGER
**Milestone:** M14
**Start baseline:** `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`
**Implementation branch:** `codex/m14-preprod-foundation-authorized`

## Preparation

- 2026-09-27 — owner approved/froze the M14 design.
- 2026-09-27 — controlling decision, acceptance amendment, readiness, implementation contract and owner checklist prepared.
- 2026-09-27 — M14 implementation branch and Draft PR created.
- Issue #4 remains CLOSED while the controller prepares the first executable package.

## Package ledger

| Package | Controller state | Accepted head/evidence |
|---|---|---|
| WP1 — runtime deployment-profile isolation | Not started | — |
| WP2 — dual installer coexistence | Not started | — |
| WP3 — initial PROD→PREPROD seed | Not started | — |
| WP4 — remote environment isolation | Not started | — |
| WP5 — immutable GitHub candidate pipeline | Not started | — |
| WP6 — production promotion pipeline | Not started | — |
| WP7 — owner two-PC acceptance/closure | Not started | — |

## Rules

- Each Codex package is authorized only by its exact PR `CODEX_HANDOFF_READY` record.
- Failed/superseded candidate evidence remains historical; never rewrite it as passing.
- Real production database/customer/order/payment data must never be committed or uploaded as source-repository CI/release evidence.
- Post-launch business bug fixes remain outside M14.
