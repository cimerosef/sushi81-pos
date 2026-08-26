# Product requirements

**Status:** Draft — Phase 1  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Target use:** Internal operational use by Sushi 81

## 1. Purpose

Sushi81 POS is a dedicated Windows desktop application intended to replace the Excel/VBA-centered POS workflow currently used by Sushi 81 and to provide a stable foundation for local order management, printing, online-order intake and accounting export.

The product must preserve the speed and business-specific simplicity of the current workflow while improving reliability, data durability, maintainability and testability.

This document defines the Phase 1 product boundary. Detailed business rules and architecture are intentionally deferred to the later documents in the project roadmap.

## 2. Product vision

The v1 product should behave like a focused operational tool built specifically for Sushi 81 rather than a generic commercial POS platform.

Its core principles are:

- **fast in daily use** — common counter operations should require minimal clicks and typing;
- **local-first** — core POS work must not depend on a remote SaaS service being available;
- **reliable** — business data must survive application restarts, software updates and ordinary workstation failures when the documented recovery process is followed;
- **business-specific** — the product should implement approved Sushi 81 workflows without unnecessary generic POS complexity;
- **maintainable** — application code, business rules, persisted data and integrations should be separable and testable;
- **evolvable** — later changes to printing, import/export or storage should not require rebuilding the entire product concept;
- **cost-conscious** — the product should not introduce a mandatory recurring paid dependency unless explicitly approved.

## 3. Primary users

The primary users are Sushi 81 staff operating the POS during normal business activity.

The owner/administrator also needs to be able to maintain the business configuration and obtain the data required for accounting and operational follow-up.

The v1 product is designed for Sushi 81's own operational environment. Multi-tenant SaaS behavior and a general-purpose commercial POS product are not requirements for v1.

## 4. v1 product goals

### G-01 — Replace the Excel/VBA operational dependency

The normal POS workflow must be executable from a standalone Windows application without requiring Microsoft Excel or VBA to run the POS itself.

Existing Excel files may remain part of migration, accounting or transitional workflows until their replacement interfaces are explicitly specified.

### G-02 — Preserve fast order entry

The target POS must support rapid creation of an order using an interaction model at least as practical as the current UserForm workflow.

### G-03 — Centralize order data

Orders created or imported into the POS must be represented as durable application data rather than existing only as transient UI state or workbook cells.

### G-04 — Support Sushi 81 fulfilment workflows

The system must support the approved pickup and delivery workflows and retain the information required to execute them correctly.

### G-05 — Support operational printing

The system must support the approved kitchen-ticket and customer-ticket workflows.

### G-06 — Support online-order intake

The system must provide a controlled way to bring supported online-order information into the POS without forcing staff to manually reconstruct every field.

The first planned integration path is the pasted-order workflow defined later in `paste-order-import.md`.

### G-07 — Preserve accounting continuity

The POS must retain sufficient structured information to generate the approved accounting/export output required by Sushi 81's existing accounting process.

### G-08 — Improve recoverability and maintainability

The target system must make application updates, data backup, recovery, schema evolution and automated testing safer and more explicit than in the current workbook-centered design.

## 5. Functional requirements

The following requirements define capabilities that v1 must support. Detailed semantics remain governed by the later business-design documents.

### 5.1 Catalogue and product selection

**FR-001 — Product catalogue**  
The application must be able to load and use a structured Sushi 81 product catalogue.

**FR-002 — Product search and selection**  
The operator must be able to find and add products quickly during order entry. Product search must support both product code and product name, and the product-selection workflow must preserve both fast double-click addition and an explicit add action.

**FR-003 — Cart management**  
The operator must be able to view the current cart, change quantities and remove or add items before finalization. For an item already in the cart, the operator must be able to select the line and adjust its quantity directly through a convenient quantity control and/or `+`/`-` actions without having to reopen the item through a separate edit gesture.

**FR-004 — Prices and commercial attributes**  
The catalogue must be capable of carrying the information required by approved pricing and discount rules.

**FR-005 — Product options and choices**  
The catalogue/order-entry model must support products that require one or more operator-selectable customer choices, such as choosing one flavour from several available flavours for an ice cream or choosing an allowed variant within a menu item. These choices must be selectable directly during order entry instead of relying only on free-text comments, and the selected choice must remain attached to the relevant ordered item for operational use and printing.

The exact catalogue editing, option-group model, required/optional choice rules and source-of-truth model will be defined in `catalogue-management.md` and the later business-design documents.

### 5.2 Order creation

**FR-010 — Create order**  
The operator must be able to create a new order from selected catalogue items.

**FR-011 — Fulfilment mode**  
The order must support the approved fulfilment modes, including pickup and delivery.

**FR-012 — Required fulfilment information**  
The system must enforce the information required for the selected fulfilment mode, such as delivery information when applicable.

