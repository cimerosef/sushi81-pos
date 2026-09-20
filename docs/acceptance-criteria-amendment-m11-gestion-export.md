# Acceptance criteria amendment — M11 Gestion intermediate export

**Status:** Approved — V1 acceptance amendment  
**Date:** 2026-09-20  
**Applies to:** AC-EXP-001 through AC-EXP-011 where clarified below; AC-HIB-008 export cross-check  
**Decision source:** `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`

This amendment closes M11 readiness ambiguities without changing the approved four-sheet workbook boundary or the existing order-close invariant.

## AC-EXP-001 clarification — Closed is the positive-sale export gate

For ordinary POS-originated positive-sale export:

- `Open` orders are never eligible for `CREATE` or `UPDATE`;
- `Closed` is the lifecycle readiness gate;
- export does not introduce a second independently configurable/operator-visible payment gate;
- the existing lifecycle still requires exact CB + Espèce reconciliation before normal Close;
- `Cancelled` and `HIBOUTIK_PASTE` orders remain excluded from initial positive-sale export.

**Evidence:** selection tests covering Open unpaid/part-paid/otherwise, Closed reconciled, Cancelled and Hiboutik source.

## AC-EXP-002 clarification — correction date filtering

The optional inclusive date filter continues to use the order fulfilment/business date. It narrows both first-time eligible CREATE actions and applicable pending correction actions. It does not use payment effective date or technical export-generation date as the selection period.

**Evidence:** selection tests for CREATE/UPDATE/CANCEL inside and outside inclusive boundaries.

## AC-EXP-003 / AC-EXP-011 clarification — fixed V1 contract and SettlementDate

The V1 workbook remains the fixed four-sheet contract from `export.md`; per-run operator column selection is not supported.

`Orders.SettlementDate` is the business date on which cumulative effective-dated signed payment adjustments reach the committed authoritative order total. It is not merely `ClosedAt` and not `recorded_at`.

A Closed order for which this date cannot be derived consistently must fail closed rather than receive an invented value.

**Evidence:** workbook contract/cell-type tests plus payment-effective-date settlement tests, including later technical recording and zero-net payment-bucket reclassification.

## AC-EXP-009 clarification — UPDATE waits for Closed current state

After a successful CREATE, a committed modification creates/implies a pending UPDATE for the same stable `OrderId`.

An UPDATE is emitted only when the current committed order is again `Closed`. If modification reopens the order, the UPDATE remains pending until a later successful Close.

The emitted UPDATE is the full current committed replacement snapshot.

**Evidence:** correction-state tests covering Closed->modified-still-Closed and Closed->modified->Open->Closed.

## AC-EXP-010 clarification — CANCEL precedence

For an order with a prior successful export:

- later cancellation creates/implies `CANCEL`;
- CANCEL may be emitted without requiring current Closed/settled state;
- a pending UPDATE that has never been successfully emitted is superseded by the cancellation;
- the next applicable batch emits CANCEL rather than an unneeded UPDATE followed by CANCEL.

**Evidence:** correction-state tests covering CREATE->pending UPDATE->CANCEL and CREATE->successful UPDATE->later CANCEL.

## AC-HIB-008 M11 cross-check

M11 must prove that `HIBOUTIK_PASTE` orders cannot produce CREATE, UPDATE or CANCEL Gestion-export events, even if otherwise Closed and carrying apparently valid payment/line/tax snapshots.

`source_total_ttc` is reference-only and must not enter the Gestion export contract.

**Evidence:** SQLite/application export-selection and end-to-end workbook tests.
