# Export to Gestion SUSHI 81

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define the reliable export of eligible Sushi81 POS orders and sales detail into a controlled intermediate file for the downstream `Gestion SUSHI 81` workflow.

## 1. Scope

V1 keeps `Gestion SUSHI 81.xlsm` in the broader management workflow, but Sushi81 POS does not write into that workbook directly.

The approved boundary is:

**Sushi81 POS -> controlled intermediate export file -> separate/manual import into Gestion SUSHI 81**

This document defines POS-side eligibility, export content, duplicate protection, file-generation safety and the contract exposed to the downstream import process.

It does not redefine catalogue import/export, order/payment lifecycle, printing, annual SQLite archive behavior or the internal logic of the downstream management workbook/import process.

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

The UI may provide convenience presets such as Today, Yesterday or This week, but custom date selection must remain available.

The date filter never overrides the eligibility rules in section 3. An order inside the chosen range is still excluded if it is Open, partially paid, Cancelled, Hiboutik-originated or already successfully exported under the applicable idempotency/correction rules.

This rule is frozen in `docs/decisions/export-date-range.md`.

## 5. Logical export datasets

The intermediate export package must expose deterministic order-level and sales-detail datasets.

### 5.1 Order-level data

The export model must be able to provide the fields required by the management workflow, including where applicable:

- Sushi81 POS order ID;
- relevant order/business date and time;
- fulfilment mode and planned fulfilment date/time;
- authoritative TTC total;
- cumulative CB and Espèce result where required for reconciliation;
- settlement/payment date information where required by downstream reconciliation;
- other existing management fields only when genuinely required.

### 5.2 Sales-detail data

Sales-detail export must preserve the historical values required for product analysis, including where required:

- parent order ID;
- relevant business date;
- product code snapshot;
- product name snapshot;
- quantity;
- saved unit/base amount;
- saved line/effective adjustment values required by analysis;
- VAT information if required by the downstream mapping.

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

Export must be idempotent enough that retrying after a failure does not casually duplicate the same order in generated export packages.

The implementation should maintain export-batch/ledger metadata so that:

- an order is marked successfully exported only after the intermediate file is generated and validated successfully;
- failed generation remains safely retryable;
- already-successfully-exported orders are not included again by an ordinary repeated export;
- diagnostics can identify failed batches without altering business state.

Because downstream import is a separate manual step, stable Sushi81 POS order IDs must be present in the intermediate data so the downstream importer can also detect duplicates independently.

## 8. Intermediate-file workflow — approved Phase 4 rule

V1 does **not** write directly into `Gestion SUSHI 81.xlsm`.

Instead:

1. the operator launches export in Sushi81 POS;
2. POS determines the eligible not-yet-exported set, optionally narrowed by the selected date range;
3. POS generates a controlled intermediate export file;
4. POS validates the generated file;
5. only after successful validation is the export batch marked successful;
6. the operator later imports that file into the current Gestion workflow through a separate process.

The POS export transaction ends at successful generation of the intermediate file. It does not claim or track that the later Gestion import was actually performed.

This separation is intentional so that future changes to `Gestion SUSHI 81.xlsm`, its import rules, or the wider management workflow do not unnecessarily couple the POS to that implementation.

This rule is frozen in `docs/decisions/export-intermediate-file.md`.

## 9. Intermediate export contract — technical baseline

The V1 intermediate format should be a controlled, versionable tabular Excel-compatible package, generated without Excel COM.

The exact sheet/column design is a technical mapping task, but it must:

- carry a schema/version marker;
- preserve stable Sushi81 POS order IDs;
- separate or clearly distinguish order-level and sales-detail data;
- contain all fields required for the downstream Gestion import and analysis workflow;
- contain only persisted historical values, never current-catalogue recalculation;
- be generated to a temporary path first and finalized only after validation;
- fail safely without changing POS business data.

The filename should be unique enough to distinguish export runs and allow the operator to identify the generated period/batch without depending on file contents alone.

## 10. Required tests

Tests must cover at least:

- eligible `Closed` ordinary POS order included;
- unpaid and partially paid `Open` orders excluded;
- Cancelled order excluded;
- Hiboutik paste-created order excluded;
- default export with no date restriction includes all eligible not-yet-exported orders;
- optional inclusive custom date range narrows the eligible set;
- historical prices/options/VAT preserved;
- intermediate file generated and validated without access to `Gestion SUSHI 81.xlsm`;
- failed file generation safely retryable;
- repeated ordinary export does not duplicate already-exported records;
- stable order IDs present for downstream duplicate protection;
- post-export modification/cancellation under the final correction rule.

## 11. Remaining business/workflow decision

The following are frozen:

- only fully settled `Closed`, non-cancelled ordinary POS orders are eligible;
- default export has no mandatory date restriction and exports all eligible not-yet-exported orders;
- the operator may optionally narrow export with a custom inclusive date range;
- no mandatory J-2 cutoff remains;
- POS generates a controlled intermediate export file instead of writing directly into `Gestion SUSHI 81.xlsm`.

One business/workflow decision remains:

**Post-export correction — treatment of an order that has already been exported and is later modified or cancelled.**

## 12. Approval rule

This document remains **Draft — Phase 4 working design** until the remaining post-export correction decision is frozen and the intermediate-file field mapping is validated against the current downstream management needs.

Pure file naming, workbook layout, ClosedXML handling, temporary-file mechanics, schema-version encoding and export-ledger implementation details may be selected directly according to the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.
