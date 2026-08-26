# Product requirements

**Status:** Draft — Phase 1, final review  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Target use:** Internal operational use by Sushi 81

## 1. Purpose and product role

Sushi81 POS is a focused Windows desktop application intended to replace the Excel/VBA-centered `POS_Caisse.xlsm` workflow currently used by Sushi 81.

Its role is deliberately narrow: it is a fast, reliable **order-entry and operational relay tool** for telephone and walk-in orders, with printing, short/medium-term operational storage, payment follow-up, future-order reminders and controlled export into the existing management workflow.

It is **not** intended to become Sushi 81's full accounting, ERP, CRM, inventory or e-commerce system.

`Gestion SUSHI 81.xlsm` remains in use and must continue to receive the approved POS order/sales export. Hiboutik remains the web-order platform and the operational destination in which card revenue must ultimately be represented.

The product must preserve the speed and business-specific simplicity of the current workflow while improving reliability, payment-state handling, data durability, searchability, printing and maintainability.

This document defines the Phase 1 product boundary. Detailed business rules, lifecycle semantics and architecture are intentionally deferred to later project documents.

## 2. Product principles

The v1 product should behave like a tool built specifically for Sushi 81 rather than a generic commercial POS platform.

Core principles:

- **fast in daily use** — frequent actions require minimal clicks and typing;
- **local-first** — core order-entry and printing work must not depend on a hosted backend;
- **reliable** — committed order/payment data survives ordinary restart or application failure;
- **business-specific** — implement approved Sushi 81 workflows without unrelated POS complexity;
- **maintainable** — UI, business rules, persistence, printing and integrations remain separable and testable;
- **evolvable** — printing, export, storage and Hiboutik-support changes should not require redesigning the whole product;
- **cost-conscious** — no mandatory recurring paid dependency without explicit approval;
- **compact bilingual UI** — French and Chinese interface labels are switchable, but only one interface language is shown at a time to preserve screen space.

## 3. Users and deployment model

The application is used by Sushi 81 staff during normal operation and by the owner for end-of-day review and administration.

The same application and feature set must be available on both the shop computer and the home computer so that either machine can replace the other in an emergency.

The normal workflow does not require simultaneous use of the live database from both computers. The target architecture must prevent or safely handle conflicting concurrent access.

No login, employee-account, role or permission system is required in v1.

## 4. v1 product goals

### G-01 — Replace the Excel/VBA POS dependency

Normal order entry, retrieval, modification, payment follow-up, printing, catalogue maintenance and day-to-day administration must work from the standalone application without requiring Excel/VBA for POS operation.

### G-02 — Preserve and improve fast order entry

The interaction model must be at least as practical as the current UserForm and remove known friction such as inconvenient quantity editing.

### G-03 — Maintain durable operational data

The live POS database should retain the current natural year's operational data together with any older unpaid or partially paid orders that are not yet eligible for archive. Completed data from prior natural years must be manually archivable by year into separate archive databases instead of being deleted merely to keep an Excel workbook small.

### G-04 — Keep `Gestion SUSHI 81.xlsm` in the workflow

The POS remains an order-entry/relay system. It must continue to provide the approved transfer/export required by `Gestion SUSHI 81.xlsm`; v1 does not replace the management workbook.

### G-05 — Support pickup, delivery and future orders

The system must support Sushi 81 pickup and delivery workflows and explicitly handle orders placed in advance for a later fulfilment date.

### G-06 — Support reliable printing and reprinting

The system must support kitchen/customer printouts at order creation, payment-updated customer reprints and later reprinting of retained or archived orders.

### G-07 — Provide a Hiboutik emergency-print fallback

When Hiboutik server-side printing fails, staff must be able to paste the text of the automatic Hiboutik order-summary email into the POS, review the parsed result and print the order locally.

### G-08 — Make payment status and reconciliation explicit

