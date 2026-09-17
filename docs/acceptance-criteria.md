# V1 acceptance criteria

**Status:** Approved — Phase 5 baseline (V1 Specification), amended 2026-09-16
**Last updated:** 2026-09-16
**Product:** Sushi81 POS  
**Purpose:** Convert the approved V1 product, business, lifecycle, catalogue, data, storage, architecture, paste-import, printing and export specifications into verifiable implementation acceptance criteria.

**Approved amendments:** `docs/decisions/target-directed-authority-handoff.md` amends the storage/handoff acceptance contract below. `docs/decisions/github-handoff-transport.md` makes a dedicated private GitHub Release Asset API the normal handoff transport and server acknowledgement path; OneDrive remains separately approved for recovery/archive only. `docs/decisions/filtered-catalogue-bulk-activation.md` adds AC-CAT-013 for filtered current-catalogue bulk activation/deactivation. `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md` adds AC-HIB-010 and clarifies the ordinary POS summary boundary.

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

**Evidence:** workbook structure/protection test + manual Excel inspection.

### AC-CAT-009 — Catalogue update import

In normal update mode:

- a valid existing internal ID updates that same current record;
- changing the visible product code while retaining the internal ID edits the same current product;
- a blank internal ID creates a new record;
- corrupted/unsafe technical IDs are rejected rather than guessed.

**Evidence:** integration/import tests.

### AC-CAT-010 — Add-only catalogue import

V1 supports an explicit add-only workbook mode with no existing internal IDs. No-ID rows create new records only; they must never be matched to existing products by code/name for implicit update. A code conflict with an existing current product is a blocking error.

The mode is usable for first catalogue initialization and later batches of entirely new records.

**Evidence:** empty-catalogue and non-empty-catalogue import tests.

### AC-CAT-011 — Import preview and atomicity

Before commit, catalogue import validates the complete workbook and shows a preview/summary of intended create/modify/activate/deactivate actions plus errors/warnings.

If a blocking error exists, no catalogue change is committed. If validation passes and the operator confirms, all accepted changes commit atomically.

Removing a row from the workbook never implies permanent deletion from the live catalogue.

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

**Evidence:** shared monetary-library unit tests + cross-module regression tests.

### AC-ORD-009 — Editable authoritative order total

There is one authoritative order-total field. The operator may directly overwrite it without changing catalogue prices and without supplying a mandatory reason.

Any later price-affecting change to products, quantities, options/adjustments, discount application or applicable delivery fee automatically recalculates the normal total and replaces the previous manual override.

**Evidence:** domain/UI tests.

### AC-ORD-010 — VAT after manual total override

While a manual total override is authoritative, the final persisted tax snapshot contains exactly one 10% VAT bucket covering the complete authoritative TTC total. A later normal price-affecting recalculation restores the ordinary mixed VAT calculation until another manual total edit occurs.

**Evidence:** tax snapshot tests.

### AC-ORD-011 — Business settings are editable without code changes

The application settings UI allows the operator to edit and persist at least:

- Retrait discount rate (default 10%);
- minimum total after Retrait discount (default €15.00);
- Livraison merchandise minimum (default €30.00);
- delivery-fee enabled/disabled (default disabled);
- fixed delivery-fee amount (default €0.00).

Changing those values changes the parameters used by the already-approved rules without recompilation/reinstallation. Delivery-fee VAT remains fixed at 10% and is not exposed as an ordinary configurable VAT setting.

**Evidence:** settings UI + persistence + pricing integration tests.

## 5. Lifecycle, payment and operational summaries

### AC-LIFE-001 — Commit creates durable order before printing

Confirming a valid new order allocates a stable order ID and durably commits the complete order before any automatic print submission is attempted.

An application restart after successful commit but before/while printing must not lose the order.

**Evidence:** failure-injection persistence/printing integration test.

### AC-LIFE-002 — Status model

User-facing business status is limited to Open, Closed and Cancelled. Future/due-today/overdue are derived operational views, not destructive status replacements.

