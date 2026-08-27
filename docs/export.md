# Export to Gestion SUSHI 81

**Status:** Approved — Phase 4 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define the reliable export of eligible Sushi81 POS orders and sales detail into a controlled intermediate file for the downstream `Gestion SUSHI 81` workflow.

## 1. Scope

V1 keeps `Gestion SUSHI 81.xlsm` in the broader management workflow, but Sushi81 POS does not write into that workbook directly.

The approved boundary is:

**Sushi81 POS -> controlled intermediate export file -> separate/manual import into Gestion SUSHI 81**

This document defines POS-side eligibility, export content, duplicate protection, file-generation safety, post-export correction behavior and the versioned intermediate-file contract.

It does not redefine catalogue import/export, order/payment lifecycle, printing, annual SQLite archive behavior or the internal logic of the downstream management workbook/import process.

## 2. Frozen baseline

The following rules are approved:

1. The live SQLite database remains the authoritative POS business store.
2. Export never deletes POS orders or sales lines and never acts as a retention mechanism.
3. Historical values come from persisted order/item/payment/tax snapshots, not from the current catalogue.
4. Hiboutik paste-created orders are excluded automatically because the underlying sale already exists in Hiboutik.
5. Excel processing uses the approved ClosedXML architecture; Excel COM automation is not required.

## 3. Export eligibility — approved Phase 4 rule

An order is eligible for the normal positive-sale export only when all of the following are true:

- ordinary POS-originated source;
- not `Cancelled`;
- fully settled under the approved payment rules;
- lifecycle status `Closed`.

Therefore:

- `Open` + unpaid: excluded;
- `Open` + partially paid: excluded;
- `Closed` + fully settled: eligible;
- `Cancelled`: excluded from initial positive-sale export;
- Hiboutik paste-created: always excluded.

Eligibility is derived from persisted data, not from UI appearance or operator memory.

This rule is frozen in `docs/decisions/export-eligibility.md`.

## 4. Default export scope and optional date filter — approved Phase 4 rule

V1 does **not** impose the legacy fixed J-2 cutoff and does **not** require the operator to choose a date period before every export.

### 4.1 Default behavior

With no date filter selected, the normal export action considers **all eligible orders that have not already been emitted in a successfully generated export batch**, together with any pending legitimate post-export correction actions.

In practical terms, the default action is:

**export all eligible settled orders still waiting for export.**

### 4.2 Optional date range

The operator may optionally narrow the default set by selecting:

- start date;
- end date.

Both boundaries are inclusive.

The optional date range applies to the order's business/fulfilment date used for sales-period reporting.

The UI may provide convenience presets such as Today, Yesterday or This week, but custom date selection must remain available.

The date filter never overrides the eligibility/correction rules. It only narrows the set considered for that run.

This rule is frozen in `docs/decisions/export-date-range.md`.

## 5. Intermediate workbook — approved technical contract

V1 exports a versioned `.xlsx` intermediate workbook.

The workbook contains four logical sheets:

1. `Meta`
2. `Orders`
3. `OrderLines`
4. `TaxBreakdown`

The format is deliberately independent from the exact current column layout of `Gestion SUSHI 81.xlsm`. A downstream importer may map this stable POS contract into the current or future management workflow.

### 5.1 `Meta`

`Meta` records at least:

- `SchemaVersion` — V1 starts at `1.0`;
- `BatchId` — opaque unique export-batch identifier;
- `GeneratedAt` — export generation timestamp;
- `AppVersion` — Sushi81 POS version that generated the file;
- `FilterStartDate` — blank when no optional date filter was used;
- `FilterEndDate` — blank when no optional date filter was used;
- `OrderCount`;
- `OrderLineCount`;
- `TaxBreakdownCount`.

### 5.2 `Orders`

One row represents one exported order action.

V1 columns are:

- `Action` — controlled value `CREATE`, `UPDATE` or `CANCEL`;
- `OrderId` — stable Sushi81 POS order ID;
- `OrderStatus`;
- `CreatedAt`;
- `FulfilmentDate`;
- `FulfilmentTime`;
- `SettlementDate`;
- `FulfilmentMode`;
- `TotalTTC`;
- `CBTotal`;
- `EspeceTotal`;
- `Telephone`;
- `Address`;
- `Comment`.

