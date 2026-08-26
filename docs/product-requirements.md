# Product requirements

**Status:** Draft — Phase 1  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Target use:** Internal operational use by Sushi 81

## 1. Purpose

Sushi81 POS is a dedicated Windows desktop application intended to replace the Excel/VBA-centered `POS_Caisse.xlsm` workflow currently used by Sushi 81.

The application is primarily an order-entry and operational order-management tool for telephone and walk-in orders. It does **not** replace `Gestion SUSHI 81.xlsm`, which remains part of the management workflow and must continue to receive the approved POS data export.

The product must preserve the speed and business-specific simplicity of the current workflow while improving reliability, payment-state handling, data durability, searchability, printing and maintainability.

This document defines the Phase 1 product boundary. Detailed business rules, lifecycle semantics and architecture are intentionally deferred to later documents in the project roadmap.

## 2. Product vision

The v1 product should behave like a focused operational tool built specifically for Sushi 81 rather than a generic commercial POS platform.

Its core principles are:

- **fast in daily use** — common order-entry actions should require minimal clicks and typing;
- **local-first** — core POS work must not depend on a hosted backend;
- **reliable** — committed business data must survive normal application restarts and updates;
- **business-specific** — implement approved Sushi 81 workflows without unrelated POS complexity;
- **maintainable** — UI, business rules, persisted data and integrations should be separable and testable;
- **evolvable** — later changes to printing, export, storage or Hiboutik support should not require redesigning the whole product;
- **cost-conscious** — no mandatory recurring paid dependency without explicit approval;
- **switchable bilingual interface** — the operator-facing UI must be available in French and Chinese through a compact language switch or setting, without displaying both languages simultaneously and consuming unnecessary POS screen space.

## 3. Primary users and deployment model

The primary users are Sushi 81 staff operating the POS during normal business activity and the owner performing end-of-day review and administration.

The same application should provide the same functional capability on both the shop computer and the home computer so that either computer can replace the other in an emergency.

The target architecture must prevent or safely handle conflicting simultaneous access to the same live business database. The normal business workflow does not require the two computers to operate the live database concurrently.

The v1 application does not require login, employee accounts, roles or permissions.

## 4. v1 product goals

### G-01 — Replace the Excel/VBA POS dependency

Normal order entry, order retrieval, payment follow-up, printing and day-to-day POS administration must work from the standalone Windows application without requiring Excel/VBA.

### G-02 — Preserve and improve fast order entry

The interaction model must be at least as practical as the current UserForm and should remove known friction such as inconvenient quantity editing.

### G-03 — Maintain a durable current operational database

The POS must retain durable order data for normal operational use rather than deleting old rows merely to keep an Excel workbook small.

The live operational database is intended to retain approximately one natural year's operational data. Older completed data must be manually archivable by year into separate archive databases according to the approved archive rules.

### G-04 — Keep `Gestion SUSHI 81.xlsm` in the workflow

The POS remains an order-entry system. It must continue to provide the approved data transfer/export needed by `Gestion SUSHI 81.xlsm`; v1 does not attempt to replace the management workbook's broader role.

### G-05 — Support Sushi 81 fulfilment workflows

The system must support approved pickup and delivery workflows and retain the information needed to execute them correctly.

### G-06 — Support operational printing and reliable reprinting

The system must support kitchen/customer operational printouts, payment-updated customer reprints and later reprinting of retained or archived orders.

### G-07 — Support Hiboutik emergency-print fallback

When Hiboutik server-side printing is unavailable, the POS must provide a controlled emergency path for pasting Hiboutik order-email text, reviewing the parsed order and generating local printable order output.

### G-08 — Support clear payment and reconciliation workflows

The POS must distinguish order state from payment state, support partial/mixed payments, make unresolved payment situations visible at the appropriate time, and clearly expose the card amount that must be represented in Hiboutik.

### G-09 — Improve recoverability and maintainability

Application updates, backup, restore, schema evolution and automated testing must be safer and more explicit than in the workbook-centered design.

### G-10 — Provide switchable French/Chinese interface languages

The application must provide French and Chinese versions of the operator-facing interface. The operator must be able to switch the interface language through a compact button or setting so that only one language is displayed at a time. Product names, catalogue content, addresses, comments and other business-entered data do not require translation or bilingual duplication.

## 5. Functional requirements

### 5.1 Catalogue and product selection

**FR-001 — Product catalogue**  
The application must use a structured Sushi 81 product catalogue.

**FR-002 — Product search and selection**  
Product search must support both product code and product name. Product addition must support both fast double-click addition and an explicit add action.

**FR-003 — Cart management**  
The operator must be able to view the current cart, add/remove items and change quantities. An item already in the cart must be directly adjustable through a convenient quantity field and/or `+`/`-` controls without requiring a separate double-click edit workflow.