Order state and payment state must be separate. The POS must support unpaid, partial and settled situations, actual cash/card splits, overdue-payment follow-up and clear end-of-day reconciliation information.

For normal POS-originated orders, today's POS card total is also the amount that needs to be represented in Hiboutik. The UI therefore does not need to show a redundant second amount when both figures are identical. Exceptional Hiboutik emergency-import discrepancies must instead be surfaced explicitly when they exist.

### G-09 — Improve recoverability and maintainability

Backup, restore, schema evolution, updates and automated testing must be safer and more explicit than in the workbook-centered design.

### G-10 — Provide switchable French/Chinese UI

French and Chinese versions of software interface text must be available through a compact switch or setting. Catalogue/product data and user-entered business data are not translated by the language switch.

## 5. Functional requirements

### 5.1 Catalogue and product selection

**FR-001 — Structured product catalogue**  
The application must use a structured Sushi 81 catalogue containing the approved product identity, category, price, VAT, active/inactive and discount-related attributes.

**FR-002 — Product browsing, search and selection**  
The operator must be able to browse/filter products by category and search by both product code and product name. The order-entry workflow must preserve a convenient quantity input before adding a product. Products must be addable both by double-click and by an explicit add action.

**FR-003 — Cart management**  
The operator must be able to add/remove products and directly change the quantity of an item already in the cart using a practical quantity field and/or `+`/`-` controls without reopening a separate edit dialog.

**FR-004 — Product options/choices**  
Products may define one or more selectable choices such as flavour or menu variant. These choices must be selectable during order entry and remain attached to the relevant order item.

**FR-005 — Option price adjustment**  
A configured product option may have a predefined price adjustment such as `+0 €`, `+1 €` or `+2 €`. The order-entry workflow must also allow an operator-entered custom **option price adjustment** when the real-world situation requires an amount not present among the presets.

This custom amount changes the option/add-on adjustment only. Ordinary order entry must not allow the operator to overwrite the catalogue product's base unit price.

Exact validation and UI behavior will be defined in `catalogue-management.md` / `business-rules.md`.

**FR-006 — Historical item snapshot**  
When an order is committed, the order must preserve the product information needed to reconstruct that historical sale, including sale-time name, base price, VAT and selected options/price adjustments. Later catalogue edits or imports must not rewrite historical orders.

**FR-007 — In-application catalogue maintenance**  
Catalogue maintenance must be possible without editing Excel. The operator must be able to add products, edit approved catalogue/commercial attributes, activate/deactivate products and manage product options.

**FR-008 — Catalogue batch import/export**  
The application must provide practical batch import/export using an approved tabular format such as Excel or CSV. Exact columns, validation, preview and conflict handling will be specified in `catalogue-management.md`.

### 5.2 Order creation and customer/order information

**FR-010 — Create order**  
The operator must be able to create an order rapidly from catalogue items.

**FR-011 — Fulfilment mode**  
Orders must support pickup (`Retrait`) and delivery (`Livraison`).

**FR-012 — Required fulfilment information**  
The system must enforce information required by the selected fulfilment mode, such as delivery address where applicable.

**FR-013 — Commercial rules**  
Approved discount, threshold and delivery rules must be applied consistently. Exact rules will be frozen in `business-rules.md`.

**FR-014 — Telephone usability**  
Telephone entry must automatically support readable French-style grouping. Telephone must not be assumed mandatory for every order; exact situations in which it is required or optional will be defined in the later business rules.

**FR-015 — Free-text operational comment**  
A flexible order-level comment field must remain available for preparation instructions, special requests and information not covered by structured fields.

**FR-016 — Lightweight telephone-history assistance**  
No formal customer-profile/CRM system is required. When a telephone number has appeared previously, the operator should nevertheless be able to consult useful historical information such as previously used delivery addresses or relevant comments/preferences when practical.

**FR-017 — Structured planned fulfilment date/time**  
The relevant pickup/delivery date and time must be recordable separately from free-text comments.