Customer/operational fields are included because the intermediate contract is intended to remain useful even if the downstream management workflow evolves. They do not imply a CRM role for the POS.

### 5.3 `OrderLines`

For `CREATE` and `UPDATE`, the file contains the full latest committed line snapshot for the order rather than only a delta.

V1 columns are:

- `Action` — `CREATE` or `UPDATE` matching the parent order action;
- `OrderId`;
- `LineNo` — stable ordering within the exported order snapshot;
- `ProductCode`;
- `ProductName`;
- `Quantity`;
- `UnitBaseTTC`;
- `OptionAdjustmentTTC`;
- `LineTTC`;
- `VATRate`;
- `OptionsSummary`.

`OptionsSummary` is a readable historical snapshot of the selected structured options. Financial fields remain authoritative for calculation/import purposes.

A `CANCEL` action does not require positive sales-line rows: the downstream importer cancels the previously imported order by stable `OrderId`.

### 5.4 `TaxBreakdown`

For `CREATE` and `UPDATE`, V1 exports the persisted order-level tax snapshot rather than recalculating from current catalogue settings.

Columns are:

- `Action` — `CREATE` or `UPDATE`;
- `OrderId`;
- `VATRate`;
- `TaxableHT`;
- `VATAmount`;
- `TTC`.

A `CANCEL` action does not require tax rows because downstream cancellation is keyed by the already imported `OrderId`.

### 5.5 Cell/data representation

- Monetary fields are numeric euro values with two-decimal business precision; they are not localized text strings.
- Dates/times are written as Excel date/time values, not locale-dependent free text.
- IDs, product codes and action values are text.
- The workbook contains values, not formulas required for correctness.
- Column names and controlled action values are schema-contract identifiers and therefore are not translated with the POS UI language switch.

A future incompatible contract change increments `SchemaVersion` rather than silently changing the meaning of existing columns.

## 6. Historical financial fidelity

Export uses persisted historical snapshots.

It must never replace historical values with current catalogue data.

In particular:

- current product prices must not replace sale-time prices;
- current option definitions must not rewrite saved selections/adjustments;
- current VAT settings must not replace persisted tax breakdowns;
- manual-total orders preserve the approved authoritative total and tax treatment;
- CB/Espèce totals come from the committed payment ledger/derived current result rather than free-text comments.

## 7. Export does not change business state

A successful export does not:

- delete or archive an order;
- close, reopen or cancel an order;
- alter payment state;
- change the order ID;
- rewrite historical snapshots.

Separate technical export metadata tracks batches, emitted order actions and correction state.

## 8. Duplicate protection, retry and exact batch regeneration

Export must distinguish accidental duplicate export from a legitimate later correction.

The export ledger must ensure that:

- a business action is marked successfully emitted only after the intermediate file has been generated and validated successfully;
- failed generation remains safely retryable;
- an already-successfully-emitted unchanged `CREATE` is not included again in an ordinary new batch;
- a legitimate later `UPDATE` or `CANCEL` for the same order ID remains exportable;
- diagnostics identify failed/successful batches without altering business state.

Because the downstream import is manual and POS does not know whether the user has already imported a generated file, the application must retain enough immutable emitted-batch payload metadata to allow the operator to **regenerate the exact same successful batch** if its file is lost or damaged.

Exact regeneration:

- uses the same `BatchId`;
- reproduces the same emitted business rows;
- does not create a new `CREATE`/`UPDATE`/`CANCEL` business event;
- does not use a later modified order state in place of the originally emitted snapshot.

This is technical export history, not a general order-revision-history feature.

## 9. Post-export correction — approved Phase 4 rule

### 9.1 Modification before first successful export

If an order is modified before it has ever been successfully exported, the later ordinary export contains only the latest committed state as `CREATE`.

### 9.2 Modification after successful export

If an already-exported order is later modified, the POS records a pending correction for the same stable `OrderId`.

The next applicable export package carries `Action = UPDATE`.

`UPDATE` is a **full replacement snapshot**, not a line-by-line delta. The downstream importer must replace/update the previously imported state for that same `OrderId` rather than create a second sale.

### 9.3 Cancellation after successful export

