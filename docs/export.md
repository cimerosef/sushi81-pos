# Export to Gestion SUSHI 81

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define the reliable export of eligible Sushi81 POS orders and sales detail into the existing `Gestion SUSHI 81.xlsm` workflow.

## 1. Scope

V1 keeps `Gestion SUSHI 81.xlsm` in the workflow. Export transfers approved POS-originated order-level and sales-detail data for management history, reconciliation and product-sales analysis.

This document does not redefine catalogue import/export, order/payment lifecycle, printing or annual SQLite archive behavior.

## 2. Frozen baseline

The following rules are already approved:

1. The live SQLite database remains the authoritative POS business store.
2. Export never deletes POS orders or sales lines and never acts as a retention mechanism.
3. Historical values come from persisted order/item/payment/tax snapshots, not from the current catalogue.
4. Hiboutik paste-created orders are excluded automatically because the underlying sale already exists in Hiboutik.
5. Excel processing uses the approved ClosedXML architecture; Excel COM automation is not required.

## 3. Export eligibility — approved Phase 4 rule

An order is eligible for the normal management export only when all of the following are true:

- ordinary POS-originated source;
- not `Cancelled`;
- fully settled under the approved payment rules;
- lifecycle status `Closed`.

Therefore:

- `Open` + unpaid: excluded;
- `Open` + partially paid: excluded;
- `Closed` + fully settled: eligible;
- `Cancelled`: excluded from ordinary positive-sale export;
- Hiboutik paste-created: always excluded.

Eligibility is derived from persisted data, not from UI appearance or operator memory.

This rule is frozen in `docs/decisions/export-eligibility.md`.

## 4. Default export scope and optional date filter — approved Phase 4 rule

V1 does **not** impose the legacy fixed J-2 cutoff and does **not** require the operator to choose a date period before every export.

### 4.1 Default behavior

With no date filter selected, the normal export action considers **all eligible orders that have not already been successfully exported**.

In practical terms, the default action is:

**export all eligible settled orders still waiting for export.**

### 4.2 Optional date range

The operator may optionally narrow the default set by selecting:

- start date;
- end date.

Both boundaries are inclusive.

The optional date range applies to the order's business/fulfilment date used for sales-period reporting.

The UI may provide convenience presets such as Today, Yesterday, This week or another useful period, but custom date selection must remain available.

The date filter never overrides the eligibility rules in section 3. An order inside the chosen range is still excluded if it is Open, partially paid, Cancelled, Hiboutik-originated or already successfully exported under the applicable idempotency/correction rules.

This rule is frozen in `docs/decisions/export-date-range.md`.

## 5. Logical export datasets

### 5.1 Order-level data

The export model must be able to provide the fields required by the existing management workbook, including where applicable:

- Sushi81 POS order ID;
- relevant order/business date and time;
- fulfilment mode and planned fulfilment date/time;
- authoritative TTC total;
- cumulative CB and Espèce result where required for reconciliation;
- settlement/payment date information where required by the target mapping;
- other existing management fields only when they are genuinely required.

The exporter must preserve the existing business meaning of `Gestion SUSHI 81.xlsm` rather than adding every field that happens to exist in SQLite.

### 5.2 Sales-detail data

Sales-detail export must preserve the historical values required for product analysis, including where required:

- parent order ID;
- relevant business date;
- product code snapshot;
- product name snapshot;
- quantity;
- saved unit/base amount;
- saved line/effective adjustment values required by the target analysis;
- VAT information if required by the workbook mapping.

Current catalogue prices/options/VAT must never rewrite historical exported values.

## 6. Export does not change business state

A successful export does not:

- delete or archive an order;
- close, reopen or cancel an order;
- alter payment state;
- change the order ID;
- rewrite historical snapshots.

Separate technical export metadata may track successful batches and exported order IDs.

## 7. Duplicate protection and retry — technical requirement

Export must be idempotent enough that retrying after a failure does not casually duplicate the same order in the management workflow.

The implementation should maintain export-batch/ledger metadata so that:

- an order is marked successfully exported only after the physical export succeeds and is validated;
- failed runs remain safely retryable;
- already-successfully-exported orders are not inserted again by an ordinary repeated export;
- diagnostics can identify failed batches without altering business state.

Temporary-file generation, validation and atomic finalization are technical implementation details selected under the project priority order.

## 8. Physical Excel safety

ClosedXML is the approved workbook library.

The exporter must:

- validate the expected target workbook/sheets/columns before destructive writing;
- use temporary output and safe finalization where appropriate;
- surface file-lock/access errors clearly;
- never reset or delete POS data because Excel writing failed.

Whether V1 writes directly into `Gestion SUSHI 81.xlsm` or generates a controlled intermediate export file remains a business/workflow decision.

## 9. Target mapping verification

Before V1 freeze, the export mapping must be validated against a representative safe copy/template of `Gestion SUSHI 81.xlsm`.

Validation must confirm at least:

- target sheet names;
- required columns and data types;
- order-ID representation;
- date/time representation;
- total/payment columns used by reconciliation;
- sales-detail columns used by product analysis;
- preservation of required formulas/macros/formatting if the target workbook itself is modified;
- handling of order IDs already present in the target.

No sensitive production data needs to be committed to Git.

## 10. Required tests

Tests must cover at least:

- eligible `Closed` ordinary POS order included;
- unpaid and partially paid `Open` orders excluded;
- Cancelled order excluded;
- Hiboutik paste-created order excluded;
- default export with no date restriction includes all eligible not-yet-exported orders;
- optional inclusive custom date range narrows the eligible set;
- eligible order outside the selected optional range excluded for that run;
- historical prices/options/VAT preserved;
- failed export safely retryable;
- repeated export does not duplicate already-exported records;
- locked or incompatible target detected safely;
- post-export modification/cancellation under the final correction rule.

## 11. Remaining business/workflow decisions

The following are frozen:

- only fully settled `Closed`, non-cancelled ordinary POS orders are eligible;
- default export has no mandatory date restriction and exports all eligible not-yet-exported orders;
- the operator may optionally narrow export with a custom inclusive date range;
- no mandatory J-2 cutoff remains.

Two business/workflow decisions remain:

1. **Physical transfer workflow** — direct write into `Gestion SUSHI 81.xlsm` versus a controlled intermediate export file.
2. **Post-export correction** — treatment of an already-exported order that is later modified or cancelled.

## 12. Approval rule

This document remains **Draft — Phase 4 working design** until the remaining decisions in section 11 are frozen and the physical mapping is validated against a representative management-workbook copy/template.
