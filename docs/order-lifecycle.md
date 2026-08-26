# Order lifecycle

**Status:** Draft — Phase 2 first batch  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Purpose:** Freeze the target order, modification, cancellation and payment lifecycle before implementation.

## 1. Scope

This document defines the target lifecycle semantics for Sushi81 POS. It translates the approved Phase 1 product requirements into implementable rules without choosing the database schema or UI technology.

It covers:

- when an order becomes a durable business record;
- order-state semantics;
- payment-state semantics;
- future-order and overdue behavior;
- modification, replacement and supplementary-order behavior;
- cancellation behavior;
- payment recording and correction;
- lifecycle treatment of Hiboutik emergency-import copies;
- history/audit expectations.

It does not define catalogue structure, discount formulas, physical database tables, printing templates or the final export schema.

## 2. Authoritative Phase 1 baseline

The lifecycle design must remain consistent with `current-system.md` and the approved `product-requirements.md` baseline.

The following principles are already fixed by Phase 1:

1. **Order state and payment state are separate concepts.**
2. A confirmed order is durably persisted and must survive application restart/failure.
3. Cancelling an order must not silently delete required business history.
4. Uncommitted edits can be abandoned without changing the persisted order.
5. Existing orders remain retrievable and reprintable according to retention/archive rules.
6. Future-order behavior is driven by structured planned fulfilment date/time.
7. A future order automatically leaves the future-orders area when its fulfilment date arrives and appears in the due-today advance-order reminder.
8. Due-today advance-order visibility is operational and does not depend on payment state.
9. An order becomes overdue-unsettled only when its planned fulfilment date is earlier than today and it is not fully settled.
10. Payment totals are attributed to the date money is actually received, not to order creation/fulfilment date.
11. Partial and mixed payments must be represented structurally.
12. Actual payment events should be retained with enough history to support correction, reconciliation and year-boundary settlement.
13. Already-settled orders must not be silently rewritten in a way that destroys financial history.
14. Actual external card refunds remain outside the POS in v1.
15. Hiboutik emergency-import records are operational/printing copies, not new POS-originated sales; they remain excluded from ordinary POS turnover/card-entry/export totals.

## 3. Lifecycle dimensions

The target design should avoid overloading one status field with unrelated meanings. At minimum, an order has independent lifecycle dimensions:

- **business/order state** — whether the order is an active committed order, cancelled, replaced, etc.;
- **payment state** — unpaid/pending, partially paid or fully settled;
- **planned fulfilment date/time** — used to derive future, due-today and overdue attention;
- **source type** — ordinary POS-originated order versus Hiboutik emergency-import copy.

Future-order, due-today and overdue labels should normally be derived operational views rather than destructive state transitions that rewrite the order's underlying business history.

## 4. Draft lifecycle model

### 4.1 Before confirmation

An in-progress cart/edit is not yet a durable business order. Leaving or cancelling the in-progress operation must not create an active order unless the operator has explicitly confirmed it.

### 4.2 Confirmation

Confirmation creates the durable order record before printing is attempted. Print failure must therefore be recoverable by reprint and must not cause order loss.

### 4.3 Payment

Payment state is derived from valid payment information associated with the order. The model must support:

- no payment received;
- part of the order total received;
- full order total received.

Payment events must carry the actual received date/time and method so daily received-payment totals can be calculated correctly.

### 4.4 Future / due today / overdue

These are operational views derived from planned fulfilment date and payment state:

- **future order:** planned fulfilment date is after today;
- **due-today advance order:** the order was created for a later date and that planned date is today;
- **overdue unsettled:** planned fulfilment date is before today and payment is not fully settled.

The due-today advance-order reminder remains visible for the rest of that calendar day; v1 does not introduce a separate collected/processed state merely to dismiss it early.

### 4.5 Modification of an unsettled order — approved Phase 2 decision

A committed order that is still unpaid or partially paid and is otherwise permitted to be modified keeps the **same business order ID** when its contents or operational information are changed.

The target application must not reproduce the Excel/VBA workaround of marking the original order `ANNULE` and creating a new order ID merely because an ordinary editable order was revised.

Instead:

- the operator edits the existing order;
- the business order ID remains stable;
- the latest approved version is the version shown by default in normal operational screens and used for current printing;
- the system preserves enough internal revision/audit history to reconstruct that a prior committed version existed and what materially changed;
- abandoning an in-progress edit leaves the last persisted version unchanged;
- reprinting after a committed modification uses the latest persisted order version.

This rule is particularly important for future orders, which may legitimately be revised multiple times before fulfilment and should not accumulate meaningless cancelled replacement order IDs.

This decision removes a technical workaround imposed by the former Excel/VBA storage model; it does not change the business meaning of an order modification.

The exact storage representation of revisions is an architecture/data-model decision to be specified later.

### 4.6 Cancellation and settled-order replacement

Cancellation must preserve history. Phase 1 also requires a safer target rule than silently editing financial history after payment.

The same-ID modification rule above applies to unpaid and partially paid orders while modification remains allowed. It does **not** by itself authorize arbitrary rewriting of an already fully settled order.

The exact state names and the exact linking semantics for settled-order supplementary/replacement flows remain to be approved in this document.

### 4.7 Hiboutik emergency-import copies

Emergency-imported Hiboutik orders participate in operational printing, reminders and discrepancy review where applicable, but they are not new POS-originated sales.

Their lifecycle must preserve:

- source identity as Hiboutik emergency import;
- original Hiboutik amount;
- POS operational/actual amount;
- recorded payment outcome used for reconciliation;
- exclusion from ordinary POS-originated turnover, Hiboutik card-entry totals and `Gestion SUSHI 81.xlsm` export.

## 5. Decisions still to freeze

The following Phase 2 decisions must be explicitly approved before this document can become baseline:

1. exact business/order-state names and allowed transitions;
2. whether a normal committed order needs a distinct `CONFIRMED`/`ACTIVE` state name or can use a simpler model;
3. ~~exact semantics for modifying an unpaid order~~ — **approved: retain the same business order ID and preserve internal revision history**;
4. detailed limits on modifying a partially paid order beyond the same-ID principle;
5. exact semantics for increasing an already-settled order, including supplementary-order linking;
6. exact semantics for reducing/cancelling an already-settled order, including replacement linking and how the POS records that an external refund may be required or has been handled;
7. whether payment corrections are implemented as reversible/corrective events, immutable events with supersession, or another auditable model;
8. exact supported payment-method labels and how mixed payments are displayed;
9. whether a cancelled order can retain valid received-payment history and, if so, how that is shown operationally;
10. minimal history/audit information shown to the operator versus retained internally.

## 6. Approval rule

This file remains a Draft until the unresolved lifecycle decisions above are reviewed and explicitly approved. Production implementation must not infer answers to these questions from the current Excel/VBA behavior.