**Evidence:** domain model test.

### AC-LIFE-003 — Current cumulative CB/Espèce fields

The operator records current cumulative Card/CB and Cash/Espèce amounts directly. No separate manually selected CB/Espèce/Mixte payment-method category is required.

Composition is derived from the two amounts.

**Evidence:** UI/domain tests.

### AC-LIFE-004 — Close arithmetic

An order may be closed only when current CB + current Espèce equals the authoritative order total exactly to the cent. Underpayment or overpayment blocks close with a clear arithmetic error while leaving the order editable/open.

**Evidence:** boundary validation tests.

### AC-LIFE-005 — Dated payment adjustments and effective-date correction

Changing a cumulative CB/Espèce amount persists the signed delta with both:

- an effective business date/time used for received-payment attribution;
- an application-generated recorded timestamp showing when the adjustment was actually persisted.

Normal payment entry defaults the effective date to the current business date so same-day work requires no extra step.

When a payment is entered/corrected after the fact, the operator can change the effective payment date to the date on which the money was actually received. The recorded timestamp remains the actual later persistence time.

Examples:

- CB €20 on day 1 then changed to €50 on day 2 contributes €20 to day 1 and only €30 to day 2;
- €20 CB actually received on day 1 but first entered on day 2 can be assigned effective date day 1, contributing €20 to day 1 while remaining technically recorded on day 2.

Changing the effective date must not create a duplicate payment or alter external card-terminal state.

**Evidence:** multi-day/back-dated payment integration tests + UI test.

### AC-LIFE-006 — Main-screen received-payment summary

The main interface shows at least today's total received, today's CB received and today's Espèce received for ordinary POS-originated non-cancelled orders, based on payment deltas effective today.

Unpaid value is not counted merely because an order exists or is due today. A later `recorded_at` timestamp does not move a legitimately back-dated effective payment into the later day's business summary.

**Evidence:** reporting/UI tests including same-day, cross-day and back-dated entry.

### AC-LIFE-007 — Main-screen operational turnover

The main interface provides the current business date's operational turnover. For date D, ordinary non-cancelled POS-originated orders contribute their full current authoritative total when their planned fulfilment date is D, independent of payment/closure state.

**Evidence:** reporting/UI tests covering unpaid/partial/closed/future/cancelled orders.

### AC-LIFE-008 — Future and due-today advance orders

A non-cancelled order saved with a planned fulfilment date later than the then-current business date permanently sets its persisted advance-order marker.

The main interface surfaces a due-today advance-order reminder when planned fulfilment date = today, the marker is true and status is not Cancelled. Payment or Closed state does not remove that reminder, and no extra processed/collected state is required merely to hide it early.

**Evidence:** date-transition + main-screen UI tests.

### AC-LIFE-009 — Overdue unsettled

The main interface clearly surfaces an order as overdue unsettled only when planned fulfilment date is before today, it is not Cancelled and it is not fully closed/reconciled under the approved lifecycle.

**Evidence:** reporting/UI tests.

### AC-LIFE-010 — Modification uses the same order

Every non-cancelled order remains modifiable regardless of Open/Closed state. Saving a modification retains the same order ID and latest saved business version. V1 does not create supplementary-order chains or mandatory cancel-and-replace flows for ordinary edits.

If an edit makes a previously Closed order fail CB + Espèce = total, it becomes Open again until the close condition is re-satisfied.

**Evidence:** lifecycle integration tests.

### AC-LIFE-011 — Abandon uncommitted edit

Cancelling/abandoning an in-progress modification leaves the last persisted order unchanged.

**Evidence:** UI/persistence test.

### AC-LIFE-012 — Cancellation retains history

Cancelling an order retains the order and its recorded CB/Espèce facts, sets Cancelled state/timestamp and removes it from ordinary active turnover, ordinary received-payment summaries and initial positive-sale export.

**Evidence:** lifecycle/reporting tests.

### AC-LIFE-013 — New order from prior customer information

