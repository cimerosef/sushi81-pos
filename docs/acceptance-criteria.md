# V1 acceptance criteria

**Status:** Approved — Phase 5 baseline (V1 Specification), amended 2026-09-17  
**Last updated:** 2026-09-17  
**Product:** Sushi81 POS  
**Purpose:** Convert the approved V1 product, business, lifecycle, catalogue, data, storage, architecture, paste-import, printing and export specifications into verifiable implementation acceptance criteria.

**Approved amendments:** `docs/decisions/target-directed-authority-handoff.md` amends the storage/handoff acceptance contract below. `docs/decisions/github-handoff-transport.md` makes a dedicated private GitHub Release Asset API the normal handoff transport and server acknowledgement path; OneDrive remains separately approved for recovery/archive only. `docs/decisions/filtered-catalogue-bulk-activation.md` adds AC-CAT-013 for filtered current-catalogue bulk activation/deactivation. `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md` adds AC-HIB-010 and clarifies the ordinary POS summary boundary. `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md` clarifies AC-CAT-008 through AC-CAT-011 for the approved M10 Category `short_code` workbook semantics.

## 1. Acceptance principle

This document defines the minimum observable and testable conditions that a Sushi81 POS V1 implementation must satisfy before production use.

Acceptance is specification-based. GitHub `docs/` and approved records under `docs/decisions/` are the authoritative source. Acceptance must not be inferred from the legacy Excel/VBA implementation when the approved V1 specification differs from that legacy behavior.

A criterion may be verified by one or more of:

- automated unit test;
- automated integration/database test;
- deterministic document/parser/export test;
- installer/update test;
- controlled multi-device/storage test;
- manual UI/operational acceptance test.

Pure visual details remain implementation choices unless a criterion explicitly requires operational visibility or clarity.

## 2. Product boundary and application shell

### AC-PROD-001 — Standalone Windows POS

**Given** a supported Sushi 81 Windows workstation,  
**when** Sushi81 POS is installed and launched,  
**then** normal order entry, order retrieval/modification, payment recording, printing, catalogue maintenance and POS administration work without requiring `POS_Caisse.xlsm` or Excel/VBA automation.

**Evidence:** installer smoke test + manual workflow test.

### AC-PROD-002 — Local-first core operation

**Given** the authoritative device temporarily has no Internet/OneDrive connectivity,  
**when** the operator performs normal local order/catalogue/payment/printing work,  
**then** those core functions remain available against the authoritative local database; only operations that intrinsically require GitHub handoff or OneDrive recovery/archive publication may be unavailable.

**Evidence:** controlled offline integration/manual test.

### AC-PROD-003 — No V1 login/role subsystem

V1 must not require employee login, employee accounts, roles or permissions before normal POS use.

**Evidence:** UI/configuration review.

### AC-PROD-004 — French/Chinese UI switch

The operator can switch application interface labels between French and Chinese without translating or modifying catalogue data, addresses, comments or other user-entered business data. Only one UI language is displayed at a time.

**Evidence:** manual UI test + localization resource test where practical.

## 3. Catalogue management

### AC-CAT-001 — Current product identity

Each current product has an opaque internal identity distinct from its editable operator-facing product code. Product codes are unique among current products but may be edited and may be reused after the previous current record releases the code.

Historical orders remain unchanged after code changes/reuse.

**Evidence:** database/integration tests.

### AC-CAT-002 — Category uniqueness

Creating, editing or importing a current category whose operator-facing name is visually equivalent to an existing category name must be rejected, including equivalence caused only by surrounding whitespace or letter case.

**Evidence:** unit + import validation tests.

### AC-CAT-003 — Product maintenance

The catalogue UI supports creating, editing, activating/deactivating and permanently deleting current products, with deletion requiring explicit confirmation.

Product deletion/deactivation must not delete or rewrite historical order snapshots.

**Evidence:** UI + persistence tests.

### AC-CAT-004 — Required product fields

A current product cannot be committed without valid product code, product name, category, TTC price and VAT rate/category. Active state and Retrait-discount eligibility are persisted current catalogue attributes.

**Evidence:** validation tests.

### AC-CAT-005 — Structured product options

