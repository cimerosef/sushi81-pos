# M11 export lifecycle, settlement-date and correction clarifications

**Status:** Approved — post-freeze V1 amendment  
**Date:** 2026-09-20  
**Applies to:** M11 — Gestion intermediate export  
**Owner decision source:** project-owner approval in the M11 readiness discussion on 2026-09-20

## Decision

This record closes the material M11 ambiguities found during readiness review while preserving the existing order lifecycle.

### 1. Closed is the export gate

For ordinary positive-sale export, the export subsystem uses lifecycle status `Closed` as the operator/business readiness gate.

- An `Open` order is never emitted as `CREATE` or `UPDATE`, whether unpaid, partially paid or otherwise.
- A `Closed` ordinary POS order may be emitted subject to the remaining source/cancellation/idempotency rules.
- M11 does not add a second operator-visible or independently configurable payment-eligibility gate.

This does **not** weaken the existing order lifecycle. Under the approved lifecycle, normal `Close` remains possible only when CB + Espèce equals the authoritative order total exactly. Therefore a normal Closed order is already reconciled by the lifecycle invariant.

### 2. SettlementDate uses the actual payment business date

The exported `Orders.SettlementDate` represents the business date on which the committed order became fully settled according to the effective-dated payment ledger, not the later technical recording timestamp and not merely the date on which the operator clicked Close.

Implementation must derive this from persisted `PaymentAdjustment` effective-business-date facts.

For a deterministic current-state calculation, group signed CB + Espèce payment deltas by effective business date and use the business date on which cumulative effective-dated received money reaches the committed authoritative order total. A same-day or later CB/Espèce reclassification whose net received-money delta is zero does not by itself move `SettlementDate`.

If a data-corruption/invariant violation makes a Closed order's settlement date non-derivable, export must fail closed for that order/batch rather than invent a date.

### 3. Post-export UPDATE eligibility

After a successful `CREATE`, a later committed modification creates or implies a pending correction for the same stable `OrderId`.

- If the modified order is `Open`, the correction remains pending and is not emitted.
- Once the current committed order is again `Closed`, the next applicable export may emit one full-replacement `UPDATE`.
- The `UPDATE` contains the current committed order/line/tax/payment snapshot; it is not a line delta.

### 4. CANCEL supersedes a not-yet-emitted UPDATE

If an order that has already been exported is later cancelled:

- the next applicable action is `CANCEL`;
- any `UPDATE` that became pending after the previous successful export but was never itself successfully emitted is superseded;
- the POS must not emit a pointless intermediate UPDATE and then CANCEL solely because the modification happened first;
- `CANCEL` does not require the cancelled order to be Closed or currently settled.

A successfully emitted UPDATE remains historical export fact. A later cancellation still emits one later `CANCEL`.

### 5. Export fields are contract fields, not per-run operator choices

The V1 four-sheet workbook and its fields are fixed by `docs/export.md`.

The operator chooses the **order scope** (default all pending/applicable actions, optionally narrowed by inclusive fulfilment/business dates) and may regenerate a prior successful batch. The operator does not choose a different subset of columns for each run.

Any incompatible future field/meaning change is a schema-version change, not an ad-hoc checkbox selection.

## Rationale

The export is a stable machine-readable boundary into the Gestion workflow, not an arbitrary reporting tool. Fixed fields, stable IDs, Closed-only positive-sale export, effective-date settlement semantics and simple correction precedence make duplicate protection and exact regeneration deterministic while keeping order lifecycle authority in the existing order subsystem.
