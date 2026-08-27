# Export to Gestion SUSHI 81

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Specify the reliable transfer/export of Sushi81 POS-originated order and sales-detail data into the existing `Gestion SUSHI 81.xlsm` management workflow.

## 1. Scope

This document defines the V1 export boundary between Sushi81 POS and `Gestion SUSHI 81.xlsm`.

It covers:

- which POS records are eligible for export;
- the logical order-level and sales-line datasets;
- exclusion of Hiboutik paste-created orders;
- historical snapshot fidelity;
- duplicate/idempotency protection;
- export validation and failure handling;
- the boundary between POS storage and the management workbook.

It does not redefine:

- catalogue import/export (`catalogue-management.md`);
- payment and order lifecycle semantics (`order-lifecycle.md`);
- annual SQLite archive behavior (`storage-strategy.md`);
- printing (`printing.md`);
- the internal accounting logic of `Gestion SUSHI 81.xlsm` itself.

## 2. Authoritative baseline

The following requirements are already approved and are not reopened here:

1. `Gestion SUSHI 81.xlsm` remains in the V1 workflow and is not replaced by Sushi81 POS.
2. The management workflow requires POS-originated order/sales data for current-year sales history, LCL reconciliation and product-sales analysis.
3. Hiboutik paste-created orders represent orders that already exist in Hiboutik and must be excluded from the normal POS-to-management export.
4. Historical order/product values must come from the saved order snapshots; later catalogue edits must not rewrite exported history.
5. The live SQLite database is the authoritative POS business store. Export must not become a second primary database.
6. V1 annual archive/retention is handled by `storage-strategy.md`; export must not delete orders merely because they have been transferred.
7. Excel workbook processing uses the approved ClosedXML-based architecture rather than Excel COM automation.

## 3. Relationship to the current workflow

The legacy environment transfers two related logical datasets from `POS_Caisse.xlsm` into `Gestion SUSHI 81.xlsm`:

- order-level data corresponding to the management workbook's `Hors Hiboutik` history;
- sales-detail data corresponding to the legacy `Ventes` information used for product analysis.

The old transfer tool also removed successfully transferred rows from the POS workbook because the workbook itself was the operational storage and needed to stay manageable.

The standalone Sushi81 POS must **not** copy that deletion behavior. SQLite retention and annual archive now solve the storage problem independently.

Therefore V1 export is a transfer/reporting operation, not a data-retention operation.

## 4. Export source and eligibility boundary

Export reads only committed business records from the authoritative local database.

### 4.1 Eligible ordinary POS orders — approved Phase 4 rule

An order is eligible for the normal management export only when **all** of the following are true:

- it is an ordinary POS-originated order;
- it is not `Cancelled`;
- it is fully settled under the approved payment rules;
- its lifecycle status is `Closed`.

Accordingly:

- `Open` + unpaid -> not exported;
- `Open` + partially paid -> not exported;
- `Closed` + fully settled -> eligible;
- `Cancelled` -> not exported as a positive sale;
- Hiboutik paste-created -> never exported.

The exporter must derive eligibility from persisted source/lifecycle/payment state rather than from UI appearance or operator memory.

This rule is frozen in `docs/decisions/export-eligibility.md`.

### 4.2 Hiboutik paste-created orders

Orders created through the Hiboutik paste-import entry point are automatically excluded from this export because the underlying order already exists in Hiboutik.

The hidden source marker is sufficient for this exclusion. No special operator action is required.

### 4.3 Cancelled orders

Cancelled records remain in the POS database for history but are not exported as ordinary positive sales under the normal export path.

How to represent a cancellation that occurs **after** an order has already been exported is a separate correction question still to be frozen below.

## 5. Logical export datasets

V1 should expose two deterministic logical datasets even if the final physical workbook/file implementation maps them differently.

### 5.1 Order-level dataset

Each exported order record should be able to carry, where required by the target mapping:

- Sushi81 POS order ID;
- order creation date/time;
- planned fulfilment date/time;
- fulfilment mode;
- authoritative final TTC total;
- current lifecycle status relevant to the approved export rule;
- telephone/address/comment only if the existing management workflow genuinely needs those fields;
- cumulative CB and Espèce amounts/current payment result where required for reconciliation;
- relevant payment/settlement date information where the target workflow needs receipt-date attribution;
- source type for internal filtering/diagnostics, even if the target workbook does not expose it.

The physical column mapping must preserve the existing `Gestion SUSHI 81.xlsm` business meaning rather than introducing new columns merely because SQLite contains more data.

### 5.2 Sales-detail dataset

Each exported sales line should be able to carry the historical saved values required for product-sales analysis, including:

- parent Sushi81 POS order ID;
- order/business date required by the target workflow;
- product code snapshot;
- product name snapshot;
- quantity;
- saved base/unit selling amount;
- saved line amount and/or adjustment information required to reproduce the approved management analysis;
- relevant VAT information if required by the target mapping;
- status/exclusion information needed to avoid treating cancelled data as sales.