Products may have zero or more option groups. Groups support required/optional and single-/multi-select semantics, including valid min/max constraints for multi-select. Individual options support active/inactive state, persisted display order and positive, negative or zero price adjustment.

Invalid option configurations must not be committed.

**Evidence:** unit + catalogue UI tests.

### AC-CAT-006 — Order-entry option validation

Adding a product with enabled option groups invokes the ordinary option-selection workflow and enforces the configured selection rules before the line/order can be validly confirmed. Inactive options are unavailable for new orders.

**Evidence:** UI/domain tests.

### AC-CAT-007 — Custom option adjustment

An operator-entered custom option adjustment may be positive or negative to cent precision and requires a non-empty description. It changes the current order line only and never changes the catalogue product base price.

**Evidence:** domain/UI tests.

### AC-CAT-008 — Catalogue Excel export/update workbook

V1 can export the complete hierarchical catalogue to `.xlsx` with Products, OptionGroups and Options information sufficient for safe update/re-import.

Internal IDs/technical relationship values required for update matching are hidden/protected/non-editable in the normal operator workflow and are not presented as business identifiers.

Category short-code workbook behavior is additionally controlled by `acceptance-criteria-amendment-m10-category-short-code-workbook.md`.

**Evidence:** workbook structure/protection test + manual Excel inspection.

### AC-CAT-009 — Catalogue update import

In normal update mode:

- a valid existing internal ID updates that same current record;
- changing the visible product code while retaining the internal ID edits the same current product;
- a blank internal ID creates a new record;
- corrupted/unsafe technical IDs are rejected rather than guessed.

Category name/short-code update semantics are clarified by `acceptance-criteria-amendment-m10-category-short-code-workbook.md`.

**Evidence:** integration/import tests.

### AC-CAT-010 — Add-only catalogue import

V1 supports an explicit add-only workbook mode with no existing internal IDs. No-ID rows create new records only; they must never be matched to existing products by code/name for implicit update. A code conflict with an existing current product is a blocking error.

The mode is usable for first catalogue initialization and later batches of entirely new records. Category short-code behavior in this mode is controlled by `acceptance-criteria-amendment-m10-category-short-code-workbook.md`.

**Evidence:** empty-catalogue and non-empty-catalogue import tests.

### AC-CAT-011 — Import preview and atomicity

Before commit, catalogue import validates the complete workbook and shows a preview/summary of intended create/modify/activate/deactivate actions plus errors/warnings.

If a blocking error exists, no catalogue change is committed. If validation passes and the operator confirms, all accepted changes commit atomically.

Removing a row from the workbook never implies permanent deletion from the live catalogue.

Category short-code conflicts defined by the M10 acceptance amendment are blocking Errors within this same whole-workbook atomicity rule.

**Evidence:** transaction rollback + UI preview tests.

### AC-CAT-012 — Historical catalogue independence

After an order is confirmed, later changes to product code/name/category/price/VAT/discount eligibility/options/active state or catalogue deletion do not change historical order viewing, printing or export values.

**Evidence:** persistence snapshot regression test.

### AC-CAT-013 — Filtered bulk activation/deactivation

**Given** the operator is in the in-application Catalogue maintenance area and has any combination of code/name keyword search, category filter and Active/Inactive/All status filter, **when** the operator invokes bulk Activate or bulk Deactivate, **then** the complete current filtered Product result is captured before confirmation and the target action, matched count and effective-change count are shown. Products already in the requested state are skipped; zero effective changes perform no business write; explicit confirmation is required; all effective changes commit atomically or none commit; missing/stale/conflicting captured Products fail completely without partial state changes; only Product active state and its normal updated timestamp for changed Products may change; historical snapshots and all other catalogue fields remain unchanged; the catalogue refreshes while preserving filter values; and French and Simplified Chinese labels/messages are available without translating catalogue business data.

**Evidence:** Application request/count/no-op tests; SQLite atomicity, stale/missing rollback, timestamp and aggregate-preservation integration tests; WPF/presentation capture, synchronization, confirmation, refresh, localization and no-bulk-delete tests; manual Windows/WPF checklist.

## 4. Order creation and business rules

### AC-ORD-001 — Fast cart editing

The operator can add/remove cart products and directly change quantities of already-added lines using practical controls without recreating the entire order line.

