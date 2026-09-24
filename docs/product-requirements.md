# Product requirements

**Status:** Approved — Phase 1 baseline  
**Last updated:** 2026-09-16
**Product:** Sushi81 POS  
**Target use:** Internal operational use by Sushi 81

## 1. Purpose and product role

Sushi81 POS is a focused Windows desktop application intended to replace the Excel/VBA-centered `POS_Caisse.xlsm` workflow used by Sushi 81.

Its role is deliberately narrow: it is a fast, reliable **order-entry and operational relay tool** for telephone and walk-in orders, with printing, payment follow-up, future-order reminders, durable operational storage, catalogue maintenance and controlled export into the existing management workflow.

It is **not** intended to become Sushi 81's full accounting, ERP, CRM, inventory or e-commerce system.

`Gestion SUSHI 81.xlsm` remains in use downstream. Hiboutik remains the web-order platform and the external system in which ordinary POS-originated card revenue must ultimately be represented.

This document defines the V1 product boundary. Detailed behavior is authoritative in the later approved lifecycle, business, catalogue, architecture, data, storage, paste-import, printing and export documents.

## 2. Product principles

V1 follows these priorities:

1. reliability;
2. simplicity;
3. maintainability;
4. operational clarity;
5. novelty only when it does not weaken the priorities above.

The product must be:

- **fast in daily use** — frequent actions require minimal clicks and typing;
- **local-first** — core order-entry and printing do not depend on a hosted backend;
- **reliable** — committed business data survives ordinary restart/failure;
- **business-specific** — implement approved Sushi 81 workflows without unrelated generic POS complexity;
- **maintainable** — UI, business rules, persistence, printing and integrations remain separable/testable;
- **evolvable** — storage, printing, export and Hiboutik fallback changes do not require redesigning the whole application;
- **cost-conscious** — no mandatory recurring paid dependency without explicit approval;
- **compact bilingual UI** — French and Chinese software labels are switchable, with only one interface language displayed at a time.

## 3. Users and deployment model

The application is used by Sushi 81 staff during normal operation and by the owner for review/administration.

The same application feature set is available on every paired Windows computer so another paired device can serve as an operational fallback.

V1 does not require simultaneous writes from multiple computers. The approved architecture uses a controlled single-writer model with non-authoritative read-only access on other paired devices.

No login, employee-account, role or permission system is required in V1.

## 4. V1 product goals

### G-01 — Replace the Excel/VBA POS dependency

Normal order entry, retrieval, modification, payment follow-up, printing, catalogue maintenance and day-to-day POS administration work from the standalone application without requiring Excel/VBA for POS operation.

### G-02 — Preserve and improve fast order entry

The interaction model must be at least as practical as the former UserForm, including convenient quantity editing for items already in the cart.

### G-03 — Maintain durable operational data

The live POS database retains current operational data together with unresolved older orders. Completed older data is archived by complete natural year into separate historical databases according to `storage-strategy.md` rather than being deleted merely to keep a workbook small.

### G-04 — Keep `Gestion SUSHI 81` in the wider workflow

V1 does not replace the management workbook/process. Sushi81 POS produces the approved intermediate export defined in `export.md` for later separate/manual downstream import.

### G-05 — Support pickup, delivery and advance orders

The system supports `Retrait`, `Livraison`, structured planned fulfilment date/time, future-order views and due-today advance-order reminders.

### G-06 — Support reliable printing and reprinting

The system supports kitchen/customer output after new-order confirmation and selective later reprinting from retained or archived order snapshots.

### G-07 — Provide a Hiboutik paste-print fallback

When Hiboutik server-side printing is unavailable, staff can paste the automatic Hiboutik order-summary text, obtain a normal pre-populated Sushi81 order, review/complete it and then confirm/print it through the ordinary workflow.

### G-08 — Make payment and operational summaries explicit

Order status and payment data are separate. V1 supports unpaid, partial and settled situations through cumulative CB/Espèce amounts, cross-day received-payment attribution, overdue follow-up and daily received-payment summaries.

For ordinary POS-originated orders, the current-day POS CB amount is also the amount that must ultimately be represented in Hiboutik. A redundant second permanently displayed amount is not required.

### G-09 — Improve recoverability and maintainability

Backup/recovery, handoff, schema migration, deployment, diagnostics and automated testing are explicit and safer than the workbook-centered design.

### G-10 — Provide switchable French/Chinese UI

French and Chinese software interface strings are switchable. Catalogue/business-entered data is not translated by the UI language switch.