**FR-018 — Future-order entry**  
The normal order-entry screen must provide a simple option to mark an order as a future order and choose its future fulfilment date. Same-day ordering remains the default and must not require an extra step.

**FR-019 — Future-order modification and automatic progression**  
A future order must remain modifiable/cancellable according to the approved lifecycle. Before its fulfilment date it appears in the future-orders area. When that date arrives, it must automatically leave the future-orders area and appear in the **due-today advance-order reminder** without requiring manual state changes. If its fulfilment date is edited, the appropriate reminder bucket updates automatically.

### 5.3 Order state and payment state

**FR-020 — Separate order and payment state**  
Order lifecycle state and payment state must be represented separately.

**FR-021 — Payment states**  
The target payment model must support at least:

- unpaid / payment pending;
- partially paid;
- fully settled.

Exact labels and transitions will be frozen in `order-lifecycle.md`.

**FR-022 — Payment composition**  
The POS must record the actual approved payment method/composition associated with an order.

**FR-023 — Structured partial/mixed payment amounts**  
The operator must be able to record actual cash and card/non-cash amounts. The application must show order total, amount already paid and remaining balance and verify mathematical consistency.

**FR-024 — Payment-event history**  
The preferred v1 data model is to record each actual payment event separately with at least date/time, amount and payment method. This supports partial payments, cross-day settlement, daily received-payment totals and settlement-year assignment. Free-text notes are not the primary payment ledger.

**FR-025 — Payment finalization and correction**  
The operator must be able to retrieve an existing order, add/correct payment information and mark it fully settled without editing the database directly.

A payment that was recorded incorrectly must be correctable through the application. The target design should preserve enough history/audit information to understand that a correction occurred, while presenting the operator with the final corrected payment result for normal daily work.

**FR-026 — Overdue unsettled attention**  
An order must appear in the overdue-unsettled area only when its planned fulfilment date is earlier than the current date and its payment state is not fully settled. Normal same-day unpaid orders and legitimate future orders must not appear there.

**FR-027 — Received-payment date attribution**  
Operational daily received-payment totals are based on the date money is actually received, not the order creation or fulfilment date. For partial payments, only the amount actually received on a date contributes to that day's totals.

**FR-028 — Hiboutik card-revenue relationship**  
For ordinary POS-originated orders, the card amount received on a given day is also the amount that must ultimately be represented/entered in Hiboutik for that day.

Because these values are normally identical, the main interface does not need a permanently duplicated "amount to enter in Hiboutik" number when it would simply repeat today's POS card total. v1 does not require automatic submission to Hiboutik.

Direct control of the card terminal is outside v1.

### 5.4 Order lifecycle, modification and retrieval

**FR-030 — Persist order**  
A confirmed order must be durably stored according to the approved lifecycle.

**FR-031 — Retrieve order**  
The operator must be able to retrieve an existing order when permitted by the lifecycle.

**FR-032 — Modify order**  
Orders must remain modifiable according to the approved lifecycle, including future orders.

For already settled orders, v1 should not silently rewrite financial history. Common post-payment changes may be handled through a supplementary order for added items/amounts or an approved cancel/replace flow when the amount is reduced. The actual external card refund remains handled by the card terminal rather than the POS. Exact linking/replacement semantics will be frozen in `order-lifecycle.md`.

**FR-033 — Cancel order without deleting history**  
Cancellation must not silently destroy business history that must be retained.

**FR-034 — Abandon uncommitted edits**  
The operator must be able to abandon uncommitted changes and return to the persisted order.

**FR-035 — Practical order search**  
Search/filtering must support telephone number and comment text, with other useful order fields available if approved during UI design.

**FR-036 — Older-order readability**  
When current operational search results include older retained orders, the UI should visually de-emphasize older entries (for example muted/grey styling) so recent orders are easier to scan.

**FR-037 — Current database first**  
Normal order search prioritizes the live/current database. Archived years are not automatically searched during every normal lookup.