**Evidence:** manual UI acceptance test.

### AC-ORD-002 — Mandatory fulfilment mode

A new order cannot be confirmed until the operator explicitly selects exactly one of `Retrait` or `Livraison`.

When creating a new order from reusable information from an earlier order, fulfilment mode is not inherited and must be selected again.

**Evidence:** UI/domain validation tests.

### AC-ORD-003 — Telephone and delivery address remain optional

Missing telephone never blocks confirmation for either Retrait or Livraison. Missing delivery address does not block initial Livraison confirmation.

A saved Livraison order can later be reopened and have its address added/corrected on the same order ID.

**Evidence:** UI/lifecycle tests.

### AC-ORD-004 — Telephone display normalization

A standard French ten-digit number entered continuously, for example `0612345678`, may be entered without manual spaces and is displayed/printed in readable grouped form such as `06 12 34 56 78` after save, without making telephone mandatory.

**Evidence:** formatting unit test + print/UI test.

### AC-ORD-005 — Retrait discount

With the default business settings:

- normal Retrait discount = 10%;
- only discount-eligible products receive it;
- positive option surcharges are not discounted;
- negative option adjustments reduce the discountable product amount before discount;
- the discount is not applied if the resulting discounted order total would be below €15.00;
- there is no ordinary force-apply override.

Changing the configured discount rate or post-discount minimum changes the parameter used by the same rule without recompilation.

**Evidence:** pricing unit tests including boundary values.

### AC-ORD-006 — Livraison minimum and delivery fee

With default settings, Livraison requires at least €30.00 of merchandise/commercial amount before any delivery fee is added. A delivery fee cannot make an otherwise-under-minimum order qualify.

Livraison does not receive the ordinary Retrait discount.

Delivery fee is configurable enabled/disabled with a configurable fixed amount, default disabled/€0.00. When non-zero and enabled, it is added after the minimum check and uses fixed 10% VAT.

**Evidence:** pricing/validation tests.

### AC-ORD-007 — Option-adjustment VAT

Positive option adjustments use 5.5% VAT. Negative option adjustments inherit the associated product VAT. The operator does not select the VAT rate for these adjustments during order entry.

**Evidence:** tax unit tests.

### AC-ORD-008 — Monetary rounding

All final business monetary results use cent precision and deterministic round-half-up behavior. Screen values, closing validation, print output and export must not disagree due to different rounding conventions.

**Evidence:** money/rounding regression tests.

### AC-ORD-009 — Authoritative manual total

The order interface exposes one authoritative total field. Ordinary pricing writes its calculated value. The operator may manually replace it. A later price-affecting change recalculates and replaces the override.

While a manual total is authoritative, the final tax snapshot is a single 10% TTC bucket over that total.

**Evidence:** pricing/application tests + manual UI test.

### AC-ORD-010 — Business-setting edits

The operator can edit the approved business settings without recompiling: Retrait discount rate, post-discount minimum, Livraison merchandise minimum, delivery-fee enabled state and fixed delivery-fee amount.

Invalid values are rejected and saved values survive restart.

**Evidence:** validation + persistence + UI tests.

### AC-ORD-011 — Price-affecting current catalogue/settings changes apply prospectively

A current Product/Option/BusinessSettings change affects later pricing according to the new current value but does not rewrite already committed order snapshots.

**Evidence:** cross-slice integration tests.

## 5. Order lifecycle, payments and operational views

### AC-LIFE-001 — Confirmed order durability and same identity

Confirmed order persistence is transactional. Reloading/editing an existing committed order operates on the same persistent order identity rather than creating a replacement order.

**Evidence:** database/integration tests.

### AC-LIFE-002 — Source provenance

Every durable order carries an internal source discriminator sufficient to distinguish ordinary POS-originated orders from Hiboutik paste-created orders for the approved anti-double-counting rules. This is system-controlled, not a normal operator-editable business field.

**Evidence:** persistence/import tests.

### AC-LIFE-003 — Signed payment adjustments and cumulative display

CB/Espèce operator edits are stored as signed adjustment history while the UI shows cumulative CB/Espèce amounts. Negative corrections are supported.

**Evidence:** payment/application/persistence tests.

### AC-LIFE-004 — Effective payment date

