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

### WP1 remediation — exact provider wiring and lifecycle evidence

Status: **Implemented; awaiting controller review**.

- Wired production `SqliteCatalogueStore` through `IActiveProductCodeQueries`, so `OrderEntryCatalogueService` reaches the direct SQLite exact-code provider path; retained the existing safe summary fallback for non-provider implementations.
- Added an instrumented application test that fails if the direct-provider lookup silently enumerates active products or reloads through the edit query.
- Added focused lifecycle evidence proving a non-null `SourceTotalTtc` survives payment modification, price-affecting repricing, `CloseAsync`, and `CancelAsync` without changing authoritative POS total semantics.
- Verification for this remediation was accepted by the controller at `08ec453f2f91eadf671faf6aeb239e891dc7da91`: exact-head GitHub CI run #699 is green, the Release build completed with 0 warnings / 0 errors, and the exact-head CI total is 584 passed / 0 failed / 0 skipped. Local `CODEX_DONE` reported 492/492 because the local count omitted one 92-test OneDrive-feasibility project; the controller accepted GitHub CI as the controlling verification.

### WP2 — Pure parser and synthetic fixtures

Status: **Implemented; remediation evidence captured; awaiting controller review**.

- Added the pure Application-layer `HiboutikProductBlockParser` and immutable parse DTOs. The parser performs only deterministic text normalization and line classification into product candidates, specifically known ignored lines, or unresolved lines; it has no catalogue, persistence, authority, clipboard, WPF, network, or business-write dependency.
- Implemented the approved product grammar with ordered quantity/code candidates, repeated-code preservation, the exact `1 x Livraison (0)` ignored service row, strict per-item/final total recognition, and cent-precise source-total reliability. A unique parseable final `TOTAL` takes precedence unless final-total evidence is malformed or competing; without that final total, complete and unambiguous associated per-item totals are summed, while malformed, incomplete, or stray per-item evidence makes only that fallback derivation unavailable.
- Added 24 focused parser tests covering the 21 required cases plus committed-fixture regression, transient unresolved context, and delivery derivation. The committed synthetic fixture remains the only copied fixture and yields a reliable **€24.30** source total with delivery excluded from product candidates; the privacy test confirms no customer/contact data.
- Verification: targeted parser/Application tests passed **24/24**; targeted M09 WP1 Infrastructure IntegrationTests passed **3/3**; full `Sushi81.Pos.sln` Release suite passed **608/608**; standalone Release build passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Execution topology: serial main-agent implementation and review; no subagents used because the pure parser and its focused tests share one narrow contract.
- Scope boundary: no WP3 orchestration, catalogue resolution, source-aware confirmation, WPF workflow/localization, reporting/export, clipboard integration, or M10+ work. No Windows/WPF owner manual acceptance is claimed for this non-UI handoff.

### WP2 remediation — strict money grammar and final-total precedence evidence

Status: **Implemented; evidence captured; awaiting controller review**.

- Rejected dangling decimal separators in `HiboutikProductBlockParser.TryParseMoney`; `5.` and `0.` are now unresolved rather than normalized to whole euros, while the approved integer, one-decimal, and two-decimal forms remain unchanged.
- Added focused synthetic tests for dangling final/per-item totals and for the approved precedence rule: a unique valid final `TOTAL 24.30` remains reliable when a malformed per-item `Total : 5.` is also present, and that malformed line remains unresolved.
- Corrected the WP2 evidence wording above so malformed per-item evidence is described as blocking only fallback per-item derivation; malformed or competing final-total evidence remains the final-total reliability failure case.
- Verification: targeted parser/Application tests passed **27/27**; targeted M09 WP1 Infrastructure IntegrationTests passed **3/3**; full `Sushi81.Pos.sln` Release suite passed **611/611**; standalone Release build passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Execution topology: serial main-agent implementation and review; no subagents used.
- Scope boundary: no WP3 orchestration, catalogue lookup, persistence, WPF/localization, reporting/export, clipboard/network integration, M10+, or business/specification changes. No Windows/WPF owner manual acceptance is claimed for this non-UI remediation.

### WP3 — Application import orchestration

Status: **Implemented; evidence captured; awaiting controller review**.

- Added the transient `HiboutikImportSession` and per-line state model. Start/import preserves source-row order, repeated rows, source text, parsed quantity/code, ignored-row classification, and the nullable reliable source total without retaining any durable raw paste data.
- Added exact active-code resolution, explicit unresolved-line product selection or ignore transitions, current-product re-fetch on manual selection, and deterministic blockers for unresolved rows, missing positive quantities, empty imports, and pending option review.
- Added explicit option-review completeness, including reviewed-empty optional groups, while delegating ordinary active/required/min/max option validation to the shared pricing service. Resolved lines materialize as ordinary `OrderLineDraft` values suitable for the existing order-entry workflow.
- Added the dedicated `ConfirmHiboutikImportAsync` route. It assigns `OrderSourceType.HiboutikPaste` and the session's nullable `SourceTotalTtc` inside the existing authority/current-catalogue/pricing/persistence/notifier/reload/print pipeline; ordinary `ConfirmNewOrderAsync` remains POS-originated with no source total.
- Added 10 focused Application tests covering exact/no-fuzzy resolution, unresolved/manual quantity, explicit ignore/materialization, option-review completeness, no-write incomplete confirmation, source-vs-POS pricing, source metadata, and authority blocking.
- Verification: targeted WP3 orchestration tests passed **10/10**; full `Sushi81.Pos.sln` Release test suite passed **621/621**; standalone Release build passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Execution topology: serial main-agent implementation and review; no subagents used because WP3 changes shared Application confirmation contracts and the transient import-session layer.
- Scope boundary: no WPF workflow/localization, reporting/export, clipboard/network integration, WP4+, M10+, or business/specification changes. No Windows/WPF owner manual acceptance is claimed for this non-UI handoff.