### G-11 — Provide a narrow Hiboutik daily payment reference view

The top Caisse dashboard provides exactly two additional passive read-only values for the current business date: `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`. They are derived from signed effective-date payment adjustments on non-Cancelled `HIBOUTIK_PASTE` orders and remain separate from ordinary POS-originated turnover and received-payment summaries.

## 5. Functional requirements

### 5.1 Catalogue and product selection

**FR-001 — Structured product catalogue**  
The application uses structured current catalogue data covering product internal identity, operator-facing code/name, category, TTC price, VAT, active/inactive state, Retrait-discount eligibility and structured options.

**FR-002 — Product browsing/search/selection**  
The operator can browse/filter by category and search by product code and product name. Products can be added rapidly, including by double-click and an explicit add action.

**FR-003 — Cart management**  
The operator can add/remove products and directly change quantities of items already in the cart using practical quantity controls without recreating the whole line.

**FR-004 — Structured product options**  
Products may define one or more required/optional single- or multi-select option groups. Selected choices remain attached to the relevant order item.

**FR-005 — Option price adjustments**  
Configured options may carry positive, negative or zero price adjustments. The operator may also enter a custom option adjustment with the approved description/validation rules. Adjustments affect the line/order only and never overwrite the catalogue product's base price.

**FR-006 — Historical snapshots**  
Confirmed orders preserve the sale-time product/category/price/VAT/options/adjustment information needed for later viewing, printing, reporting and export. Later catalogue changes do not rewrite historical orders.

**FR-007 — In-application catalogue maintenance**  
The operator can add/edit/activate/deactivate/delete current catalogue records and manage structured options from a dedicated catalogue-management area without editing Excel.

**FR-008 — Catalogue batch import/export**  
V1 provides the complete approved `.xlsx` catalogue import/export workflow, including protected/transparent technical identifiers, preview/validation, atomic commit and explicit add-only mode for first initialization or later new-record batches.

Detailed catalogue semantics are frozen in `catalogue-management.md`.

### 5.2 Order creation and information

**FR-010 — Create order**  
The operator can create an order rapidly from catalogue items.

**FR-011 — Mandatory fulfilment mode**  
Every new order explicitly selects exactly one of `Retrait` or `Livraison`; confirmation is rejected until one is selected.

**FR-012 — Customer/order information**  
Telephone is optional for both modes. Delivery address is optional at initial Livraison confirmation and can be added/corrected later on the same order. A free-text comment remains available.

**FR-013 — Commercial rules**  
Approved Retrait discount, thresholds, Livraison minimum, delivery-fee, VAT and rounding rules are applied consistently according to `business-rules.md`.

**FR-014 — Telephone usability**  
A standard French ten-digit telephone number may be entered continuously and displayed/printed in readable grouped form without burdensome mandatory validation.

**FR-015 — Free-text operational comment**  
The order retains a flexible comment field for preparation instructions, references and information not covered by structured fields.

**FR-016 — Lightweight telephone-history assistance**  
No formal Customer/CRM master is required. Historical orders may be searched by telephone so useful previous address/comment information can be consulted/reused.

**FR-017 — Structured planned fulfilment date/time**  
Planned fulfilment date and optional time are stored separately from comments.

**FR-018 — Future-order entry**  
The normal order workflow supports choosing a future planned fulfilment date without creating a separate order subtype.

**FR-019 — Future-order progression**  
Future/due-today behavior is derived automatically from planned fulfilment date and the persisted advance-order marker defined in `order-lifecycle.md` and `data-model.md`.

### 5.3 Order/payment state

**FR-020 — Separate order and payment concerns**  
Business status and payment information remain separate.

**FR-021 — Payment conditions**  
V1 represents unpaid, partially paid and fully reconciled states from cumulative CB/Espèce amounts relative to the authoritative order total.

**FR-022 — Payment composition**  
The operator records current cumulative Card/CB and Cash/Espèce amounts directly. A separate manually selected CB/Espèce/Mixte category is not required.

**FR-023 — Payment arithmetic**  
The application shows/derives recorded payment totals and the difference from the authoritative order total. An order can be Closed only when CB + Espèce equals that total exactly to the cent.

**FR-024 — Dated payment adjustments**  
The operator UI remains focused on current cumulative CB/Espèce values, while the application internally retains dated signed changes sufficient to attribute only newly received/corrected money to the proper business date.

**FR-025 — Payment correction**  
The operator can retrieve an existing order and correct cumulative CB/Espèce information through the application. Cross-day summary correctness is preserved by internal adjustment data; an operator-facing payment-event ledger is not required.

