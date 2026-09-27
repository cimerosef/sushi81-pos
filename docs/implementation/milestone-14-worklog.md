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
| WP1 — runtime deployment-profile isolation | Passed | Accepted head `c8ebb4638caac75f2e80715b52e97079fc6288a3`; controller comment `5859507909`; exact-head CI #922 / run `36346911287` SUCCESS |
| WP2 — dual installer coexistence | Implemented; awaiting controller acceptance | Exact-head hosted Windows package/lifecycle CI pending; no controller acceptance recorded |
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
- Verification: Release solution tests passed, 946 passed / 0 failed / 0 skipped; Release solution build passed with 0 warnings / 0 errors; `git diff --check` passed. Implementation-head CI #921 / run `36346595631` succeeded on `11304c80841fd50998e215e5c584b08931c7d9ba`; final exact-head CI #922 / run `36346911287` succeeded on `c8ebb4638caac75f2e80715b52e97079fc6288a3`.
- No real production database or business data was used. Controller accepted WP1 in PR #30 comment `5859507909`. WP1 is Passed; WP2 remains separately package-gated.

## WP2 — dual Prod/PreProd installer coexistence

- Handoff: `M14-WP2-DUAL-INSTALLER-02` (`CODEX_HANDOFF_READY` comment `5859516151`).
- Generalized the existing M13 package builder and Inno script while preserving the frozen production AppId `C7A1B9E2-1E62-4B4B-A2EA-7802814408FC`, product/display name `Sushi81 POS`, per-user binary directory and durable data root. Added stable PreProd AppId `67FB6B75-3C5E-44A5-98AD-305EA4C62D95`, display name `Sushi81 POS PREPROD`, separate per-user binary directory, shortcut and durable data root.
- WP2 packaging performs one `dotnet publish`, adds only a root `deployment-profile.txt` marker to each profile package, and emits source-SHA-bound SHA-256 file/tree evidence. The Prod and PreProd application payloads are compared file-by-file, excluding only that packaging marker. M14 package version is `1.0.1`; legacy production packaging retains its configured `1.0.0` default.
- Added exact-head Windows CI for the authorized branch using signed Inno Setup 6.7.3, full repository build/test, and hosted dual-installer lifecycle verification. Lifecycle CI status: pending at this worklog edit; do not treat the package as accepted until controller review.
- Local verification: Release solution build passed with 0 warnings/errors; Release solution tests passed, 947 passed / 0 failed / 0 skipped; PowerShell parser checks, release-config JSON parsing and `git diff --check` passed. Local Inno compilation and installer lifecycle were not run; lifecycle is guarded to hosted GitHub Actions.
- Lifecycle evidence uses only synthetic fixtures in both durable data roots. No real production/customer/order/payment data was used. Interactive WPF launch smoke remains owner-controlled and is not claimed by CI.

## Rules

- Each Codex package is authorized only by its exact PR `CODEX_HANDOFF_READY` record.
- Failed/superseded candidate evidence remains historical; never rewrite it as passing.
- Real production database/customer/order/payment data must never be committed or uploaded as source-repository CI/release evidence.
- Post-launch business bug fixes remain outside M14.