The operator can start a new order from reusable telephone/address/comment information from an existing order. The new order receives a new ID only when confirmed and does not inherit source products, payment amounts, total, status, planned fulfilment date/time or fulfilment mode.

**Evidence:** UI/domain test.

### AC-LIFE-014 — Future-order count and entry point

The main interface provides a compact count/entry point for non-cancelled orders whose planned fulfilment date is after today. Opening that entry point allows the operator to inspect the actual future orders and dates.

**Evidence:** reporting/main-screen UI test.

### AC-LIFE-015 — Live order search, telephone/comment lookup and explicit archive access

Normal order lookup searches the live/current database without automatically opening every archive and supports practical lookup by at least telephone and comment text.

Historical order information can be used to consult/reuse prior telephone/address/comment values without introducing a Customer master.

The operator can explicitly select/open an annual archive and search/inspect its read-only historical orders.

**Evidence:** live-search + telephone/comment + archive-selection UI/integration tests.

## 6. Hiboutik paste-order fallback

### AC-HIB-001 — Paste import is only an order-creation aid

Pasting/parsing Hiboutik order-summary text must not itself create a durable order. Successful parsing returns the operator to the ordinary order-entry screen with recognized ordinary fields pre-populated for review/editing.

**Evidence:** parser/UI integration test.

### AC-HIB-002 — No special emergency-order UI/model

A Hiboutik paste-created order uses the same ordinary order screen, lifecycle, editing and printing workflow as a manually created order.

V1 must not expose or require:

- dedicated emergency-order screen/style/count;
- Hiboutik discrepancy panel/status;
- Hiboutik-original-total field;
- dedicated Hiboutik order-number field;
- `EmergencyImportDetail` business entity;
- Hiboutik-specific payment/reconciliation workflow.

**Evidence:** UI/schema review + regression test.

### AC-HIB-003 — Untrusted plain-text boundary

Paste import does not automatically access Gmail/Outlook, scrape Hiboutik, call a Hiboutik API, monitor the clipboard or execute embedded HTML/script content. Parser failure/unsupported format performs no business write.

**Evidence:** architecture/parser tests.

### AC-HIB-004 — Exact product-code matching

Parsed product lines match current Sushi81 products by exact shared product code. Missing/unknown product code is never silently replaced using fuzzy name matching; it requires operator correction before confirmation.

**Evidence:** parser tests.

### AC-HIB-005 — Mandatory option confirmation for imported products with options

Every pasted product whose current catalogue product has enabled option groups requires operator confirmation through the ordinary option UI before order confirmation, including all optional groups. Optional groups permit explicit confirmation of no selection.

**Evidence:** parser + option workflow tests.

### AC-HIB-006 — POS pricing is authoritative

After product matching and option confirmation, the ordinary Sushi81 pricing engine calculates the authoritative total from current catalogue prices and final reviewed cart. A total copied from Hiboutik text never silently overrides this calculation.

The ordinary manual total override remains available afterwards.

**Evidence:** parser/pricing regression test where pasted total differs from POS calculation.

### AC-HIB-007 — Future fulfilment preserved

When a reliable future fulfilment date/time exists in the pasted source, it is preserved as structured ordinary order data and is not silently replaced by today's date.

**Evidence:** parser date tests.

### AC-HIB-008 — Hidden source discriminator only

A non-user-facing source discriminator identifies a Hiboutik paste-created order solely to enforce anti-double-counting boundaries. The operator cannot see/edit/manage this discriminator as a business field.

A paste-created order is automatically excluded from:

- ordinary POS-originated operational turnover;
- ordinary POS-originated received-payment summaries;
- the ordinary POS CB amount that must newly be represented in Hiboutik;
- export to `Gestion SUSHI 81`.

**Evidence:** schema + reporting/export tests.

### AC-HIB-009 — No retained dedicated source metadata