If an already-exported order is later cancelled, the POS records a pending correction for the same stable `OrderId`.

The next applicable export package carries `Action = CANCEL`.

The downstream importer must reverse/remove/mark cancelled the previously imported sale for that same `OrderId` rather than create another positive record.

### 9.4 Correction tracking

A pending `UPDATE` or `CANCEL` may be emitted even though the same `OrderId` appeared in an earlier successful batch.

Once the correction batch is generated and validated successfully, that correction action is marked emitted in the technical export ledger.

The downstream importer is expected to process `CREATE`, `UPDATE` and `CANCEL` idempotently by stable `OrderId`.

This rule is frozen in `docs/decisions/export-post-export-correction.md`.

## 10. Intermediate-file workflow — approved Phase 4 rule

V1 does **not** write directly into `Gestion SUSHI 81.xlsm`.

Instead:

1. the operator launches export in Sushi81 POS;
2. POS determines the eligible not-yet-emitted set, optionally narrowed by date, together with applicable pending corrections;
3. POS generates the controlled intermediate `.xlsx` file to a temporary path;
4. POS validates schema, row relationships and expected counts;
5. POS finalizes the file only after validation succeeds;
6. only then is the batch marked successfully emitted;
7. the operator later imports that file into the current Gestion workflow through a separate process.

The POS export transaction ends at successful generation of the intermediate file. It does not claim or track that the later Gestion import was actually performed.

This separation is intentional so that future changes to `Gestion SUSHI 81.xlsm`, its import rules or the wider management workflow do not unnecessarily couple the POS to that implementation.

This rule is frozen in `docs/decisions/export-intermediate-file.md`.

## 11. File generation and naming — technical baseline

ClosedXML is used without Excel COM automation.

Generation must:

- write to a temporary path first;
- validate the workbook before finalization;
- fail safely without changing POS business data;
- never overwrite the only existing good export file after a failure.

Recommended filename pattern:

`Sushi81_POS_Export_YYYYMMDD_HHMMSS_<BatchId>.xlsx`

The exact short `BatchId` rendering is technical, provided the file name remains unique and recognizable.

## 12. Required validation and tests

Tests must cover at least:

- eligible `Closed` ordinary POS order exported as `CREATE`;
- unpaid and partially paid `Open` orders excluded;
- Cancelled order excluded from initial positive-sale export;
- Hiboutik paste-created order excluded;
- default export with no date restriction includes all eligible not-yet-emitted orders;
- optional inclusive date range narrows the eligible set;
- historical prices/options/VAT preserved;
- all four required sheets and schema fields generated correctly;
- monetary/date/text cell types remain contract-compatible;
- `OrderLines` and `TaxBreakdown` reference valid parent `OrderId` values;
- failed file generation safely retryable;
- repeated ordinary export does not duplicate an already-emitted unchanged `CREATE`;
- exact regeneration of a previous successful batch reproduces the same payload;
- pre-export modification exports only latest state as `CREATE`;
- post-export modification produces full replacement `UPDATE` for the same `OrderId`;
- post-export cancellation produces `CANCEL` for the same `OrderId`;
- repeated correction handling remains idempotent downstream.

A sanitized fixture importer/test workbook may later be built for automated compatibility testing, but the POS export contract does not depend on access to the production `Gestion SUSHI 81.xlsm` workbook.

## 13. Frozen Phase 4 decisions

The Phase 4 export design is frozen:

- initial export eligibility = fully settled `Closed`, non-cancelled ordinary POS orders only;
- default export = all eligible not-yet-emitted orders;
- optional inclusive date-range filter remains available;
- no mandatory J-2 cutoff;
- POS generates a controlled intermediate `.xlsx` file rather than writing directly into Gestion;
- stable order IDs provide downstream identity;
- `UPDATE` and `CANCEL` represent post-export corrections;
- `UPDATE` contains a full replacement snapshot;
- intermediate schema is versioned and independent from the current Gestion workbook layout;
- successful batches can be regenerated exactly without inventing a new business export action.

## 14. Approval

**Approved — Phase 4 baseline.**

All material V1 export business rules and the POS-side intermediate-file contract are frozen. Downstream import into `Gestion SUSHI 81` may evolve independently as long as it respects the versioned export contract.
