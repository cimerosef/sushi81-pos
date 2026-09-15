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

### WP3 remediation — contract evidence

Status: **Implemented; focused contract evidence captured; awaiting controller review**.

- Added focused Application regressions for the remaining WP3 contract clauses: `1 x Livraison (0)` and final `TOTAL` are ignored and never materialized; repeated rows remain ordered and independently materializable; missing/inactive manual selections leave unresolved state unchanged; no-options products are immediately ready; optional reviewed-empty and required valid option paths materialize ordinary drafts with current category, quantity and selected option IDs.
- Added confirmation evidence for ordinary POS provenance (`SourceType.Pos`, null source total), completed Hiboutik provenance with both reliable and null source totals, the frozen €35.90 source/current-cart separation with Retrait 10% discount (`TotalTtc` €32.31, `SourceTotalTtc` €35.90), final current-catalogue re-fetch, committed-snapshot reload before ordinary initial dispatch, post-save output failure/retrievability, and absence of raw pasted source text from the durable snapshot.
- Production defect exposed/fixed: **none**. This remediation is test/evidence-only; the existing WP3 implementation remained unchanged.
- Verification: targeted WP3 remediation/orchestration tests passed **19/19**; accepted WP2 parser tests passed **27/27**; accepted M09 WP1 Infrastructure IntegrationTests passed **3/3**; full `Sushi81.Pos.sln` Release suite passed **630/630**; standalone Release build passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Scope/topology: no WP4+, WPF/localization, reporting/export, M10+, business/specification or merge work; serial main-agent execution; no owner manual acceptance claimed.

### WP4 — WPF workflow and localization

Status: **Implemented; evidence captured; awaiting controller review**.

- Wired one accepted `HiboutikImportOrchestrator` into the normal Caisse composition. The compact Caisse expander accepts one explicit multiline paste/parse action, retains raw source only in transient view-model state, supports reset/re-import, and never reads the clipboard or persists/logs the pasted text.
- Added localized transient line review with source line/code/quantity/amount context, deterministic unresolved/blocker status, explicit Select product or Ignore actions, positive-quantity capture when needed, and reuse of the existing option-selection dialog for option-enabled products. Imported resolved lines enter the ordinary cart and share ordinary cart editing, pricing, validation, confirmation, retry, and print behavior without a duplicate Hiboutik workflow.
- Final confirmation now includes active import completeness in `CanConfirm` and routes completed sessions through `ConfirmHiboutikImportAsync`; success clears raw/session state while retaining the committed ordinary cart display. Incomplete sessions are disabled and safely rejected programmatically.
- Extended ordinary browser/detail read models and SQLite list/search reads with passive Hiboutik provenance and nullable source amount. The authoritative POS total remains primary; source amount is displayed as reference evidence only and null remains unavailable.
- Added FR/zh-CN resource parity for paste instructions, line review, source evidence, reset, status, errors, and passive source labels. Existing business/source text is preserved when switching language.
- Verification: focused WP4 architecture/WPF/localization tests passed **3/3**; focused browser/search persistence evidence passed **1/1**; the complete `Sushi81.Pos.sln` Release suite passed **634/634** (0 failed, 0 skipped); standalone Release builds passed with **0 warnings, 0 errors**; `git diff --check` passed.
- Scope boundary: no WP5 reporting/integration closure, M10+, schema changes beyond the accepted M09 source-total migration, background clipboard/API/network behavior, duplicate detector, emergency UI, merge, or owner Windows/WPF manual acceptance.

### WP4 remediation — ordinary cart ownership, option quantity, and focused presentation evidence

Status: **Implemented; awaiting controller review**.

- Preserved ordinary cart-line ownership after import materialization. Later unresolved-line resolution/ignore/option-review transitions no longer replace an already-materialized cart draft; ordinary quantity and option/custom-adjustment edits remain operator-owned, and an explicitly removed imported line is not silently reinserted. Duplicate source rows remain independently mapped in transient state, with no durable source-line metadata.
- Threaded the accepted quantity from the reused imported option dialog through `OrderEntryShellViewModel` and `HiboutikImportOrchestrator`. Option review now validates and stores that positive quantity in the ordinary materialized line; cancel/no completion remains pending and does not materialize a row.
- Added focused Desktop/Architecture evidence for quantity/removal stability across later import transitions, duplicate source rows, option/custom edits, missing-quantity manual resolution, explicit ignore, pending/invalid/optional-empty/valid option review, null passive source-total display, FR/zh-CN source-text preservation, and reuse of the single normal Caisse orchestrator seam. Added Application evidence that accepted option quantity round-trips into the ordinary materialized line.
- Corrected the WP4 CI evidence boundary: run #704 belongs to parent commit `890dfb0d3056da5fc8524f876164bd49303f2049` and failed on the culture-sensitive French source-amount test; follow-up commit `128b607a60d2ccb68514cfc130ac2efddfe7a53b` repaired that evidence, and exact-head run #705 was green with 634/634 passed, 0 failed, 0 skipped and a Release build with 0 warnings / 0 errors.
- Verification for this remediation: targeted Application orchestration tests **20/20** passed; targeted Desktop/Architecture M09 evidence **7/7** passed; full `Sushi81.Pos.sln` Release suite **639/639** passed (0 failed, 0 skipped); Release build completed with **0 warnings, 0 errors**; `git diff --check` passed.
- Scope boundary: no WP5/WP6, parser, reporting/export, schema, M10+, merge, or owner Windows/WPF manual acceptance work was started.