**FR-013 — Commercial rules**  
The system must apply the approved minimum-order, discount and other commercial rules automatically and consistently.

**FR-014 — Preserve item-specific customer choices**  
When an ordered product has configured options or variants, the chosen values must be stored with that order item and remain available when the order is viewed, modified, reprinted or otherwise processed.

**FR-015 — Telephone entry usability**  
Where a telephone number is entered, the UI must support readable French-style grouping automatically rather than requiring the operator to type spaces manually. Whether telephone is mandatory for a particular order type will be defined by the later business rules; v1 must not assume that every walk-in order has a telephone number.

The final rules are not frozen here; they will be defined in `business-rules.md`.

### 5.3 Payments

**FR-020 — Payment recording**  
The POS must record the approved payment method or payment composition associated with an order.

The current system includes categories such as `CB`, `Espèces` and `DIV`, but the target payment model will be frozen in the business-design phase.

**FR-021 — Payment processing boundary**  
Phase 1 requires recording payment information for POS operations. Direct electronic control of, or settlement through, a bank card terminal is not assumed unless a later approved specification explicitly introduces it.

**FR-022 — Structured split-payment amounts**  
The target payment model must support recording the actual monetary split of a mixed payment instead of representing the entire order only with a generic `DIV` category or a free-text note. At minimum, the operator must be able to enter the cash amount and non-cash/card amount and verify that their sum equals the order total.

**FR-023 — Payment finalization workflow**  
The application must support retrieving an existing unpaid or payment-unconfirmed order, recording or confirming its final payment composition, and completing the payment-related workflow without requiring direct editing of the underlying data store.

### 5.4 Order lifecycle, retrieval and navigation

**FR-030 — Persist order**  
An order must be durably stored according to the approved lifecycle.

**FR-031 — Reload order**  
The operator must be able to retrieve an existing order when the approved lifecycle permits it.

**FR-032 — Modify order**  
The operator must be able to modify an order when allowed by the approved lifecycle and business rules.

**FR-033 — Cancel order**  
The product must support the approved cancellation workflow without silently destroying historical business data that must be retained.

**FR-034 — Abandon in-progress edits**  
The operator must be able to abandon an uncommitted edit and return to the previously persisted state where applicable.

**FR-035 — Practical order search**  
The order-search interface must allow the operator to find an order using more than raw visual comparison of telephone numbers. Search/filtering must be able to use practical order information including telephone number and comment/remark text; additional useful fields may be included if approved during UI design.

**FR-036 — Distinguish current and older orders**  
When the order list contains both today's orders and older retained orders, the UI must make their age/date visually clear. A muted/grey treatment for non-current-day orders is an acceptable design direction, but the exact styling will be decided during UI design.

Exact states, finalization behavior, audit requirements and edit/cancel permissions will be defined in `order-lifecycle.md`.

### 5.5 Online-order intake

**FR-040 — Paste external order data**  
The system must provide an operator-facing method to paste supported external order text/data for parsing.

**FR-041 — Review parsed result**  
Imported data must be presented in a way that allows the operator to detect parsing problems before committing an incorrect order.

**FR-042 — Preserve operational date information**  
The import workflow must preserve operationally important information such as the requested pickup date so that future orders cannot be mistaken for same-day orders.

The exact supported Hiboutik format, parser behavior and error handling will be specified in `paste-order-import.md` and validated with samples under `samples/pasted-orders/`.

### 5.6 Printing

**FR-050 — Kitchen ticket**  
The POS must support generation and printing of the approved kitchen ticket.

**FR-051 — Customer ticket**  
The POS must support generation and printing of the approved customer ticket.

**FR-052 — Print failure handling**  
A printing problem must not silently corrupt or lose the underlying order data.

**FR-053 — Separate operational and payment printing moments**  
The target workflow must allow the kitchen ticket to be printed when the order is created while allowing the customer ticket to be printed separately when payment is finalized, so that the customer ticket can reflect the confirmed payment information rather than being forced to print before the payment method is known.

**FR-054 — Selective reprint**  
The operator must be able to retrieve an existing order and reprint the kitchen ticket or customer ticket independently when needed.

Printer configuration, templates, retry/reprint behavior, statutory receipt content and implementation architecture will be defined in `printing.md`.

### 5.7 Export

**FR-060 — Structured export**  
The POS must be able to export the approved business/accounting data in a deterministic format.

**FR-061 — Accounting compatibility**  
The exported information must be sufficient to preserve the approved VAT, sales-account and transaction distinctions required by Sushi 81's accounting workflow.

The exact export schema, period rules and mappings will be defined in `export.md`.

### 5.8 Operational overview and administrative capabilities

**FR-070 — Business configuration**  
Configuration that is expected to change during normal business operation must not require source-code changes.

