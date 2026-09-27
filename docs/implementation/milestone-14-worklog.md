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
| WP1 — runtime deployment-profile isolation | Implemented; controller acceptance pending | Release tests/build passed; exact-head CI pending |
| WP2 — dual installer coexistence | Not started | — |
| WP3 — initial PROD→PREPROD seed | Not started | — |
| WP4 — remote environment isolation | Not started | — |
| WP5 — immutable GitHub candidate pipeline | Not started | — |
| WP6 — production promotion pipeline | Not started | — |
| WP7 — owner two-PC acceptance/closure | Not started | — |

## WP1 — runtime deployment-profile isolation

- Handoff: `M14-WP1-RUNTIME-PROFILE-01` (`CODEX_HANDOFF_READY` comment `5858391155`).
- Implemented the fixed `prod` / `preprod` profiles and the `deployment-profile.txt` evidence seam. The file contains exactly one ASCII profile value (`prod` or `preprod`), optionally followed by one line ending. The fixed installed production and PreProd directories require matching evidence; missing, malformed, unknown or identity-conflicting evidence stops startup before business or authority data is opened. Unpackaged developer/test runs without evidence retain the historical `prod` default.
- Production durable data remains `%LOCALAPPDATA%\Sushi81 POS`; PreProd derives all `IAppPaths` locations from `%LOCALAPPDATA%\Sushi81 POS PREPROD`.
- Added the permanent PreProd window identity/banner, kitchen/customer initial-print and reprint marker, and `PREPROD_` prefix to all default Gestion export filenames. Production title, paths, print content and filename defaults remain unmarked.
- Verification: Release solution tests passed, 946 passed / 0 failed / 0 skipped; Release solution build passed with 0 warnings / 0 errors; `git diff --check` passed. Exact-head CI #921 / run `36346595631` succeeded on implementation commit `11304c80841fd50998e215e5c584b08931c7d9ba`.
- No real production database or business data was used. WP1 remains pending controller review/acceptance; this entry does not mark it accepted.

## Rules

- Each Codex package is authorized only by its exact PR `CODEX_HANDOFF_READY` record.
- Failed/superseded candidate evidence remains historical; never rewrite it as passing.
- Real production database/customer/order/payment data must never be committed or uploaded as source-repository CI/release evidence.
- Post-launch business bug fixes remain outside M14.