### WP5 — integration/regression closure

Status: **Implemented; evidence captured; awaiting controller review**.

- Added `M09Wp5IntegrationTests` as a focused real-SQLite cross-layer regression package. It starts from a genuine `HiboutikImportOrchestrator` creation and exercises the ordinary confirmation, SQLite persistence, lifecycle/payment, reporting and print/reprint seams without adding a Hiboutik-specific production path.
- Evidence covers transient parse/no-write and non-authoritative confirmation blocking; injected parent/child persistence failure with rollback of `orders`, `order_items`, `order_tax_breakdown` and `payment_adjustments`; notifier failure as non-rollback; the frozen source/current-price separation (`source display 99.99` and retained source total **€35.90**, Retrait 10% authoritative total **€32.31**); the ordinary manual total override (**€40.00**) with source reference unchanged; lifecycle edit/payment/Close preservation; and durable privacy checks for the raw source marker.
- Reporting evidence proves Hiboutik turnover and received CB are excluded while an ordinary POS order remains included, and the same Hiboutik order remains visible through live search and planned-date browsing. The existing M05 operational-view tests remain the reusable evidence for generic future/due-today/overdue-unsettled predicates; no distinct CB-to-represent seam exists in the current application, so no speculative one was added.
- Printing evidence proves the committed order is retrievable during initial dispatch, an initial print failure leaves the order committed and retryable, explicit reprint reads the latest committed lifecycle state, and printed customer totals use the authoritative **€32.31** rather than the source total. No Hiboutik-specific print path was introduced.
- Verification: focused WP5 integration tests **5/5**; full `Sushi81.Pos.sln` Release suite **644/644** passed (0 failed, 0 skipped); standalone Release solution build passed with **0 warnings, 0 errors**; `git diff --check` passed; established self-contained `win-x64` Desktop publish smoke passed to ignored `artifacts/m09-wp5-integration-publish/` after the required runtime-target restore.
- Production code changed: **none**. Test/evidence-only package; no M06/WP6 owner-manual acceptance, M10+, export, merge, or business/specification changes. Execution topology: serial main-agent work; no subagents used.

### WP5 remediation — notifier failure and direct operational inclusion

Status: **Verified; accepted by controller review**.

- Added `HiboutikNotifierFailureAfterCommitLeavesOrderReloadable`: the real `HiboutikImportOrchestrator` plus `ConfirmHiboutikImportAsync` path commits one SQLite order before an injected `IDurableChangeNotifier` exception; the order remains reloadable with `HIBOUTIK_PASTE`, its non-null source total, its child row, ordinary initial-dispatch semantics, and no duplicate parent row.
- Added `PasteCreatedHiboutikOrderRemainsInFutureDueAndOverdueOperationalViews`: one genuinely paste-created future order is paid through the ordinary lifecycle seam and queried at successive business dates. It remains present in future, due-today advance, and overdue-unsettled lists/counts with `SourceType.HiboutikPaste`; source classification is not changed to POS, while POS-originated turnover/received-card totals remain zero.
- The previously accepted `HiboutikIsExcludedFromPosReportingButRemainsSearchableAndPaid` regression remains the focused evidence for search/planned-date inclusion and the broader POS money-total exclusion; no distinct current “CB amount to newly represent in Hiboutik” seam exists and no M11 export path was added. Accepted WP1 lifecycle evidence remains reused for the lower-level cancel/source-total preservation contract.
- Production code changed: **none**. This remediation is test/worklog-only; no operational rule, parser, WPF, WP6, M10+, M11, merge, or business/specification behavior was changed. Execution topology remains serial main-agent work with no subagents.

- Final accepted head: `bc6639e4bb4f0f48a74be7155a9de5d9010a79f1`.
- Exact-head GitHub Actions CI #708 succeeded with **646/646** tests passed, 0 failed, 0 skipped; the Release build reported **0 warnings, 0 errors**.
- Controller disposition: **WP5 FINAL DISPOSITION: ACCEPTED** at the exact head above. The self-contained `win-x64` publish smoke from the parent WP5 candidate remains reusable because this remediation changed only tests and evidence documentation.