Each payment adjustment has an operator/business effective date and a separate recorded timestamp. Daily received-payment summaries use effective date, including later/back-entered payments.

**Evidence:** payment-date attribution tests.

### AC-LIFE-005 — Close/reopen/cancel

An order may close only when cumulative received payment equals the authoritative total. A closed order may automatically reopen when later edits create a difference. Cancellation is explicit and preserves the durable order/history while excluding it from ordinary financial summaries.

**Evidence:** lifecycle/payment tests.

### AC-LIFE-006 — Same-ID modification and abandon

An existing committed order may be modified and saved on the same ID. Abandoning unsaved modification restores/displays the latest committed snapshot rather than persisting edits.

**Evidence:** application/UI tests.

### AC-LIFE-007 — Reuse customer information

The operator can start a new order from reusable customer/contact text from an earlier order without inheriting fulfilment mode or mutating the earlier order.

**Evidence:** UI/application tests.

### AC-LIFE-008 — Post-commit printing boundary

Automatic normal printing is attempted only after durable order confirmation/update commit succeeds. Print failure does not roll back a committed order.

**Evidence:** application/printing integration tests.

### AC-LIFE-009 — Operational turnover excludes non-POS sources and cancelled orders

Ordinary operational turnover summaries include the intended POS-originated non-cancelled orders and exclude Hiboutik paste-created and cancelled orders according to the approved anti-double-counting semantics.

**Evidence:** summary query tests.

### AC-LIFE-010 — Received-payment summaries

Daily total received, CB and Espèce summaries use signed effective-date payment adjustments for ordinary POS-originated non-cancelled orders.

The separately approved post-M09 Hiboutik CB/Espèce values are controlled by `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md` and remain distinct from these ordinary POS totals.

**Evidence:** summary/payment tests.

### AC-LIFE-011 — Advance/future/due/overdue views

The application exposes useful future/due-today/overdue operational views according to the approved planned-fulfilment and unsettled-order rules, without relying on session-only state.

**Evidence:** query + UI tests.

### AC-LIFE-012 — Search by telephone/comment/reference

Persisted orders can be found by the approved live search fields, including telephone and comment/reference text, with results based on persisted current order snapshots.

**Evidence:** query/UI tests.

### AC-LIFE-013 — Dated order browser

Persisted orders are browseable by planned fulfilment date with deterministic operational ordering and without requiring a memorized GUID.

**Evidence:** integration/UI tests.

### AC-LIFE-014 — Historical planned-time fidelity

Structured planned fulfilment time round-trips exactly and is displayed unambiguously in 24-hour `HH:mm` form; evening values such as `18:25` must not become `06:25` without AM/PM.

**Evidence:** persistence + presentation regression tests.

### AC-LIFE-015 — Historical access across live/archive data

The completed V1 provides a normal operator path to search/access relevant historical orders even after annual archive processing, without requiring direct SQLite/file manipulation.

**Evidence:** M12 archive/hydration + UI acceptance.

## 6. Hiboutik paste fallback

AC-HIB-001 through AC-HIB-009 are controlled by `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md` where that amendment supersedes older baseline clauses. The post-M09 dashboard extension is controlled by AC-HIB-010 in its separate amendment.

### AC-HIB-001 — Untrusted paste input

The Hiboutik fallback accepts only operator-pasted plain text through the approved workflow and does not require Gmail/Outlook/API integration or background clipboard monitoring.

**Evidence:** parser/UI boundary tests.

### AC-HIB-002 — Deterministic parsing

The parser recognizes the approved product-detail-block syntax and known harmless total/service lines without turning unknown material lines into silently ignored input.

**Evidence:** deterministic parser fixtures/tests.

### AC-HIB-003 — Unknown material lines fail safe

Every unknown/material source line becomes explicit unresolved state and requires operator resolution or explicit ignore before confirmation.

**Evidence:** application/UI tests.

### AC-HIB-004 — Exact active product-code auto-resolution

Automatic matching uses exact current active Product code only. Fuzzy name/code matching is not used to silently resolve imported rows.

**Evidence:** catalogue/import tests.

### AC-HIB-005 — Ordinary option confirmation

Imported products with enabled option groups use the ordinary option-selection rules, including explicit reviewed state for optional groups.