**FR-038 — Archive access**  
The operator must be able to explicitly select/open an archived year and search or inspect archived orders.

### 5.5 Retention and annual archive

**FR-040 — Manual natural-year archive**  
Completed older data must be manually archivable by natural year into a separate archive database.

**FR-041 — Settlement-year assignment**  
For Sushi 81's archive/reporting-year rule, an order that crosses a year boundary while unpaid belongs to the natural year in which it becomes fully settled. Original creation and fulfilment dates remain unchanged in the record.

**FR-042 — Do not archive unresolved payments**  
Unpaid or partially paid orders must remain in the live operational database even if their creation/fulfilment date belongs to an older year.

**FR-043 — Reprint retained and archived orders**  
Any order still in the live database must be reprintable. Archived orders must remain queryable through explicit archive selection and must retain enough historical data to regenerate approved kitchen/customer output.

Exact archive-file naming, attachment/opening and storage behavior will be specified in `storage-strategy.md`.

### 5.6 Hiboutik emergency-print import

**FR-050 — Emergency paste import**  
The operator must be able to paste the text of the automatic Hiboutik order-summary email when Hiboutik server-side printing is unavailable.

**FR-051 — Review before commit/print**  
Parsed content must be shown for operator review before it becomes a committed/printed emergency order.

**FR-052 — Preserve fulfilment information**  
The parser must preserve operationally important information such as requested pickup/delivery date/time so a future web order cannot be mistaken for a same-day order.

**FR-053 — Emergency-order marker**  
Emergency-imported Hiboutik orders must be visibly distinguishable from ordinary POS-created orders. The main interface must show a clear current-day count/indicator using a distinctive visual treatment.

**FR-054 — Preserve Hiboutik original amount and POS actual amount**  
An emergency-imported Hiboutik order must preserve the **original total received from the Hiboutik email** separately from the amount that the POS calculates/uses operationally after any local discount or adjustment.

The operator must also be able to record the payment actually received for the emergency order so that end-of-day reconciliation can compare:

- Hiboutik's original order amount;
- the POS operational/actual amount;
- the actual payment received and payment method.

This payment information exists for reconciliation of the emergency copy; it does not turn the emergency record into a new POS-originated sale.

**FR-055 — Emergency-order accounting/statistics boundary**  
An emergency-imported Hiboutik order is a local operational/printing copy of an order that already exists in Hiboutik. It must therefore:

- remain available in the POS for viewing, printing, payment/reconciliation reference and future-order reminders where applicable;
- be excluded from ordinary POS-originated received-payment/turnover totals;
- be excluded from the ordinary POS card amount that must be newly entered into Hiboutik;
- be excluded from export into `Gestion SUSHI 81.xlsm`.

**FR-056 — Emergency reconciliation discrepancy**  
When a Hiboutik emergency order's original Hiboutik amount differs from the POS operational/actual amount and/or recorded payment outcome, the main/reconciliation interface must surface a clear exception/discrepancy indicator so that the difference is not forgotten during daily review.

When no such exception exists, the main screen does not need to display a redundant Hiboutik-specific reconciliation amount.

The exact email formats, parser behavior and error handling will be specified in `paste-order-import.md` using sanitized samples.

### 5.7 Printing

**FR-060 — Kitchen ticket**  
The POS must generate and print the approved kitchen ticket.

**FR-061 — Customer/order ticket at order creation**  
When an ordinary or future order is confirmed, the operational kitchen and customer/order printouts must be generated immediately. For a future order, the operator may physically retain these tickets as an additional reminder until fulfilment day.

**FR-062 — Payment-updated customer reprint**  
After payment is recorded or corrected, the operator must be able to reprint the customer receipt/order ticket so that the latest approved payment information can be reflected when required.

**FR-063 — Selective reprint**  
Kitchen and customer output must be independently reprintable.

**FR-064 — Print failure handling**  
Printing failure must not corrupt or remove an already persisted order.