**FR-071 — Diagnostics**  
The application must provide enough diagnostics to understand operational failures without exposing or committing sensitive production data.

**FR-072 — Daily operational summary**  
The main operational interface must provide an at-a-glance summary of the current day's turnover and approved payment breakdown. The summary should update automatically from committed/payment-updated order data rather than depending on a manual refresh as the normal workflow.

The exact settings surface, reconciliation display and logging design may be refined during architecture and implementation planning.

## 6. Non-functional requirements

### NFR-001 — Windows desktop application

The v1 POS must run as a native or desktop-class application on the supported Windows environment used by Sushi 81.

### NFR-002 — Local-first core operation

Core order-entry and order-management functions must continue to work without a mandatory connection to a hosted application backend.

Features that inherently depend on an external system may report that dependency separately, but they must not make locally stored orders unavailable.

### NFR-003 — Data durability

Committed business data must be stored durably and must not depend on the application remaining open.

A normal application crash or restart must not erase already committed orders.

### NFR-004 — Recoverability

The architecture must provide a documented backup and restore path for business data before production use.

### NFR-005 — Safe schema evolution

Persistent-data schema changes must be versioned and testable. An application update must not silently reset or discard production business data.

### NFR-006 — Separation of application and business data

The deployment design must not rely on the executable files and the live business database being located in the same folder or on the same disk partition.

The final storage locations and configuration mechanism will be defined in `storage-strategy.md`, but application installation and business-data placement must remain logically separable.

### NFR-007 — Performance and responsiveness

Common operator actions—opening an order, product search, adding items, changing quantities, finding an order and moving through normal order-entry steps—should feel immediate on the target workstation and should not require network round trips for local data.

Printing must be initiated without unnecessarily blocking the order-entry interface; the target design should avoid the recurring multi-second UI stalls experienced in the current VBA workflow.

### NFR-008 — Usability

The primary workflow must be optimized for frequent operational use, with minimal unnecessary dialogs or configuration choices during order entry.

### NFR-009 — Testability

Core business logic, persistence behavior, import parsing and export generation must be designed so that they can be covered by automated tests without requiring manual interaction with the production UI.

### NFR-010 — Dependency discipline

Prefer free and open-source dependencies compatible with the approved architecture. A paid, hosted, proprietary or subscription dependency that creates recurring operational cost must not be introduced without an explicit approved decision.

### NFR-011 — Sensitive-data protection

Real customer, order, payment, credential or other sensitive production data must not be committed to Git. Test fixtures must use synthetic or appropriately sanitized data.

### NFR-012 — Maintainability

UI, business rules, persistence and external integrations should have clear boundaries so that changes in one area do not unnecessarily destabilize the others.

### NFR-013 — Lightweight access model

The internal v1 application does not require a login flow, employee account system or role/permission framework. Such a system must not be introduced unless a later explicit business requirement justifies it.

## 7. Product boundaries for Phase 1

Phase 1 establishes the product direction but deliberately does not freeze the following:

- exact order states and finalization semantics;
- exact discount and minimum-order rules;
- the final payment data model beyond the requirement to preserve structured mixed-payment amounts;
- detailed product-option/group configuration rules;
- exact customer/contact-history behavior;
- catalogue maintenance workflow;
- database engine and physical schema;
- application framework and UI technology;
- data-file location and migration mechanics;
- multi-PC synchronization behavior;
- backup implementation;
- exact Hiboutik pasted-order format;
- exact mechanism, if any, for optimizing transfer of POS-originated card revenue into Hiboutik;
- printer model/protocol and ticket templates;
- accounting export schema;
- exact visual styling of order-age/status cues and the daily dashboard.

Those topics have dedicated documents in Phases 2–4.

## 8. v1 scope discipline

The product should solve the operational POS and order-management problem Sushi 81 actually has today. Features must not be added merely because they are common in generic POS products.

In particular, implementation work must not silently introduce new business behavior, external services, recurring-cost dependencies or major integrations that have not been approved in the relevant design document.

The existing business requirement that POS-originated card revenue ultimately be represented correctly in Hiboutik remains in scope as an operational constraint. The future system may optimize how that requirement is fulfilled, but Phase 1 does not assume that it should be removed.

## 9. Phase 1 success criteria

Phase 1 is complete when:

1. the current operational baseline is accurately described in `current-system.md`;
2. the v1 product goals and required capability areas in this document are accepted as the planning baseline;
3. any incorrect assumptions found during review are corrected before they propagate into Phase 2;
4. unresolved details are deliberately assigned to the later roadmap documents rather than being guessed during implementation.

Approval of Phase 1 does **not** authorize production implementation. The repository roadmap requires the relevant business, architecture, storage, integration and acceptance documents to be reviewed before the architecture-freeze milestone and before production implementation begins.