**FR-026 — Overdue unsettled attention**  
Past planned-fulfilment-date orders that are not Cancelled and are not fully closed/reconciled appear in the overdue-unsettled area.

**FR-027 — Received-payment date attribution**  
Daily received-payment totals use the date the payment delta is effectively received/corrected rather than order creation/fulfilment date.

**FR-028 — Hiboutik card-revenue relationship**  
For ordinary POS-originated orders, the ordinary POS CB amount received is the amount that must ultimately be represented in Hiboutik. V1 does not automatically submit it and does not control the card terminal.

### 5.4 Order lifecycle, modification and retrieval

**FR-030 — Durable confirmation**  
A confirmed order is durably persisted before printing is attempted.

**FR-031 — Retrieve/search order**  
The operator can retrieve/search existing live orders, including by telephone and comment where applicable.

**FR-032 — Modify the same order**  
Every non-cancelled order remains directly modifiable regardless of Open/Closed state. The same stable order ID is retained; V1 does not require supplementary-order chains, mandatory cancel/replace behavior or operator-visible revision history for ordinary changes.

**FR-033 — Cancel without deletion**  
Cancellation retains the business record and recorded payment facts while excluding the order from ordinary active financial/statistical treatment according to approved rules.

**FR-034 — Abandon uncommitted edits**  
The operator can abandon uncommitted edits and return to the last persisted order state.

**FR-035 — Practical order search**  
Normal live search supports useful order fields and prioritizes current operational data.

**FR-036 — Older-order readability**  
Older retained results may be visually de-emphasized so recent operational orders remain easy to scan.

**FR-037 — Current database first**  
Normal lookup searches the live/current database first rather than automatically opening every annual archive.

**FR-038 — Archive access**  
The operator can explicitly select/open a historical archive year for query/reprint.

### 5.5 Retention and annual archive

**FR-040 — Automatic previous-year archive**  
The authoritative device automatically processes the previous complete calendar year on February 1, or the first safe later startup, according to `storage-strategy.md`.

**FR-041 — End-year assignment**  
Closed records use `closed_at` year and Cancelled records use `cancelled_at` year for archive assignment; Open records remain live regardless of age.

**FR-042 — Do not archive unresolved Open orders**  
Open orders remain in the live operational database across year boundaries.

**FR-043 — Reprint retained and archived orders**  
Live and explicitly selected archived orders remain queryable/reprintable from preserved snapshots.

### 5.6 Hiboutik paste-order fallback

**FR-050 — Paste Hiboutik order**  
The operator can paste the plain-text content of the automatic Hiboutik order-summary message into the fallback importer.

**FR-051 — Review before commit**  
Parsing never commits an order directly. Recognized values pre-populate the ordinary order screen for operator review/editing; the ordinary confirmation action creates the durable order.

**FR-052 — Preserve ordinary fulfilment data**  
Reliably present fulfilment mode/date/time, telephone, address and comments may be pre-populated as ordinary editable order fields. A future fulfilment date must not silently become today's date.

**FR-053 — Exact product-code matching**  
Products are matched by exact shared Sushi81/Hiboutik product code. Missing/unknown codes are not silently guessed by product name.

**FR-054 — Complete missing product options normally**  
Because pasted Hiboutik text does not contain Sushi81 product-option selections, every imported product with enabled option groups requires explicit completion/confirmation through the ordinary option UI, including explicit no-selection confirmation for optional groups where appropriate.

**FR-055 — POS pricing authority**  
The authoritative total is calculated by the ordinary Sushi81 pricing engine from current catalogue data and the final reviewed cart. A Hiboutik total present in pasted text does not automatically override that value. The ordinary manual total override remains available afterwards.

**FR-056 — No dedicated emergency-order model**  
A Hiboutik paste-created order uses the same ordinary order UI/lifecycle/printing workflow. V1 does not require a special emergency screen/style/count, original-Hiboutik-total field, discrepancy panel, dedicated Hiboutik reference field, dedicated reconciliation state or `EmergencyImportDetail` entity.

**FR-057 — Hidden anti-double-counting source marker**  
The application retains only the non-user-facing source discriminator required to distinguish a Hiboutik paste-created order from an ordinary POS-originated sale. This marker is automatic and is used to exclude the pasted order from ordinary POS-originated turnover, received-payment summaries, new Hiboutik CB-entry totals and `Gestion SUSHI 81` export.