V1 does not require raw pasted-email retention, immutable Hiboutik source total, dedicated source reference, parser fingerprint or dedicated duplicate-management subsystem. If desired, the operator may manually write the Hiboutik reference in the ordinary comment field.

**Evidence:** schema review.

### AC-HIB-010 — Hiboutik daily CB/Espèce dashboard

The approved post-M09 amendment adds exactly two passive read-only values to the top Caisse dashboard: `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`. For business date D, each value sums signed `PaymentAdjustment` deltas whose parent order has `source_type = HIBOUTIK_PASTE`, whose current status is not `CANCELLED`, whose `effective_at` belongs to D and whose bucket is respectively `CB` or `ESPECE`.

Open and Closed non-Cancelled Hiboutik orders are included. Cancelled Hiboutik orders contribute zero while their retained payment-adjustment rows remain durable. Effective business date controls attribution; `recorded_at`, order creation date and planned fulfilment date do not substitute. The two values remain strictly separate from ordinary POS-originated operational turnover, received total, CB and Espèce values. No Hiboutik turnover, combined received total, order count, discrepancy, dedicated lifecycle, reconciliation workflow, schema migration, new durable field or write path is introduced.

The values reuse the existing dashboard refresh/current-business-date behavior, are localized in French and Simplified Chinese, and remain visible/read-only on a non-authoritative device without weakening authority rules.

**Evidence:** focused source/status/date/bucket/signed-delta reporting tests; application and view-model projection tests; XAML/localization parity tests; Windows/WPF manual acceptance on the exact candidate.

## 7. Printing

### AC-PRINT-001 — Automatic initial printing

After a new order has been successfully committed, the application automatically generates/submits one kitchen ticket and one customer ticket using that committed state.

**Evidence:** print-service integration test.

### AC-PRINT-002 — Initial print content

Kitchen output includes at least order ID, creation/confirmation time, fulfilment mode, planned fulfilment date/time when present, optional telephone/address, full comment, ordered lines in saved order with quantity/code/name, attached options/adjustments and current authoritative total.

Customer output includes at least ordinary Sushi 81 business identity, order/date/fulfilment information, item descriptions/prices, authoritative TTC total, persisted VAT breakdown and applicable current payment information.

**Evidence:** deterministic print-model snapshot tests + manual layout review.

### AC-PRINT-003 — Future-order prominence

When an order is confirmed while its planned fulfilment date is in the future, both initial documents make that future date/time operationally prominent enough that it cannot reasonably be mistaken for a same-day order.

**Evidence:** manual print acceptance test.

### AC-PRINT-004 — Print failure never rolls back order

Failure to generate/submit either print job does not delete, roll back or corrupt the committed order. The UI identifies which document failed and permits independent retry.

**Evidence:** failure-injection test.

### AC-PRINT-005 — Saved modifications do not auto-reprint

Saving any modification to an existing order automatically prints neither kitchen nor customer document. After save, the operator may independently reprint either, both or neither.

**Evidence:** integration/manual test.

### AC-PRINT-006 — Reprint uses latest committed state

Explicit reprint never prints unsaved edits as authoritative data. It uses the latest successfully committed state available to the device and retains the same order ID.

**Evidence:** UI/integration test.

### AC-PRINT-007 — Reprint marking

An explicit kitchen reprint visibly shows `RÉIMPRESSION`. An explicit customer reprint visibly shows `DUPLICATA`.

**Evidence:** print-model test.

### AC-PRINT-008 — Cancelled-order printing

A Cancelled order remains printable/reprintable, but every kitchen/customer document generated from its current Cancelled state displays a prominent `ANNULÉ`. On a reprint, the reprint marking also remains present.

**Evidence:** print-model/manual visibility test.

### AC-PRINT-009 — Archived-order printing

An explicitly selected archived order can be viewed and reprinted from its historical snapshots without dependence on the current catalogue or current VAT settings and without writing to the archive database.

**Evidence:** archive/print integration test.

### AC-PRINT-010 — Non-authoritative-device printing

