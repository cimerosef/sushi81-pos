# Current system

**Status:** Draft — Phase 1  
**Last updated:** 2026-08-25  
**Purpose:** Describe the operational baseline that Sushi81 POS is intended to replace or integrate with. This document records the current system as understood today; it does not define the final target architecture or freeze future business rules.

## 1. Operational context

Sushi 81 currently operates with a collection of local and web-based tools rather than one dedicated POS application.

The known operational environment includes:

- Windows PCs;
- Microsoft Excel 365 with macro-enabled workbooks and VBA/UserForm logic;
- Hiboutik for web/e-shop order handling;
- local ticket printing for kitchen and customer use;
- Excel-based accounting/export workflows feeding the existing accounting process;
- small browser-side automations used to reduce operational mistakes in Hiboutik.

The current solution has evolved around the real needs of a single Sushi 81 business operation. The goal of the new project is not to redesign those needs from scratch, but to move the useful workflow into a more reliable and maintainable standalone application.

## 2. Current Excel/VBA POS workflow

The current POS workflow is implemented primarily through an Excel/VBA UserForm.

### 2.1 Product selection and cart

Known current behavior includes:

- product search;
- adding products to a cart;
- quantity adjustment with `+` and `-` controls;
- fast product addition through double-click behavior;
- calculation of the order total;
- a 10% discount mechanism that applies only to products marked as discount-eligible (`OUI` in the current data);
- enforcement of the current minimum-order rule, including blocking the relevant operation when the order is below €15.

These describe the current implementation. The authoritative target rules will be frozen later in `business-rules.md`.

### 2.2 Fulfilment mode

The current workflow distinguishes at least:

- pickup (`Retrait`);
- delivery (`Livraison`).

For delivery, an address is required by the current workflow.

The exact target customer/address model is not defined by this document and will be specified in the later business and data-model phases.

### 2.3 Payments

The current POS records payment using categories including:

- card (`CB`);
- cash (`Espèces`);
- other (`DIV`).

The current software records the payment mode used for the order. Direct integration with a bank card terminal is not assumed by this Phase 1 document.

### 2.4 Order state and modification

Known current behavior includes:

- an order can be recorded as `OK` or `ANNULÉ`;
- an existing order can be reloaded for modification;
- modifications can be abandoned through an `Annuler modification` action.

The precise lifecycle, finalization semantics, audit requirements and rules for editing/cancelling finalized orders are intentionally deferred to `order-lifecycle.md`.

## 3. Printing

The current workflow supports operational printing, including:

- a kitchen ticket;
- a customer ticket.

Printing is therefore part of the existing business process and must be treated as a first-class integration when the target system is designed.

Printer selection, templates, failure handling and the final printing architecture are deferred to `printing.md`.

## 4. Hiboutik and online orders

Hiboutik is currently used for online/e-shop orders and remains a separate web interface.

Known operational behavior includes:

- a sound notification when a new online order arrives;
- a manual page refresh to make the new order visible;
- a `date de retrait` field used to identify the requested pickup date;
- a browser userscript that highlights future pickup dates in red so that a future order is not accidentally prepared as a same-day order.

This highlights an important weakness of the current system: online-order information is not naturally unified with the local POS workflow, so staff must visually transfer and verify information between interfaces.

The target design includes a dedicated later specification for pasted-order import (`paste-order-import.md`). This document does not yet define the parsing format or import contract.

## 5. Catalogue management

Product and commercial data are currently maintained in Excel/VBA-based files.

The present POS depends on structured catalogue information such as product identity, pricing and discount eligibility. The exact current source-of-truth workflow, catalogue editing process and the target catalogue model still need to be documented and frozen in `catalogue-management.md`.

## 6. Accounting and export workflow

Sushi 81 also uses Excel-based processes for accounting preparation and export.

Known current elements include:

- the `Gestion SUSHI 81.xlsm` workbook;
- handling of transactions identified as `Hors Hiboutik`;
- IDs generated in the form `YYYYMMDD_HHMMSS` for that workflow;
- exclusion of current-day (`J0`) data from the relevant export process;
- accounting preparation for Zefyr;
- VAT/accounting distinctions currently used by the business, including 20%, 10% and 5.5% VAT contexts.

The future POS must preserve the ability to produce the business data needed by the accounting workflow, but the exact export contract will be specified later in `export.md`.

## 7. Data and storage characteristics of the current solution

The current operational solution is file/workbook based rather than built around a dedicated transactional application database.

This has practical consequences:

- application logic and business data are closely coupled to Excel files;
- macro behavior depends on the Excel runtime and workbook state;
- versioning application logic independently from live business data is difficult;
- validation, migrations and automated testing are harder than in a dedicated application;
- recovery and long-term data integrity depend heavily on file-management discipline;
- extending the workflow increases the complexity of VBA and workbook interactions.

These limitations are architectural motivations for the standalone POS project. They are not, by themselves, a specification of which database technology must be used.

## 8. Current-system strengths that should not be lost

The replacement system should preserve the practical strengths of the current workflow:

- fast order entry suitable for day-to-day counter use;
- a compact workflow with little unnecessary operator interaction;
- business-specific behavior rather than generic POS complexity;
- quick correction/modification of an order when allowed;
- kitchen and customer ticket output;
- continuity with existing accounting needs;
- support for both pickup and delivery operations;
- clear visibility of operationally important information such as future-order dates.

## 9. Main limitations motivating the new POS

The standalone project is intended to address the following structural problems:

1. **Dependence on Excel/VBA** — core POS behavior depends on macro-enabled workbooks and Excel runtime state.
2. **Tight coupling of data and application logic** — business data, UI logic and automation are difficult to evolve independently.
3. **Fragmented order handling** — local POS work and Hiboutik online orders are handled through separate interfaces and manual verification.
4. **Operational error risk** — important distinctions, such as future pickup dates, currently require compensating browser automation.
5. **Limited testability and maintainability** — regression testing and structured evolution are difficult in the current workbook model.
6. **Data-management constraints** — file-based persistence is less suitable for explicit schemas, migrations, auditability and controlled recovery.
7. **Integration friction** — printing, accounting export and future import features are harder to isolate behind stable interfaces.

## 10. Boundary of this document

This document deliberately does **not** decide:

- the target technology stack;
- the database engine;
- the detailed data model;
- final order lifecycle semantics;
- final discount, minimum-order or payment business rules;
- the catalogue source-of-truth design;
- the storage, synchronization or backup architecture;
- printer implementation details;
- the exact Hiboutik paste/import format;
- the final accounting export schema.

Those decisions belong to the later documents in the repository roadmap.

## 11. Phase 1 baseline

For Phase 1, the important conclusion is that Sushi81 POS is replacing a proven but increasingly constrained Excel/VBA-centered operational workflow. The new product should preserve its speed and Sushi 81-specific practicality while separating application logic, durable business data and external integrations into a maintainable standalone system.