### WP6 — exact owner-manual-acceptance candidate and M09 traceability

Status: **Documentation/evidence preparation; owner manual acceptance not yet executed**.

WP6 is the final executable M09 preparation package. It does not perform owner Windows/WPF acceptance, check owner checklist boxes, declare M09 Passed, merge PR #17, or authorize M10+.

Repository scope for the WP6 candidate is documentation/evidence-only:

- reconcile this worklog's WP5 remediation status and accepted exact-head evidence;
- reconcile `docs/implementation-status.md` to `Partial` while preserving the accepted WP1–WP5 history;
- prepare `docs/implementation/milestone-09-final-manual-acceptance.md` for owner execution while leaving every A–N and final-disposition checkbox unchecked;
- produce the final exact-head self-contained `win-x64` Desktop candidate after the documentation commit, without committing binaries or real business data.

The accepted WP1–WP5 evidence is mechanically traced below. Each row records the automated evidence already accepted, the manual checklist sections still requiring owner execution, and the WP6 preparation status. No row is a final `Passed` declaration.

| Criterion | Accepted automated evidence reused | Owner manual sections still pending | WP6 preparation status |
|---|---|---|---|
| AC-HIB-001 — paste is an order-creation aid only | WP2 parser tests; WP3 `IncompleteSessionIsBlockedBeforeOrderWrite`; WP5 `NonAuthoritativeHiboutikConfirmationWritesNothingAndMalformedPasteStaysTransient`; WP5 real SQLite no-write/abandon evidence. | A, B, C | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-002 — ordinary order UI/model | WP3 ordinary confirmation/provenance tests; WP4 normal Caisse composition, ordinary cart/option path and architecture evidence; WP5 ordinary lifecycle and print/reprint integration. No emergency entity/workflow was added. | A, D, I, L | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-003 — untrusted plain-text boundary | Pure WP2 parser boundary tests and `SyntheticFixtureContainsNoCustomerOrContactData`; WP4 transient-only/raw-source-clearing and no-clipboard/API architecture evidence; WP5 durable privacy audit. | A, N | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-004 — exact active code plus explicit unresolved disposition | WP1 `ExactActiveProductCodeLookupUsesCurrentCatalogueIdentityOnly`; WP2 exact/no-fuzzy parser tests; WP3 exact resolution, manual selection, explicit ignore and blocker tests; WP4 unresolved review evidence. | B, C | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-005 — mandatory option confirmation | WP3 option-review completeness tests, including reviewed-empty optional groups and required selections; WP4 reused ordinary option dialog, quantity and cancel/pending evidence. | D | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-006 — POS pricing authoritative and source total reference-only | WP1 source-total persistence/lifecycle tests; WP3 `HiboutikConfirmationUsesCurrentPricingAndTransfersReferenceTotalOnly`; WP5 real SQLite €35.90 source reference versus €32.31 Retrait authoritative total and €40.00 manual override. | F, G, I, L | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-007 — product-block scope and ordinary future fulfilment | WP3 ordinary order-field boundary; WP4 normal Caisse workflow and structured ordinary fulfilment-field evidence; WP5 paste-created future/due/overdue operational inclusion regression. Full operator confirmation of ordinary fields remains manual. | E, J | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-008 — system-controlled source discriminator with passive visibility | WP1 browser/search passive source evidence; WP3 provenance/authority tests; WP4 passive Hiboutik/source-total display; WP5 reporting exclusion, search/planned-date and future/due/overdue inclusion. M11 owns the final Gestion export-exclusion cross-check. | I, J, K, N | Automated evidence accepted / owner manual pending; not Passed. |
| AC-HIB-009 — minimal retained source metadata | WP1 nullable cent-precise persistence and lifecycle preservation; WP3 source type plus nullable source total assignment and raw-source absence; WP4 transient-state clearing/passive display; WP5 durable privacy audit. Only system source type and nullable reference total are retained. | I, N | Automated evidence accepted / owner manual pending; not Passed. |

The amended M09 semantics control this matrix: product-block paste only, ordinary manually entered fulfilment/date/time/customer fields, exact active-code matching with explicit unresolved disposition, POS-authoritative pricing, optional nullable reference-only source total, passive read-only source visibility, and system-controlled anti-double-counting. The M11 export implementation/cross-check is not claimed here.

The WP6 exact candidate head and the SHA-256/size values of its self-contained executable and ZIP are intentionally not self-referenced before the documentation commit exists. They are authoritative in the matching durable `CODEX_DONE: M09-WP6-EVIDENCE-MANUAL-ACCEPTANCE-BUILD-11` comment on PR #17 after the final exact-head build/publish.