A non-authoritative/read-only paired device may print/reprint from the committed live-data copy it actually holds. The UI clearly warns that the device is non-authoritative and data may be stale, but printing is not hard-blocked and does not imply authority transfer or freshness verification.

**Evidence:** multi-device UI/integration test.

### AC-PRINT-011 — No B2B invoice subsystem

V1 customer printing remains the ordinary customer ticket/restaurant note and does not introduce company-master, formal invoice numbering, credit-note lifecycle or B2B invoice template workflow solely for invoicing.

**Evidence:** scope/schema review.

## 8. Export to the Gestion workflow

### AC-EXP-001 — Eligibility

An order is eligible for initial positive-sale export only when all are true:

- source is ordinary POS, not Hiboutik paste-created;
- status is not Cancelled;
- payment is fully settled;
- lifecycle status is Closed.

**Evidence:** export-selection tests.

### AC-EXP-002 — Default and optional date scope

With no date filter, export considers all eligible not-yet-successfully-emitted orders plus applicable pending post-export corrections. There is no mandatory legacy J-2 cutoff.

The operator may optionally apply inclusive start/end fulfilment/business-date filtering; the selected range is visible before execution.

**Evidence:** export-selection/UI tests.

### AC-EXP-003 — Intermediate `.xlsx`, not direct workbook mutation

POS generates a controlled intermediate `.xlsx` and does not write directly into `Gestion SUSHI 81.xlsm`.

The workbook contains the versioned logical sheets `Meta`, `Orders`, `OrderLines` and `TaxBreakdown` with the contract fields defined in `export.md`.

**Evidence:** workbook contract test.

### AC-EXP-004 — Historical snapshot fidelity

Export uses committed historical order/line/payment/tax snapshots. Current catalogue prices/options/VAT must never replace sale-time values in an old order export.

**Evidence:** regression test after catalogue changes.

### AC-EXP-005 — Safe generation

Export writes to a temporary/staging path, validates schema/relationships/counts before finalization, and marks a batch successfully emitted only after the final intermediate file is generated and validated. Failure leaves the business order state unchanged and remains retryable.

**Evidence:** failure-injection integration test.

### AC-EXP-006 — Duplicate protection

An unchanged successfully emitted `CREATE` is not emitted again by an ordinary later export. Technical export metadata distinguishes legitimate later corrections from accidental duplicate sale export.

**Evidence:** repeated-export tests.

### AC-EXP-007 — Exact successful-batch regeneration

The operator can regenerate the exact payload of a previous successful batch using the same `BatchId` without creating a new business export event and without substituting a later order version.

**Evidence:** export-ledger/payload test.

### AC-EXP-008 — Modification before first export

An order modified before its first successful export is emitted once as `CREATE` using its latest committed state; no `UPDATE` is generated merely because pre-export edits occurred.

**Evidence:** export test.

### AC-EXP-009 — Post-export update

Modifying an already successfully exported order creates a pending `UPDATE` for the same stable `OrderId`. The later export contains a full replacement order/line/tax snapshot rather than a second sale or line delta.

**Evidence:** export correction test.

### AC-EXP-010 — Post-export cancellation

Cancelling an already successfully exported order creates a pending `CANCEL` for the same stable `OrderId`. It is not emitted as another positive sale.

**Evidence:** export correction test.

### AC-EXP-011 — Contract data types

Money is exported as numeric euro values with business cent precision; dates/times are Excel date/time values; IDs/product codes/action values are text; correctness does not depend on formulas. Schema identifiers and action values are not translated with the UI language.

**Evidence:** workbook cell-type test.

## 9. Storage, handoff, recovery and archive

### AC-STO-001 — Local live database

Each paired device has its own application-managed `%LOCALAPPDATA%\Sushi81 POS\Data\live.db`. The live database is never the file directly opened/written from a OneDrive-synchronized folder.

**Evidence:** installation/path review.

### AC-STO-002 — N-device single writer and target-directed normal transfer