**FR-058 — Hiboutik daily CB/Espèce reference values**
For business date D, the top Caisse dashboard displays exactly `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`. Each value sums signed `PaymentAdjustment` deltas whose parent order has `source_type = HIBOUTIK_PASTE`, whose current status is not `CANCELLED`, whose `effective_at` belongs to D and whose bucket is respectively `CB` or `ESPECE`. Open and Closed non-Cancelled Hiboutik orders are included; Cancelled orders contribute zero while their adjustment facts remain retained. The values use effective business date rather than `recorded_at`, order creation date or planned fulfilment date, and do not use order totals or current cumulative payment amounts.

This is a separate passive reporting view. It does not change ordinary POS-originated turnover, received-payment, CB or Espèce values and does not introduce a Hiboutik turnover/count/discrepancy metric, special status, dedicated lifecycle, payment workflow, drill-down or emergency dashboard.

Detailed parser behavior is frozen in `paste-order-import.md`.

### 5.7 Printing

**FR-060 — Kitchen ticket**  
The POS generates the approved kitchen ticket from committed data.

**FR-061 — Customer ticket**  
The POS generates the approved ordinary customer/order ticket. Formal B2B invoicing remains outside V1.

**FR-062 — Automatic first print**  
After a new order is successfully committed, one kitchen and one customer document are automatically generated/submitted.

**FR-063 — Selective reprint**  
Kitchen and customer output are independently reprintable from the latest committed state.

**FR-064 — Print failure handling**  
Printing failure never corrupts/removes a committed order and each failed document can be retried independently.

**FR-065 — Modification/cancellation/reprint markings**  
Saving an existing-order modification does not automatically reprint. Explicit reprints carry the approved `RÉIMPRESSION`/`DUPLICATA` markings; Cancelled-order documents remain printable but carry prominent `ANNULÉ`.

**FR-066 — Non-authoritative printing**  
A non-authoritative/read-only paired device may print its locally available committed state after a clear stale/non-authoritative warning; printing does not imply authority transfer.

Detailed behavior is frozen in `printing.md`.

### 5.8 Export to the management workflow

**FR-070 — Structured intermediate export**  
The POS produces the versioned `.xlsx` intermediate export contract defined in `export.md`; it does not directly edit `Gestion SUSHI 81.xlsm`.

**FR-071 — Export eligibility**  
Initial positive-sale export includes only ordinary POS-originated, non-Cancelled, fully settled Closed orders.

**FR-072 — Exclude Hiboutik paste-created orders**  
Hiboutik paste-created orders are always excluded from management export because the underlying sale already exists in Hiboutik.

**FR-073 — Default scope and optional date filter**  
Default export selects every eligible not-yet-emitted order plus applicable pending corrections. There is no mandatory J-2 cutoff. An optional inclusive fulfilment/business-date filter may narrow the set.

**FR-074 — Idempotency and post-export correction**  
An unchanged successful `CREATE` is not emitted twice. A later modification/cancellation of an already-exported order creates an `UPDATE`/`CANCEL` action for the same stable order ID according to `export.md`.

**FR-075 — Exact batch regeneration**  
A previously successful export batch can be regenerated exactly without inventing a new business export action.

### 5.9 Main-screen operational overview and settings

**FR-080 — Received-payment summary**  
The main interface shows at least today's actual amount received, today's CB amount and today's Espèce amount for ordinary POS-originated non-cancelled business activity, based on dated payment changes.

**FR-081 — Due-today advance orders**  
The main interface specifically surfaces advance orders whose planned fulfilment date is now today. Payment/Closed state does not remove the reminder; no separate processed/collected state is required merely to hide it early.

**FR-082 — Future orders**  
The main interface provides a compact future-order count/entry point for orders whose planned fulfilment date is after today.

**FR-083 — Overdue unsettled orders**  
The main interface clearly surfaces past-due unsettled orders.

**FR-084 — No Hiboutik emergency dashboard**  
The hidden source discriminator for Hiboutik paste-created orders must not create a dedicated current-day emergency-order count, special visual order type or discrepancy dashboard.

**FR-085 — Business configuration**  
Normal business values approved as configurable are editable through application settings without source-code changes.

**FR-086 — Diagnostics**  
The application exposes sufficient technical diagnostics for operational failures without unnecessarily exposing/committing sensitive production data.

**FR-087 — Interface language switch**  
The operator can switch software UI strings between French and Chinese; business-entered data remains unchanged.

## 6. Non-functional requirements

**NFR-001 — Windows desktop**  
V1 is a Windows desktop application under the approved .NET/WPF architecture.