Structured product options are part of the POS historical order snapshot. Whether option labels/adjustments require dedicated target columns or are represented only through the effective financial values depends on the existing management workbook's analytical needs and will be resolved during physical mapping.

## 6. Historical financial fidelity

Export must use persisted order/item/payment/tax data and must not recalculate old orders from the current catalogue.

In particular:

- current product prices must not replace saved sale-time prices;
- current option definitions must not rewrite saved option selections/adjustments;
- current VAT settings must not replace persisted historical tax breakdowns;
- manual-total orders must preserve the already-approved authoritative total/tax treatment;
- payment totals used for reconciliation must come from the committed payment-adjustment ledger/current derived result rather than free-text comments.

## 7. Export must not mutate POS business history

A successful export does not:

- delete an order;
- delete its sales lines;
- change payment state;
- close or cancel an order;
- move the record into annual archive;
- change the order ID;
- rewrite historical snapshots.

Export tracking metadata may be stored separately for duplicate/correction control, but it is technical integration metadata rather than a business-state transition.

## 8. Deterministic and idempotent export — technical requirement

The export mechanism must be designed so that retrying after an uncertain failure does not casually duplicate the same business rows in `Gestion SUSHI 81.xlsm`.

Recommended technical model:

- each export run receives an opaque export-batch ID;
- the POS records which order IDs were included in a successfully completed batch;
- an order is not marked successfully exported until the physical export artifact/target write has completed and passed validation;
- temporary output is created first and only promoted/finalized after successful generation;
- a failed generation leaves the business records eligible for retry;
- diagnostics identify the failed batch without changing order/payment state.

The exact implementation may use an export ledger/table and content/version hashes where useful. Those are technical details provided they preserve the final business rules for re-export/corrections.

## 9. Physical Excel handling — technical safety baseline

ClosedXML is the approved workbook library.

Excel COM automation is not required.

For any generated workbook/file:

- use a pinned/tested ClosedXML version;
- write to a temporary path first;
- validate required sheets/columns/output counts before finalization;
- avoid overwriting the only good target copy after a generation failure;
- surface file-lock/access errors clearly;
- never treat an Excel write failure as a reason to remove or reset POS data.

Whether V1 writes directly into `Gestion SUSHI 81.xlsm` or produces a controlled intermediate export package remains an operator-workflow decision because it changes the daily transfer process.

## 10. Target mapping and compatibility verification

Before Phase 4/V1 freeze, the physical mapping must be validated against a representative safe copy/template of `Gestion SUSHI 81.xlsm`.

Validation should confirm at least:

- target sheet names;
- required columns and data types;
- order-ID representation;
- date/time representation;
- total/payment columns used by management reconciliation;
- sales-detail columns used by product analysis;
- preservation of any required workbook formulas/macros/formatting if the target workbook itself is modified;
- behavior when the target already contains previously exported order IDs.

No real sensitive production workbook/data is required in Git; fixtures/templates must be sanitized where committed.

## 11. Testing

Automated/integration tests should cover at least:

- one ordinary Retrait POS order;
- one ordinary Livraison POS order;
- multiple product lines;
- structured option adjustments;
- mixed VAT data;
- manual-total override;
- `Open` unpaid order excluded;
- `Open` partially paid order excluded;
- `Closed` fully settled ordinary POS order included;
- cancelled order excluded from ordinary positive-sale export;
- automatic exclusion of Hiboutik paste-created orders;
- retry after failed file generation;
- repeated export without duplicate insertion;
- already-exported order later modified, under the final correction rule;
- target file locked/unavailable;
- malformed/incompatible target template detected before destructive write.

## 12. Business/workflow decisions still requiring confirmation

Export eligibility is now frozen: only fully settled `Closed`, non-cancelled, ordinary POS-originated orders are eligible.

Three operator/business choices still materially change management data or the export workflow and must be resolved sequentially:

1. **Date/cutoff rule** — whether to preserve a fixed delay such as the legacy J-2 rule, use another fixed cutoff, or export eligible orders without that legacy delay.
2. **Physical transfer workflow** — whether Sushi81 POS writes directly into `Gestion SUSHI 81.xlsm` or generates a controlled intermediate workbook/file for the management workflow.
3. **Post-export modification/correction** — how an order that was already exported and is later modified/cancelled is represented without silently creating duplicate or inconsistent management history.

These are business/operational decisions because they change what data appears in the management workbook and how daily reconciliation is performed.

## 13. Approval rule

This document remains **Draft — Phase 4 working design** until the remaining business/workflow decisions in section 12 are frozen and the physical target mapping is validated against a representative `Gestion SUSHI 81.xlsm` template/copy.

Pure file-format, ClosedXML, temporary-file, hashing and export-ledger implementation details may be selected directly according to the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.
