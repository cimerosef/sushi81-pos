# Order lifecycle

**Status:** Draft — Phase 2 first batch  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Purpose:** Freeze the target order, modification, cancellation and payment lifecycle before implementation.

## 1. Scope

This document defines the target lifecycle semantics for Sushi81 POS. It translates the approved Phase 1 product requirements into implementable rules without choosing the database schema or UI technology.

The lifecycle must remain deliberately simple. Sushi81 POS is primarily an operational order and turnover-recording relay tool. It is not intended to become a payment-control, refund-accounting or financial-workflow system.

It covers:

- when an order becomes a durable business record;
- order-state semantics;
- payment-state semantics;
- future-order and overdue behavior;
- order modification and cancellation;
- payment recording and correction;
- lifecycle treatment of Hiboutik emergency-import copies;
- minimal history/audit expectations.

It does not define catalogue structure, discount formulas, physical database tables, printing templates, external card-terminal settlement or the final export schema.

## 2. Authoritative baseline and Phase 2 refinement

The lifecycle design remains based on `current-system.md` and the approved `product-requirements.md` Phase 1 baseline.

Phase 1 intentionally deferred the exact post-confirmation modification semantics. This Phase 2 document now refines that area with a simpler rule: **payment state does not lock an order against modification.**

The following principles remain fixed:

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
11. Partial and mixed payments must be representable structurally.
12. Hiboutik emergency-import records are operational/printing copies, not new POS-originated sales; they remain excluded from ordinary POS turnover/card-entry/export totals.
13. External card-terminal refund/additional-charge handling remains outside the POS in v1.

## 3. Lifecycle dimensions

The target design must avoid overloading one status field with unrelated meanings. At minimum, an order has independent lifecycle dimensions:

- **business/order state** — active/committed versus cancelled;
- **payment state** — unpaid/pending, partially paid or fully settled;
- **planned fulfilment date/time** — used to derive future, due-today and overdue attention;
- **source type** — ordinary POS-originated order versus Hiboutik emergency-import copy.

Future-order, due-today and overdue labels are operational views derived from the underlying order data rather than separate destructive lifecycle states.

## 4. Target lifecycle model

### 4.1 Before confirmation

An in-progress cart/edit is not yet a durable business order. Leaving or cancelling the in-progress operation must not create an active order unless the operator explicitly confirms it.

### 4.2 Confirmation

Confirmation creates the durable order record before printing is attempted. Print failure must therefore be recoverable by reprint and must not cause order loss.

### 4.3 Payment

Payment state records the operator's current understanding of the payment outcome. The model must support:

- no payment received;
- partial payment received;
- fully settled.

Where payment events are retained, they support daily received-payment totals and later correction. They do not create a payment workflow that restricts whether an order may be edited.

### 4.4 Future / due today / overdue

These are operational views derived from planned fulfilment date and payment state:

- **future order:** planned fulfilment date is after today;
- **due-today advance order:** the order was created for a later date and that planned date is today;
- **overdue unsettled:** planned fulfilment date is before today and payment is not fully settled.

The due-today advance-order reminder remains visible for the rest of that calendar day; v1 does not introduce a separate collected/processed state merely to dismiss it early.

### 4.5 Order modification — approved Phase 2 decision

**All active orders may be modified regardless of payment state.**

This includes orders that are:

- unpaid;
- partially paid;
- fully settled.

Payment state must not technically lock the order or force the operator into a supplementary-order/replacement-order workflow.

When an active order is modified:

- the **same business order ID is retained**;
- the operator edits the existing order directly;
- the latest committed version is the version shown by default in normal operational screens;
- current turnover/order reporting uses the latest committed order contents unless another explicit rule says otherwise;
- reprinting uses the latest committed order version;
- abandoning an in-progress edit restores the last persisted version unchanged;
- the system should retain a lightweight internal revision history sufficient to understand that the order was changed, without turning normal operation into an audit workflow.

This rule deliberately replaces the former Excel/VBA workaround in which a modified order was marked `ANNULE` and recreated under a new ID.

It also deliberately rejects a more complex payment-driven modification model. If an already-paid order is changed and money must be refunded or additionally collected, the operator handles that practical settlement outside Sushi81 POS, for example through the card terminal or cash handling. The POS does not need to model `refund pending`, `refund completed`, supplementary-order chains or similar financial states.

The business responsibility of Sushi81 POS is to record the order/turnover information that the operator has decided should be retained as the correct final record.

### 4.6 Partial-payment orders

Partial payment is a special payment condition, not a separate order lifecycle.

A partially paid order remains editable under the same rule as every other active order. The application should show the recorded payment amount(s) and the resulting remaining balance based on the latest order total.

If a modification creates an unusual situation that requires money to be returned or otherwise corrected, the operator resolves the monetary difference outside the POS and then records the final intended order/payment information as needed. v1 does not require automated refund-state management.

### 4.7 Cancellation

Cancellation is an explicit operator action and is distinct from ordinary modification.

A cancelled order:

- remains retained as historical business data;
- is clearly marked cancelled;
- is excluded from active-order turnover/statistical calculations and ordinary export unless a later export specification explicitly requires otherwise;
- may remain viewable/reprintable for reference where appropriate.

The POS does not automatically cancel and recreate an order merely because its contents were edited.

### 4.8 Hiboutik emergency-import copies

Emergency-imported Hiboutik orders participate in operational printing, reminders and discrepancy review where applicable, but they are not new POS-originated sales.

Their lifecycle must preserve:

- source identity as Hiboutik emergency import;
- original Hiboutik amount;
- POS operational/actual amount;
- recorded payment outcome used for reconciliation;
- exclusion from ordinary POS-originated turnover, Hiboutik card-entry totals and `Gestion SUSHI 81.xlsm` export.

They should follow the same general principle of operator-controlled correction: the POS records the final operational information the operator intends to retain, while external monetary settlement remains outside the application.

## 5. Decisions still to freeze

The lifecycle has now been simplified substantially. The remaining Phase 2 decisions are limited to:

1. exact user-facing names for the active and cancelled order states;
2. exact supported payment-method labels and how mixed/partial payment is displayed;
3. how payment correction is represented internally without creating unnecessary workflow complexity;
4. whether a cancelled order may retain previously recorded payment information for reference and how that appears in summaries;
5. how much revision history is visible to the operator versus retained only internally.

The following earlier possibilities are **not part of the target v1 lifecycle** unless explicitly reintroduced later:

- payment-state-based editing locks;
- supplementary-order chains for already-paid order edits;
- mandatory cancel-and-replace behavior for ordinary modifications;
- refund-pending/refund-completed states;
- POS-managed card-refund workflow;
- complex replacement-order financial linking.

## 6. Approval rule

This file remains a Draft until the remaining lifecycle presentation/payment-detail decisions are reviewed and explicitly approved. Implementation must preserve the core Phase 2 rule that ordinary order modification is operator-controlled and is not restricted by payment state.