The design supports more than two paired devices without fixed SHOP/HOME slots. At most one device is authoritative/writable at any moment; all others are non-authoritative/read-only.

Normal transfer is directed by the current authoritative source to exactly one eligible paired `target_device_id`. Non-target devices never compete for the same handoff through file claims/election and cannot become writable merely because they are running or observe the handoff.

**Evidence:** three-device protocol tests + design inspection.

### AC-STO-003 — Close semantics and safe target-directed handoff ordering

When the authoritative operator chooses **Close and retain authority**, the application closes without releasing write authority; the same device remains authoritative for its next valid launch and all other devices remain read-only.

When the operator explicitly chooses **Transfer authority and close**, the source:

1. selects/preselects exactly one eligible paired target;
2. blocks new business edits and commits accepted writes;
3. creates and validates a SQLite-safe complete snapshot;
4. assigns immutable lineage/generation/version/source/target/checksum metadata;
5. publishes `YYYYMMDDHHMMSS.snapshot.db` to the configured private GitHub Release and requires HTTP 201, uploaded state, exact name/size/asset ID and matching `sha256:<hex>` server receipt;
6. durably records local relinquishment/pending-transfer state that survives restart and blocks further source business writes;
7. only after that durable relinquishment creates/uploads `YYYYMMDDHHMMSS.grant.json` and validates its strict GitHub server receipt;
8. only after the grant receipt persists `Released`; retention cleanup is post-completion and retryable.

A ready/grant marker must never exist before durable source relinquishment. A snapshot alone does not release authority. A failure before relinquishment may safely abort without releasing authority; a failure after relinquishment leaves the source read-only/pending-transfer and permits only technical retries of the same immutable target-bound transfer.

**Evidence:** deterministic ordering/restart/failure-injection tests + GitHub fake-HTTP receipt tests + manual close-flow acceptance.

### AC-STO-004 — Target-bound handoff validation and acquisition

A receiving device must not promote a handoff snapshot to writable `live.db` unless:

- its immutable `device_id` exactly equals `target_device_id`;
- source/target are distinct valid paired devices for the lineage/generation;
- snapshot and matching target-bound ready/grant marker are available;
- lineage/generation/version/source/target/checksum and required metadata match;
- SQLite integrity passes;
- stale/replayed local transfer state is rejected;
- the snapshot is safely restored and authoritative local state is durably established.

Only after all checks pass may that target enable business writes. A non-target device remains read-only and does not create an acquisition claim.

**Evidence:** corruption/mismatch/wrong-target/replay/restart tests + GitHub asset/grant metadata and hash validation + controlled multi-device test.

### AC-STO-005 — No silent force takeover, source rollback or target substitution

If the authoritative device closed while retaining authority, another device cannot silently start writing from an older local copy or GitHub artifact.

After a target-directed handoff crosses durable source relinquishment:

- the former source cannot silently resume ordinary writes;
- the handoff cannot be retargeted to another device through normal flow;
- a third device cannot substitute itself for the target;
- the designated target must complete/recover the handoff, or genuine inability to do so requires explicit Disaster Recovery.

**Evidence:** protocol/restart/UI tests including target-unavailable paths.

### AC-STO-006 — Local recovery generation and retention

Important durable business changes trigger local recovery protection, including at least new-order confirmation, saved order modification, payment changes, Close/Cancel, catalogue changes/import and business-setting saves. A short debounce/coalescing delay may combine nearby saves without weakening protection.

The application retains the latest five successfully generated/validated local recovery snapshots. Older cleanup occurs only after a newer valid replacement exists.

**Evidence:** save-trigger + debounce + recovery-retention tests.

### AC-STO-007 — Target-directed handoff retention

GitHub Handoff retains the latest three complete validated immutable snapshot+target-bound-grant units after successful completion. Incomplete/starter/stray assets are never counted; each complete unit preserves matching lineage/generation/version/source/target/checksum metadata and exact remote asset identity.

**Evidence:** retention/integrity/target-binding tests.

### AC-STO-008 — Change-triggered disaster-recovery checkpoints

