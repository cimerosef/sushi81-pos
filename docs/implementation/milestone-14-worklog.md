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
| WP2 — dual installer coexistence | Passed | Accepted head `43adce6a9a59b235c8083ab750be9c393e303a31`; controller comment `5860094105`; exact-head CI #933 / run `36352059967` SUCCESS; artifact `10942766694` |
| WP3 — initial PROD→PREPROD seed | Implementation complete; controller review pending | Synthetic/local evidence below; exact-head CI is reported in the matching `CODEX_DONE` |
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
- Added exact-head Windows CI for the authorized branch using signed Inno Setup 6.7.3, full repository build/test, and hosted dual-installer lifecycle verification. CI #929 / run `36350904134` succeeded on `87cddc22689cd428f115c275436eecaf6b38a118`; its manifest verified 418 byte-identical application files with only the root `deployment-profile.txt` excluded (tree SHA-256 `7aff22a19a61f700c9f1c1453c3ff1368b921e21a9823964d8cbfaebd9101aa9`). Artifact `Sushi81-POS-M14-WP2-dual-installers-1.0.1-87cddc2` / ID `10941489941`, retention 90 days. Earlier exact-head CI #925 / run `36349877217` and #927 / run `36350353333` retain their Inno AppId interpolation failures in Actions history; profile-specific literal AppId branches fixed the compiler error and #929 passed.
- The successful CI log review found that an informational deferred interactive-launch item had been included in the lifecycle passed-count; it was removed from the count. CI #931 / run `36351472590` then passed on `3f67b81d27e3b409151b612e8fd69aee6226e83b`. Final worklog/evidence head `43adce6a9a59b235c8083ab750be9c393e303a31` passed exact-head CI #933 / run `36352059967`, including Windows build/test, signed Inno 6.7.3 compilation, dual-profile lifecycle verification and artifact upload `Sushi81-POS-M14-WP2-dual-installers-1.0.1-43adce6` / ID `10942766694`. Final manifest verified 418 byte-identical common application files with only root `deployment-profile.txt` excluded; application payload tree SHA-256 `05a2890bd9041945f01ee7b5e50d9a6e91cad6ee054fa417db06317a076e9c38`.
- Local verification: Release solution build passed with 0 warnings/errors; Release solution tests passed, 947 passed / 0 failed / 0 skipped; PowerShell parser checks, release-config JSON parsing and `git diff --check` passed. Local Inno compilation and installer lifecycle were not run; lifecycle is guarded to hosted GitHub Actions.
- Lifecycle evidence uses only synthetic fixtures in both durable data roots. Both roots are hash-checked after every install/repair/uninstall/reinstall step; uninstall of either profile re-validates the other installed profile and both durable trees. No real production/customer/order/payment data was used. Controller accepted WP2 in PR #30 comment `5860094105`. Interactive WPF launch smoke remains owner-controlled and is not claimed by CI.

## WP3 — one-time initial PROD→PREPROD seed

- Handoff: `M14-WP3-PROD-TO-PREPROD-SEED-03` (PR #30 comment `5860117635`); authorized start head `92e0f1ea3d071deffb32c82e8424b85e95f92a7c`. Implementation is prepared for controller review; WP3 is not marked accepted.
- Operator entry: launch the installed **Sushi81 POS PREPROD** shortcut on its first pristine run. Before ordinary business database migrations or authority initialization, PREPROD offers a localized explicit seed choice. “Yes” is followed by a separate confirmation; “No” starts empty PREPROD; Cancel exits without seeding. There is no PROD seed action or arbitrary path picker.
- Eligibility is fixed to the same-user PROD `Data\live.db` and the PREPROD profile paths. Seed is unavailable outside the exact PREPROD profile, without a non-empty safe source, or when PREPROD already has a live database, non-empty Data/Recovery/Archive/Cache, unrelated temporary state, reparse-point paths, authority/pairing/handoff/other Config files, remote or printer identity, custom release configuration, or unknown local settings. Only an isolated `local-settings.json` with UI culture and default release values is tolerated.
- Before and immediately before installation, the process guard verifies that the current executable is the installed PREPROD binary and inspects same-name application processes by full executable path. A PROD, additional PREPROD, unknown executable, or unverifiable process state blocks the operation; the application is never terminated automatically.
- PROD `live.db` is opened through a SQLite read-only connection and captured with SQLite backup into PREPROD Temp staging. The source is not migrated or written. The snapshot is checked for integrity, foreign keys, schema/version, allowed tables and metadata; normal migrations and a PREPROD-local recovery snapshot run only against staging. The staged database resets business revision, records non-secret one-time provenance, is checkpointed to a standalone file, and is installed with same-volume no-overwrite `File.Move` only after a final pristine-target check.
- Synthetic SQLite evidence preserves categories, products/options, business settings, orders/items/adjustments/tax/payment state, Gestion export ledger and M12 archive-proof metadata. Synthetic production authority, pairing, handoff, local remote configuration and Recovery/Cache/Logs/Temp files are not copied; canonical annual Archive files are not copied. Post-seed normal startup was exercised through `AuthorityStartupPreflight` and `AuthorityStateCoordinator`: fresh PREPROD authority/device/lineage was established at business revision 0, without production identity. Repeat seed, configured/dirty target, production-running guard, invalid migration, target race, cancellation and post-atomic-boundary recovery cases are covered.
- Local verification: 10 focused `M14InitialProductionSeedTests` passed; the bilingual operator-resource architecture test passed. Full Release solution tests passed: 958 passed / 0 failed / 0 skipped (Domain 33, Application 189, Infrastructure integration 384, Architecture 228, OneDrive feasibility 32 + 92). Full Release solution build passed with 0 warnings / 0 errors; `git diff --check` passed. Exact-head Windows CI is recorded in the matching `CODEX_DONE` after push.
- All fixtures and failure-injection data were synthetic. No real production database, customer/order/payment data, production device, or populated annual archive was used or tested. No manual/device acceptance is claimed. WP4 and later remain unstarted.

## Rules

- Each Codex package is authorized only by its exact PR `CODEX_HANDOFF_READY` record.
- Failed/superseded candidate evidence remains historical; never rewrite it as passing.
- Real production database/customer/order/payment data must never be committed or uploaded as source-repository CI/release evidence.
- Post-launch business bug fixes remain outside M14.