Printer configuration, statutory receipt content, VAT display, templates, retry behavior and implementation will be specified in `printing.md`.

### 5.8 Export to `Gestion SUSHI 81.xlsm`

**FR-070 — Structured export**  
The POS must export approved POS-originated order and sales-detail data in a deterministic form suitable for transfer into `Gestion SUSHI 81.xlsm`.

**FR-071 — Preserve management compatibility**  
The export must preserve the information required for current-year sales history, LCL reconciliation and product-sales analysis.

**FR-072 — Exclude Hiboutik emergency copies**  
Emergency-imported Hiboutik orders must not be exported into `Gestion SUSHI 81.xlsm`, because the management workbook does not store Hiboutik individual orders and the emergency record is not a new POS-originated sale.

The final export schema, period rules and transfer mechanism will be specified in `export.md`.

### 5.9 Main-screen operational overview

**FR-080 — Received-payment summary**  
The main interface must provide an at-a-glance current-day summary for **ordinary POS-originated payments** showing at least:

- today's actual amount received;
- today's card (`CB`) amount;
- today's cash (`Espèce`) amount.

These values update from actual payment events and are attributed by payment date rather than order-creation date.

Under normal conditions, today's POS card amount is also the amount that must be entered/represented in Hiboutik, so a duplicate fourth figure is unnecessary. A Hiboutik-specific warning/amount should appear only when an emergency-import discrepancy creates a real exception that requires review.

**FR-081 — Due-today advance orders**  
The main interface does **not** need a generic panel listing all same-day orders. Its "today" reminder function is specifically to surface advance orders whose planned fulfilment date has now arrived. This reminder is operational and does not depend on the order's payment state.

An advance order that becomes due today remains visible in this due-today reminder for the rest of that calendar day. v1 does not require an additional "processed/collected" state merely to remove it early.

**FR-082 — Future orders**  
The main interface must show the total number of orders whose planned fulfilment date is after today. The operator can open the area to inspect the actual orders/dates; the compact home-screen indicator does not need date-by-date counts.

**FR-083 — Overdue unsettled orders**  
The main interface must show a clearly distinct overdue-unsettled area for past-due orders that remain unpaid or partially paid.

**FR-084 — Hiboutik emergency-order count and discrepancy alert**  
Today's emergency-imported Hiboutik orders must be visibly identifiable and their count shown through an appropriate distinctive visual treatment.

If any such order has an unresolved amount/payment discrepancy, the main/reconciliation interface must also provide a clear warning/entry point for that exception.

**FR-085 — Business configuration**  
Configuration expected to change during normal business use must not require source-code changes.

**FR-086 — Diagnostics**  
The application must expose enough diagnostics to understand operational failures without exposing or committing sensitive production data.

**FR-087 — Interface language switch**  
The operator must be able to switch software UI labels between French and Chinese through a compact button or setting. Only software interface text—buttons, menus, headings, statuses, prompts and validation messages—requires localization. Product/catalogue data, addresses, comments and other business-entered data remain unchanged.

## 6. Non-functional requirements

**NFR-001 — Windows desktop**  
The v1 POS must run as a desktop-class Windows application on the supported Sushi 81 workstations.

**NFR-002 — Local-first operation**  
Core order entry, management and printing must not depend on a mandatory hosted application backend.

**NFR-003 — Data durability**  
Committed data must survive normal application restart/crash scenarios and must not depend on the application remaining open.

**NFR-004 — Recoverability**  
A documented backup and restore path must exist before production use.

**NFR-005 — Safe schema evolution**  
Persistent-data schema changes must be versioned and testable. Updates must not silently reset or discard production data.

**NFR-006 — Separation of application and business data**  
Deployment must not rely on executable files and the live business database being in the same folder or disk partition. Final locations will be defined in `storage-strategy.md`.