While the authoritative device is in normal use, if durable business data has changed since the previous cloud checkpoint, the application publishes a validated recovery-only checkpoint, subject to a maximum normal publication frequency of one checkpoint every 15 minutes. If no durable data changed, a redundant checkpoint is not required.

The latest five validated disaster-recovery checkpoints are retained. Recovery-only checkpoints do not themselves release write authority and cannot be consumed automatically as a normal handoff.

**Evidence:** changed/unchanged scheduler + frequency + retention + authority tests.

### AC-STO-009 — Explicit disaster recovery creates new generation

Using Disaster Recovery requires explicit operator confirmation of possible data loss, validates the selected checkpoint and creates a new lineage generation before writes are enabled. It is reserved for genuine abnormal inability to complete/recover the normal authoritative or target-directed path, not ordinary target substitution.

Devices returning with an older generation cannot resume writing or consume an old target-bound grant without reinitialization.

**Evidence:** multi-device recovery/generation tests.

### AC-STO-010 — Non-authoritative and pending-transfer read-only mode

A non-authoritative device clearly displays that live data may be stale and blocks authoritative business writes such as creating/modifying orders, payments, lifecycle changes, catalogue imports/edits, business-setting changes and archive execution.

The same write block applies to:

- non-target paired devices;
- a former source after durable relinquishment;
- a designated target before acquisition validation completes;
- stale/old-generation devices.

A former source in pending-transfer state may perform only technical retries needed to finish the same immutable already-fixed handoff. Printing remains governed by AC-PRINT-010.

**Evidence:** authority-state/pending-transfer UI + centralized write-guard tests.

### AC-STO-011 — Annual archive trigger

On February 1, or the first later startup when the authoritative device can safely act, POS processes only the previous complete natural/calendar year. Delayed execution never expands the archive period into the current year's January.

**Evidence:** date/scheduler tests.

### AC-STO-012 — Archive-year assignment

Archive year is based on when the business order ends:

- Closed order -> year of `closed_at`;
- Cancelled order -> year of `cancelled_at`;
- Open order -> remains in live database regardless of age.

The same rule applies whether the hidden source discriminator is ordinary POS or Hiboutik paste-created.

**Evidence:** year-boundary archive tests.

### AC-STO-013 — Archive publication safety

Eligible records are removed from `live.db` only after a complete archive database is staged, validated, published to OneDrive Archive and publication/synchronization succeeds. Any failure leaves the records live and retryable.

**Evidence:** archive failure-injection test.

### AC-STO-014 — Permanent read-only annual archives

Annual archives remain independent read-only SQLite historical databases, survive application reinstall, are retained permanently unless deliberately managed outside normal POS workflow, and remain queryable/reprintable through explicit archive selection.

**Evidence:** reinstall/archive access test.

## 10. Architecture, deployment and data integrity

### AC-ARCH-001 — Frozen platform

V1 implementation uses .NET 10 LTS, WPF, SQLite and Microsoft.Data.Sqlite as the approved core application stack unless an explicit specification amendment replaces it.

**Evidence:** project/dependency inspection.

### AC-ARCH-002 — Monetary persistence

Persisted euro-denominated business money uses integer euro cents; business calculation uses decimal semantics and the approved round-half-up rules. Binary floating-point persistence/calculation must not determine business monetary results.

**Evidence:** schema/code tests.

### AC-ARCH-003 — SQLite durability profile

Live database connections enforce foreign keys and the approved conservative durability profile, including WAL, synchronous FULL and a finite busy timeout target of five seconds. Multi-row business saves are transactional and write transactions are not held while awaiting user input, printing or OneDrive synchronization.

**Evidence:** connection configuration/integration tests.

### AC-ARCH-004 — Versioned safe migrations

Schema changes are explicit/versioned/testable. A migration failure must not silently delete/recreate/reset production business data.

**Evidence:** migration upgrade/failure tests.

### AC-ARCH-005 — `.xlsx` library boundary