**FR-004 — Prices and commercial attributes**  
The catalogue must carry the information needed by approved pricing, VAT and discount rules.

**FR-005 — Product options and choices**  
Products may have one or more operator-selectable customer choices, such as an ice-cream flavour or a menu variant. These selections must be made directly during order entry rather than relying only on free-text comments.

**FR-006 — Preserve item-specific choices**  
Selected product options must remain attached to the specific order item and remain available when the order is viewed, modified or printed.

Detailed catalogue maintenance and option-group rules will be defined in `catalogue-management.md`.

### 5.2 Order creation and customer/order information

**FR-010 — Create order**  
The operator must be able to create orders quickly from catalogue items.

**FR-011 — Fulfilment mode**  
Orders must support pickup and delivery.

**FR-012 — Required fulfilment information**  
The system must enforce information required by the selected fulfilment mode, such as delivery address where applicable.

**FR-013 — Commercial rules**  
Approved discount, threshold and delivery rules must be applied consistently. Exact rules will be frozen in `business-rules.md`.

**FR-014 — Telephone usability**  
Telephone entry must support readable French-style grouping automatically. Telephone must not be assumed mandatory for every walk-in order.

**FR-015 — Free-text operational comment**  
A flexible order-level comment field must remain available for preparation instructions, customer requests and other operational notes not covered by structured fields.

**FR-016 — Lightweight historical assistance**  
The product does not require a formal customer-profile/CRM system in v1. However, when a telephone number has appeared previously, the operator should be able to consult useful historical information such as previously used delivery addresses or relevant order comments/preferences when practical. Exact behavior will be defined later and must not create a heavyweight customer-management workflow.

**FR-017 — Structured fulfilment date/time**  
The system must be capable of recording the relevant planned pickup/delivery date and time separately from free-text comments. This is required so that future orders can be distinguished from overdue unpaid orders and from normal same-day unpaid orders.

**FR-018 — Future-order entry**  
The normal order-entry workflow must provide a simple way to mark an order as a future order and choose its planned future fulfilment date. Same-day ordering should remain the low-friction default so that the future-order control does not add unnecessary work to ordinary orders.

### 5.3 Order state and payment state

**FR-020 — Separate order and payment state**  
Order lifecycle state and payment state must be represented separately. Cancellation or replacement of an order must not be encoded as a payment state, and unpaid/partially paid/settled must not be encoded only through the order status.

**FR-021 — Payment states**  
The target payment model must support at least:

- unpaid / payment pending;
- partially paid;
- fully settled.

Exact labels and transitions will be frozen in `order-lifecycle.md`.

**FR-022 — Payment recording**  
The POS must record the approved payment method or payment composition associated with an order.

**FR-023 — Structured split-payment amounts**  
The operator must be able to record actual cash and non-cash/card amounts for a mixed or partial payment. The application must display the order total, amount already paid and remaining balance and verify the mathematical consistency of the payment entries.

**FR-024 — Payment finalization workflow**  
The operator must be able to retrieve an existing order, add or correct payment information and mark it fully settled without editing the underlying database directly.

**FR-025 — Outstanding-payment attention**  
The main interface must provide a clearly visible way to surface unpaid or partially paid orders that genuinely require follow-up without treating every normal unpaid order as a problem.

For the Phase 1 product baseline, an order becomes an overdue-payment attention item when its planned fulfilment date is earlier than the current date and its payment state is not fully settled. Same-day unpaid orders and future orders whose planned fulfilment date has not yet arrived must not appear in this overdue-payment area. Exact edge-case transitions will be frozen in `order-lifecycle.md`.

**FR-026 — Hiboutik card amount summary**  
The application must clearly display a single current total for the POS-originated card amount that still needs to be represented/entered in Hiboutik according to the approved business process. v1 does not require automatic submission to Hiboutik.

A Hiboutik emergency-import order must be excluded from this dedicated amount-to-enter total because that order already originates in Hiboutik. This exclusion does not prevent the emergency order from participating in other appropriate POS operational or turnover displays.

Direct electronic control of a bank card terminal is not assumed for v1 unless later explicitly approved.

### 5.4 Order lifecycle, retrieval and navigation

**FR-030 — Persist order**  
A confirmed order must be durably stored according to the approved lifecycle.

**FR-031 — Reload order**  
The operator must be able to retrieve an existing order when permitted by the lifecycle.

**FR-032 — Modify order**  
The operator must be able to modify an existing order while preserving the approved historical/audit behavior.

**FR-033 — Cancel order**  
Cancellation must not silently destroy business history that must be retained.

**FR-034 — Abandon in-progress edits**  
The operator must be able to abandon uncommitted edits and return to the persisted state.

**FR-035 — Practical order search**  
Search/filtering must support practical order information including telephone number and comment text; additional useful fields may be included during UI design.