**NFR-007 — Performance and responsiveness**  
Common actions—search, item addition, quantity change, order lookup, payment update and navigation—should feel immediate. Printing should not reproduce the recurring multi-second UI stalls of the VBA workflow.

**NFR-008 — Usability**  
The primary workflow must minimize unnecessary dialogs and choices during live order entry.

**NFR-009 — Testability**  
Core business logic, persistence, parsing, archive/export behavior and print-data generation must be testable independently of manual production UI interaction.

**NFR-010 — Dependency discipline**  
Prefer free/open-source dependencies compatible with the approved architecture. Paid, hosted or subscription dependencies require explicit approval.

**NFR-011 — Sensitive-data protection**  
Real customer, order, payment, credential or other sensitive production data must not be committed to Git. Test fixtures must be synthetic or sanitized.

**NFR-012 — Maintainability**  
UI, business rules, persistence, printing and integrations should have clear boundaries.

**NFR-013 — Lightweight access model**  
No login, employee-account or permission framework is required in v1.

**NFR-014 — Multi-computer safety**  
Both supported computers must be able to run the full application, while storage/synchronization design must prevent unsafe concurrent writes or database conflicts.

**NFR-015 — Localizable interface**  
French and Chinese UI strings must be maintained separately from business data and application logic. Only one interface language needs to be displayed at a time.

## 7. Explicit v1 exclusions

The following are deliberately **out of scope** for v1 unless a later explicit decision changes the boundary:

- inventory/stock management;
- table-service / dine-in table management;
- employee scheduling;
- employee login/account/role/permission management;
- loyalty/points/membership systems;
- marketing SMS/email campaigns;
- online storefront/e-commerce replacement;
- replacing Hiboutik;
- automatic Hiboutik API integration;
- direct electronic control of the bank card terminal;
- POS-managed execution of card refunds;
- full refund-accounting workflow inside the POS;
- arbitrary order-time overriding of a catalogue product's base price;
- separate telephone-versus-walk-in source classification when it has no operational value; Hiboutik emergency imports remain the distinct special source case;
- full accounting system;
- replacing `Gestion SUSHI 81.xlsm`;
- heavyweight CRM/customer-profile management.

The POS should remain a focused operational order-entry/relay system rather than expanding into a generic business-management platform.

## 8. Cutover from the old POS

Completed historical orders from `POS_Caisse.xlsm` do not need to be migrated into the new POS.

At production cutover:

- already active/unresolved orders in the old POS may continue to be processed to completion in the old system;
- newly received orders from the agreed cutover point are entered into the new POS;
- no complex one-time historical migration utility is required solely for the transition.

This deliberately favors a simple, low-risk operational handover.

## 9. Phase 1 boundaries still deferred

Phase 1 does not yet freeze:

- exact lifecycle state names and detailed transitions;
- exact replacement/linking behavior for modified settled orders;
- exact discount/minimum/delivery business rules;
- exact option-group configuration and custom option-price validation;
- detailed payment-method model, payment-correction implementation and audit representation;
- exact lightweight telephone-history UI;
- detailed catalogue import/export schema;
- database engine and physical schema;
- archive database naming/opening implementation;
- application framework/UI technology;
- exact placement/style of the language switch;
- live-data location and multi-computer synchronization mechanics;
- backup implementation;
- exact Hiboutik email parser rules and emergency-reconciliation UI;
- printer model/protocol and final ticket layouts;
- final `Gestion SUSHI 81.xlsm` export schema.

Those belong to the later business, architecture, storage and integration documents.

## 10. Phase 1 success criteria

Phase 1 is complete when:

1. `current-system.md` is an approved baseline of the current operation;
2. the v1 product goals, capabilities and exclusions in this document are accepted;
3. incorrect assumptions are corrected before Phase 2;
4. unresolved lifecycle/business/storage details are explicitly deferred rather than guessed.

Approval of Phase 1 does **not** authorize production implementation. The later business, architecture, storage, integration and acceptance documents must still be reviewed before architecture freeze and Codex production implementation.