Catalogue and POS export workbook operations use the approved ClosedXML application-service boundary without requiring Excel COM automation. Workbook parsing/generation failures cannot partially commit business state.

**Evidence:** dependency/code inspection + failure tests.

### AC-ARCH-006 — Windows printing boundary

Printing uses application-owned deterministic print data/document generation followed by Windows print-queue/spooler integration. Business persistence does not depend on printer success.

**Evidence:** architecture test/inspection.

### AC-ARCH-007 — Installation/update separation

V1 is delivered as a self-contained Windows x64 WPF application using the approved per-user Inno Setup approach. Updating/reinstalling binaries preserves `%LOCALAPPDATA%\Sushi81 POS` business data, device identity, configuration and durable authority/transfer state.

V1 does not require a background self-update service.

**Evidence:** install/upgrade/reinstall tests.

## 11. Reliability, security and performance

### AC-NFR-001 — Sensitive data is not committed to Git

Repository test fixtures use synthetic/sanitized data. Real customer telephone numbers, addresses, orders, credentials or other sensitive production data are not committed.

**Evidence:** repository review.

### AC-NFR-002 — Deterministic core tests

Business pricing, lifecycle arithmetic, payment-date attribution, catalogue import validation, Hiboutik parsing, storage/archive selection, export generation and print-data generation are testable without relying solely on manual production UI interaction.

**Evidence:** automated test suite review.

### AC-NFR-003 — Responsive normal workflow

Common product search, add/quantity changes, order lookup, payment update and navigation behave interactively without recurring multi-second stalls. Print spooling must not freeze the UI while Windows performs physical printing.

**Evidence:** manual performance acceptance + targeted timing tests where useful.

### AC-NFR-004 — Clear failure behavior

Operational failures such as parser failure, print submission failure, invalid catalogue import, failed/partial target-directed handoff, invalid snapshot, migration failure and export generation failure produce actionable operator feedback and do not silently corrupt/replace authoritative business data or re-enable a relinquished source.

**Evidence:** failure-path acceptance tests.

## 12. Explicit V1 exclusions to verify

Acceptance must confirm that implementation has not introduced mandatory V1 subsystems outside the approved scope, including:

- inventory/stock or purchasing;
- table-service/table management;
- employee accounts/roles/permissions;
- loyalty/membership/marketing systems;
- replacement of Hiboutik or automatic Hiboutik API synchronization;
- direct card-terminal control or POS-managed refund execution;
- full accounting/ERP functionality;
- replacement of `Gestion SUSHI 81`;
- heavyweight CRM/customer master;
- formal B2B invoice lifecycle;
- ordinary order revision-history UI;
- operator-facing payment-event ledger;
- special Hiboutik emergency-order UI/reconciliation model;
- live SQLite database synchronization through OneDrive;
- simultaneous multi-writer database operation;
- generic competitive OneDrive claim/election for normal authority transfer;
- hosted/Graph/OAuth coordination merely to arbitrate normal V1 handoff;
- Git history, Git LFS, Actions artifacts, Packages or filesystem synchronization as the normal handoff transport.

## 13. V1 acceptance gate

V1 may be accepted for production preparation only when:

1. every criterion above is either demonstrably passed or explicitly marked not applicable by an approved specification amendment;
2. all required automated tests pass on the production-target build;
3. installer/update/recovery/export/printing critical paths have controlled acceptance evidence;
4. no unresolved contradiction exists between implementation and the frozen/amended V1 specification;
5. no test fixture or repository artifact contains unsanitized production customer/business secrets.

This document is the **Approved — Phase 5 acceptance baseline for the frozen V1 Specification, amended 2026-08-28 for target-directed authority handoff and GitHub Release Asset transport**.

Codex implementation must treat these criteria as the acceptance contract. Any future behavior change that conflicts with them requires an explicit approved specification amendment; implementation must not silently waive a criterion by reproducing legacy VBA behavior that the approved V1 specification intentionally replaced.