**FR-036 — Distinguish current and older orders**  
The order list must visually distinguish today's orders from older retained orders. Muted/grey styling for older orders is an acceptable design direction.

**FR-037 — Current database first**  
Normal order search must prioritize the live/current operational database. Archived years do not need to participate in every ordinary search automatically.

**FR-038 — Archive access**  
The operator must be able to explicitly open/select an archived year and search or inspect archived orders when needed.

### 5.5 Retention and annual archive

**FR-040 — Natural-year archive model**  
Completed historical data must be manually archivable by natural year into a separate archive database rather than remaining indefinitely in the live operational database.

**FR-041 — Settlement-year assignment**  
For archive/reporting-year assignment, an order that remains unpaid across a year boundary belongs to the natural year in which it becomes fully settled. The record must nevertheless retain its original creation date and planned/actual fulfilment information.

**FR-042 — Do not archive unresolved payments**  
An unpaid or partially paid order must not be removed from the live operational database merely because its creation or fulfilment date belongs to an older year.

Once it is fully settled, it becomes eligible for the appropriate archive year according to the approved settlement-year rule.

**FR-043 — Reprint retained and archived orders**  
Any order still present in the live database must be reprintable. Archived orders must remain accessible through archive selection so that kitchen/customer output can be regenerated when needed.

Exact archive-file naming, database attachment/opening behavior and physical storage will be defined in `storage-strategy.md`.

### 5.6 Hiboutik emergency order import

**FR-050 — Emergency paste import**  
The POS must provide an operator-facing method to paste the text of the automatic Hiboutik order-summary email when Hiboutik server-side printing is unavailable.

**FR-051 — Review parsed result**  
Parsed order content must be shown for review before committing/printing so that a parsing error does not silently become a wrong kitchen order.

**FR-052 — Preserve operational date information**  
The emergency import must preserve important operational information such as requested pickup/delivery date/time so a future order cannot be mistaken for a same-day order.

**FR-053 — Emergency-order marker**  
Emergency-imported Hiboutik orders must be visibly distinguishable from ordinary POS-created orders. The main interface should provide a clear current-day indicator/count of emergency orders, using a distinctive visual treatment such as a different color.

The exact Hiboutik email formats, parser behavior and error handling will be specified in `paste-order-import.md` and validated with sanitized samples.

Emergency-imported Hiboutik orders are already represented in Hiboutik and must therefore be excluded from the dedicated POS-originated "amount to enter in Hiboutik" total, while remaining visibly available for normal emergency printing and appropriate operational displays.

### 5.7 Printing

**FR-060 — Kitchen ticket**  
The POS must generate and print the approved kitchen ticket.

**FR-061 — Customer/order ticket at order creation**  
Order creation must support the operational printout needed before customer payment, including the customer/order ticket used to identify and attach the order to the prepared package.

**FR-062 — Payment-updated customer reprint**  
After payment is recorded or corrected, the operator must be able to reprint the customer ticket/receipt so that the latest approved payment information can be reflected when required.

**FR-063 — Selective reprint**  
Kitchen and customer output must be independently reprintable for an existing order.

**FR-064 — Print failure handling**  
A printing problem must not corrupt or lose the persisted order.

Printer configuration, statutory receipt content, VAT display, templates, retry behavior and implementation architecture will be defined in `printing.md`.

### 5.8 Export and `Gestion SUSHI 81.xlsm`

**FR-070 — Structured export**  
The POS must export the approved order and sales-detail data in a deterministic form suitable for transfer into `Gestion SUSHI 81.xlsm`.

**FR-071 — Preserve management compatibility**  
The transfer must preserve the business information required for the management workbook's current-year sales history, LCL reconciliation and product-sales analysis.

The final export schema, period-selection rules and transfer mechanism will be specified in `export.md`.

### 5.9 Operational overview and administration

**FR-080 — Daily operational summary**  
The main interface must show an at-a-glance current-day turnover/payment summary and should update automatically from committed/payment-updated data.

**FR-081 — Today's orders**  
The main interface must provide an immediately accessible view/summary of today's orders.

**FR-082 — Future orders**  
The main interface must provide a clearly visible future-orders area so that orders already received for a later fulfilment date are not forgotten. The operator must be able to open this area and inspect the relevant future orders.

**FR-083 — Overdue unsettled orders**  
The main interface must provide a clearly visible overdue-unsettled area for orders whose planned fulfilment date has passed and whose payment state remains unpaid or partially paid. This area must be distinct from today's normal unpaid orders and from legitimate future orders.

**FR-084 — Emergency-order summary**  
The main interface must make today's Hiboutik emergency-import orders visibly identifiable and show their count or equivalent clear indicator.

**FR-085 — Business configuration**  
Configuration expected to change in normal operation must not require source-code changes.

