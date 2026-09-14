# M09 — Hiboutik paste-order fallback — worklog

**Status:** Active implementation evidence log  
**Opened:** 2026-09-14  
**Milestone:** M09 — Hiboutik paste-order fallback  
**Branch:** `codex/m09-hiboutik-paste-fallback`  
**Implementation authorization:** `docs/implementation/milestone-09-authorization.md`  
**Execution gate:** GitHub Issue #4

## Control baseline

- Exact authorized preparation head: `aa1c5a58025f96fef3a4505721186eb1ad05552b`.
- Authorization and current-state governance commits on `main` were completed before this branch was created.
- M08 is Passed / merged through PR #14 at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`.
- M09 implementation is authorized; M10+ remain unauthorized.
- Codex may execute only complete top-level `CODEX_HANDOFF_READY` tasks on the active M09 PR while Issue #4 is OPEN.
- Merge always requires separate explicit project-owner approval.

## Controlling implementation contract

- `docs/implementation/milestone-09-hiboutik-paste-fallback.md`
- `docs/implementation/milestone-09-preparation-readiness.md`
- `docs/implementation/milestone-09-final-manual-acceptance.md`
- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`
- `docs/paste-order-import.md`

## Work-package evidence

### WP1 — Domain/data migration and exact-code seam

Status: **Implemented; evidence captured; awaiting PR review**.

Required scope:

- nullable `source_total_ttc` across domain/persistence;
- safe additive SQLite migration/backward-compatible read/write;
- preservation across ordinary lifecycle modification;
- exact active product-code catalogue query seam;
- focused unit/integration tests;
- no parser/UI implementation yet;
- no M10+ work.

Implementation evidence (2026-09-14):

- Added nullable cent-precise `OrderSnapshot.SourceTotalTtc`, persisted by production SQLite migration version 7 as `orders.source_total_ttc_cents`; existing M08 databases migrate additively and retain authoritative `total_ttc_cents`.
- Added migration-aware order read/write behavior, including a clear pre-migration failure when a non-null source reference is supplied, and preserved the field through ordinary modification/close/cancel lifecycle paths.
- Added the dedicated exact active product-code query seam at the catalogue storage/application boundary; matching is normalized exact-code equality and excludes inactive, name, and prefix matches.
- Added focused M09 WP1 migration, round-trip, exact-code, and lifecycle-preservation evidence; updated existing snapshot comparisons and production migration expectations for version 7.
- Verification: targeted Infrastructure IntegrationTests passed **225/225**; full `Sushi81.Pos.sln` Release test suite passed **579/579**; standalone Release build passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Scope boundary: parser, unresolved-line/session/UI, source-aware provenance, reporting/export, and M10+ remain unimplemented. No manual WPF acceptance is claimed for this WP1 handoff.