**Evidence:** application/UI tests.

### AC-HIB-006 — Ordinary pricing remains authoritative

Current Sushi81 Catalogue/settings pricing produces the authoritative order total. Source prices/total may be retained only as the approved read-only reference and never override pricing.

**Evidence:** pricing/import tests.

### AC-HIB-007 — Ordinary order lifecycle

After confirmation, a Hiboutik paste-created order uses the ordinary order identity/lifecycle/payment/printing/search behavior rather than a dedicated emergency subsystem.

**Evidence:** integration/regression tests.

### AC-HIB-008 — Anti-double-counting source exclusion

The source discriminator keeps Hiboutik paste-created orders out of ordinary POS-originated turnover/received/export calculations where specified. Final `Gestion SUSHI 81` export exclusion is cross-checked in M11.

**Evidence:** reporting/export integration tests.

### AC-HIB-009 — Source reference is passive/non-authoritative

Approved passive Hiboutik source indication and nullable `source_total_ttc` remain read-only reference information and do not create a discrepancy/reconciliation/payment subsystem.

**Evidence:** persistence/presentation tests.

### AC-HIB-010 — Daily Hiboutik CB/Espèce values

See `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`.

## 7. Printing and reprinting

### AC-PRINT-001 — Deterministic kitchen/customer models

Kitchen and customer print content is generated deterministically from committed order snapshots and approved receipt identity/configuration.

**Evidence:** print-model tests.

### AC-PRINT-002 — Separate kitchen/customer output

The application supports the approved kitchen/customer print outputs and selected local printer queues.

**Evidence:** Windows printing integration + manual physical print.

### AC-PRINT-003 — Print after commit / failure safety

Print submission occurs after business commit; failure is visible/actionable and never erases/rolls back the committed order.

**Evidence:** failure-path tests + manual test.

### AC-PRINT-004 — Reprint exact current committed snapshot

Reprint uses the latest committed order snapshot and approved reprint marking, not current Catalogue values.

**Evidence:** print/persistence tests.

### AC-PRINT-005 — Selective reprint

The operator can intentionally reprint the required kitchen/customer output without forcing both outputs unnecessarily.

**Evidence:** UI/dispatcher tests.

### AC-PRINT-006 — Cancelled-order marking

Cancelled orders remain reprintable according to the approved cancelled marking rules.

**Evidence:** print regression tests.

### AC-PRINT-007 — Non-authoritative printing

A non-authoritative/read-only device may perform the approved read-only print/reprint behavior with the required warning/staleness semantics but cannot gain business-write authority through printing.

**Evidence:** authority/printing tests.

### AC-PRINT-008 — Local printer configuration

Printer queue selection is local technical configuration and does not travel as business data through normal authority handoff.

**Evidence:** configuration/pairing tests.

### AC-PRINT-009 — Archived order reprinting

M12 historical archive access supports reprinting hydrated archived orders according to the same historical snapshot semantics.

**Evidence:** archive/printing tests.

### AC-PRINT-010 — Customer receipt identity

Customer receipts use the approved Sushi 81 business identity/layout contract without implementing a general B2B invoice workflow.

**Evidence:** deterministic layout + physical owner acceptance.

### AC-PRINT-011 — Reprint marking

Reprints are clearly distinguishable under the approved marking semantics without changing the underlying order's business data.

**Evidence:** print-model/manual test.

## 8. Storage, authority, recovery and archive

### AC-STO-001 — Local SQLite live data

The live operational database is local SQLite in the approved application-data location and is not directly synchronized as a simultaneously writable shared OneDrive database.

**Evidence:** path/configuration/integration tests.

### AC-STO-002 through AC-STO-005 — Pairing and target-directed normal handoff

These criteria are controlled by the approved target-directed authority/GitHub transport amendments: one authoritative writer, explicit target-directed handoff, durable source relinquishment before target acquisition and wrong-target rejection.

**Evidence:** M02/M07 deterministic protocol + integration/manual multi-device tests.

### AC-STO-006 — Local recovery

Durable business mutations trigger validated local recovery snapshots according to the approved scheduling/retention rules, including shutdown flush.

**Evidence:** recovery integration tests.

### AC-STO-007 through AC-STO-009 — Remote recovery / DR