**FR-086 — Diagnostics**  
The application must provide enough diagnostics to understand operational failures without exposing or committing sensitive production data.

**FR-087 — Interface language switch**  
The operator must be able to switch the application's interface labels between French and Chinese through a compact button or setting. Only software UI text—such as buttons, menus, headings, statuses, prompts and validation messages—requires localization. Product/catalogue content, customer-entered information, addresses, comments and other business data must remain unchanged when the interface language is switched.

## 6. Non-functional requirements

**NFR-001 — Windows desktop**  
The v1 POS must run as a desktop-class Windows application on the supported Sushi 81 workstations.

**NFR-002 — Local-first core operation**  
Core order-entry, order-management and printing workflows must not depend on a mandatory hosted application backend.

**NFR-003 — Data durability**  
Committed business data must survive normal application restart/crash scenarios and must not depend on the application remaining open.

**NFR-004 — Recoverability**  
A documented backup and restore path must exist before production use.

**NFR-005 — Safe schema evolution**  
Persistent-data schema changes must be versioned and testable. Updates must not silently reset or discard production data.

**NFR-006 — Separation of application and business data**  
Deployment must not rely on executable files and the live business database being in the same folder or disk partition. Final locations will be defined in `storage-strategy.md`.

**NFR-007 — Performance and responsiveness**  
Common actions—search, add item, quantity change, order lookup, payment update and ordinary navigation—should feel immediate. Printing should be initiated without recurring multi-second UI stalls like those experienced in the VBA workflow.

**NFR-008 — Usability**  
The primary workflow must minimize unnecessary dialogs and choices during live order entry.

**NFR-009 — Testability**  
Core business logic, persistence, parsing, archive/export behavior and printing-data generation must be testable independently from manual production UI interaction.

**NFR-010 — Dependency discipline**  
Prefer free/open-source dependencies compatible with the approved architecture. Paid, hosted or subscription dependencies require explicit approval.

**NFR-011 — Sensitive-data protection**  
Real customer, order, payment, credential or other sensitive production data must not be committed to Git. Test fixtures must be synthetic or sanitized.

**NFR-012 — Maintainability**  
UI, business rules, persistence, printing and integrations should have clear boundaries.

**NFR-013 — Lightweight access model**  
No login, employee-account or permission framework is required in v1.

**NFR-014 — Multi-computer safety**  
Both supported computers must be able to run the full application, but the storage/synchronization design must prevent unsafe concurrent writes or database conflicts when the same live data is involved.

**NFR-015 — Localizable operator interface**  
French and Chinese UI strings must be maintained separately from business data and application logic so that the interface can switch cleanly between the two languages without duplicating catalogue or order content. Only one interface language needs to be shown at a time in order to preserve usable POS screen space.

## 7. Product boundaries for Phase 1

Phase 1 establishes product direction but does not yet freeze:

- exact order states, replacement semantics and state labels;
- exact edge-case lifecycle behavior around overdue unpaid/partial orders beyond the approved fulfilment-date rule;
- exact discount and minimum-order rules;
- detailed payment-method model beyond support for pending, partial, settled and structured split amounts;
- detailed product-option/group rules;
- exact lightweight telephone-history assistance;
- catalogue maintenance workflow;
- database engine and physical schema;
- archive database naming/attachment implementation;
- application framework/UI technology and exact placement/style of the French/Chinese language switch;
- live-data location and multi-computer synchronization mechanics;
- backup implementation;
- exact Hiboutik email format/parser;
- printer model/protocol and final ticket templates;
- final `Gestion SUSHI 81.xlsm` export schema.

Those decisions belong to the later business, architecture, storage and integration documents.

## 8. v1 scope discipline

The product should solve the operational Sushi 81 order-entry problem without becoming a generic POS or CRM.

In particular:

- `Gestion SUSHI 81.xlsm` remains outside the POS replacement scope;
- no employee/account/permission system is required;
- no automatic Hiboutik API integration is required for v1;
- Hiboutik emergency import exists to protect operations when Hiboutik printing fails, not to duplicate all web orders into the POS as a normal workflow;
- telephone and walk-in orders do not need to be distinguished as separate business sources unless a later requirement creates real value from doing so;
- implementation must not silently introduce unapproved external services or recurring-cost dependencies.

## 9. Phase 1 success criteria

Phase 1 is complete when:

1. `current-system.md` is an approved baseline of the existing operation;
2. the v1 product goals and capability boundaries in this document are accepted;
3. incorrect assumptions are corrected before Phase 2;
4. unresolved lifecycle/business/storage details are explicitly deferred rather than guessed.

Approval of Phase 1 does **not** authorize production implementation. The repository roadmap still requires the later business, architecture, storage, integration and acceptance documents to be reviewed before architecture freeze and production implementation.