**NFR-002 — Local-first operation**  
Core operation does not require a hosted backend.

**NFR-003 — Data durability**  
Committed data survives normal application restart/failure.

**NFR-004 — Recoverability**  
The approved local recovery, GitHub target-directed handoff, OneDrive Disaster Recovery and local annual archive paths are implemented before production use.

**NFR-005 — Safe schema evolution**  
Persistent schema changes are versioned/testable and must not silently reset/discard production data.

**NFR-006 — Separation of program and business data**  
Application binaries and live business data use separate installer/application-data locations; the live database is not required to sit beside the executable or on the same folder/partition.

**NFR-007 — Performance/responsiveness**  
Common search, item addition, quantity changes, order lookup, payment update and navigation feel immediate. Printing must not reproduce recurring multi-second UI stalls from the VBA workflow.

**NFR-008 — Usability**  
Primary live-order workflows minimize unnecessary dialogs and choices.

**NFR-009 — Testability**  
Core business logic, persistence, parsing, archive/export behavior and print-data generation are independently testable.

**NFR-010 — Dependency discipline**  
Prefer free/open-source or first-party dependencies compatible with the approved architecture. Paid/hosted/subscription dependencies require explicit approval.

**NFR-011 — Sensitive-data protection**  
Real customer/order/payment/credential or other sensitive production data must not be committed to Git. Test fixtures are synthetic/sanitized.

**NFR-012 — Maintainability**  
UI, domain/business rules, persistence, printing and integrations have clear boundaries.

**NFR-013 — Lightweight access model**  
No login/employee-account/permission framework is required.

**NFR-014 — Multi-device safety**  
All paired computers can run the full application, while the storage protocol prevents unsafe concurrent writes and silent divergence.

**NFR-015 — Localizable interface**  
French/Chinese UI strings remain separate from business data/logic.

## 7. Explicit V1 exclusions

Unless a later approved specification amendment changes the boundary, V1 excludes:

- inventory/stock and purchasing;
- table-service/table management;
- employee scheduling/accounts/roles/permissions;
- loyalty/membership/marketing systems;
- online-storefront replacement;
- replacement of Hiboutik;
- automatic Hiboutik API/email synchronization;
- direct bank-card-terminal control;
- POS-managed card refund execution/refund-accounting workflow;
- arbitrary order-time overwrite of a catalogue product's base price;
- separate telephone-versus-walk-in classification when it has no operational value;
- special operator-facing Hiboutik emergency-order/reconciliation subsystem;
- formal B2B invoice lifecycle;
- full accounting/ERP replacement;
- replacement of `Gestion SUSHI 81`;
- heavyweight CRM/customer master;
- simultaneous multi-writer database synchronization;
- ordinary user-facing order revision history;
- operator-facing payment-event ledger.

## 8. Cutover from the old POS

Completed historical orders from `POS_Caisse.xlsm` do not need to be migrated.

At production cutover:

- already active/unresolved orders in the old POS may continue to completion there;
- newly received orders from the agreed cutover point are entered into Sushi81 POS;
- no complex one-time historical-order migration utility is required solely for transition.

## 9. Relationship to later approved specifications

Phase 1 originally deferred detailed lifecycle, business, catalogue, architecture, storage, parser, printing and export choices. Those deferrals are no longer unresolved for V1: they have been frozen by the later approved documents and decisions.

The authoritative detail is now distributed as follows:

- `order-lifecycle.md` — status, payment, modification, cancellation, reminders and operational summaries;
- `business-rules.md` — pricing, discount, delivery, option, VAT and rounding rules;
- `catalogue-management.md` — current catalogue, options and batch import/export;
- `data-model.md` — logical persisted business facts/snapshots;
- `architecture.md` — implementation platform and core technical boundaries;
- `storage-strategy.md` — live storage, handoff, recovery and archives;
- `paste-order-import.md` — Hiboutik fallback parsing/conversion;
- `printing.md` — print/reprint behavior;
- `export.md` — POS-to-management intermediate export contract;
- `acceptance-criteria.md` — Phase 5 verifiable V1 acceptance gate.

Where an earlier Phase 1 sentence ever differed from a later approved decision, this file has been aligned during the Phase 5 consistency pass and the later approved semantics are the intended V1 behavior.

## 10. Approval

This document is the **Approved — Phase 1 baseline**, aligned with the later approved V1 decisions during Phase 5.

Approval of this product boundary does not itself authorize production implementation. Production implementation begins only after the Phase 5 repository-wide consistency review and V1 Specification freeze are complete.