Recovery-only cloud checkpoints, Disaster Recovery candidate ordering/validation/fencing and generation invalidation follow the approved M07 contracts.

**Evidence:** M07 deterministic/integration/manual tests.

### AC-STO-010 — Centralized authoritative-write enforcement

Every business-data mutation is rejected below the UI unless the current device is authoritative under the durable authority state. Missing/corrupt authority state fails closed.

**Evidence:** Application/SQLite/WPF authority tests.

### AC-STO-011 through AC-STO-014 — Annual archive

M12 must implement the approved previous-calendar-year archive eligibility, staged validation/publication, failure-safe removal and explicit historical access/hydration behavior without losing pending export semantics.

**Evidence:** archive integration/failure/manual tests.

## 9. Architecture

### AC-ARCH-001 — .NET/WPF dependency boundaries

Production follows the approved .NET 10/WPF Domain/Application/Infrastructure/Desktop separation and prevents Infrastructure/UI dependencies from leaking into Domain.

**Evidence:** architecture tests/build review.

### AC-ARCH-002 — SQLite migrations

Schema changes are versioned and migration failure does not silently reset business data.

**Evidence:** migration integration/failure tests.

### AC-ARCH-003 — Money/time/ID abstractions

Money precision/rounding, business clock and opaque ID generation use the approved shared abstractions rather than ad-hoc module-specific behavior.

**Evidence:** unit/architecture tests.

### AC-ARCH-004 — Logging/configuration safety

Configuration/logging remains appropriate for local Windows operation and must not persist secrets/customer business data unnecessarily.

**Evidence:** code/privacy review.

### AC-ARCH-005 — `.xlsx` implementation boundary

ClosedXML is isolated behind application-owned workbook/export-import contracts; Excel COM/Interop is not required. Catalogue M10 and Gestion M11 own separate workbook business contracts even if they reuse low-level adapter plumbing.

**Evidence:** dependency/architecture tests + workbook tests.

### AC-ARCH-006 — Printing boundary

Windows print-queue/FixedDocument implementation remains behind Application-owned deterministic print models/contracts.

**Evidence:** architecture/printing tests.

### AC-ARCH-007 — Self-contained Windows release

Final V1 ships through the approved self-contained Windows x64 packaging/installer model without requiring the operator to install a separate .NET runtime manually.

**Evidence:** M13 packaging/install test.

## 10. Export to Gestion SUSHI 81

### AC-EXP-001 through AC-EXP-011

The M11 export must satisfy the frozen `docs/export.md` contract, including selection eligibility, optional inclusive date range, four-sheet versioned workbook, temporary generation/validation/finalization, immutable batch payloads, duplicate prevention, exact regeneration, post-export CREATE/UPDATE/CANCEL correction semantics and Hiboutik source exclusion.

**Evidence:** M11 deterministic export/integration/manual tests.

## 11. Non-functional / final quality

### AC-NFR-001 — Supported Windows release

Final V1 launches and performs core workflows on the supported Sushi 81 Windows environment.

**Evidence:** M13 install/smoke/manual acceptance.

### AC-NFR-002 — Deterministic automated verification

Business/data/authority/parser/print/export/archive rules that can be mechanically verified are covered by deterministic automated tests using synthetic/sanitized fixtures.

**Evidence:** full test suite.

### AC-NFR-003 — Practical operator responsiveness

Normal interactive operations do not block the UI unacceptably; external/printing/storage operations use appropriate asynchronous boundaries while preserving deterministic commit semantics.

**Evidence:** STA/WPF tests + owner workflow acceptance.

### AC-NFR-004 — Actionable failure behavior

Invalid input, corrupt/untrusted workbook/paste data, storage/printing/transport failure and authority rejection fail safely with actionable operator feedback and without silent partial corruption.

**Evidence:** failure-path tests + manual acceptance.

## 12. Acceptance governance

Acceptance evidence must reflect the exact tested source/candidate where the milestone requires an owner executable. Automated green tests do not substitute for real Windows/WPF/Excel/printer/multi-device owner checks when the specification requires them.

A milestone may be marked Passed only after its required automated and manual evidence is complete. Passed does not authorize merge or the next milestone. Merge and later implementation authorization remain separate explicit project-owner actions.