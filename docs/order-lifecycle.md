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
- order open/closed/cancelled semantics;
- payment recording and arithmetic validation;
- future-order and overdue behavior;
- order modification and cancellation;
- creation of a new order from an existing order's customer information;
- daily received-payment summaries;
- lifecycle treatment of Hiboutik emergency-import copies;
- minimal history/audit expectations.

It does not define catalogue structure, discount formulas, physical database tables, printing templates, external card-terminal settlement or the final export schema.

## 2. Authoritative baseline and Phase 2 refinement

The lifecycle design remains based on `current-system.md` and the approved `product-requirements.md` Phase 1 baseline.

Phase 1 intentionally deferred the exact post-confirmation modification and payment-entry semantics. This Phase 2 document now refines those areas with three simple rules:

- **payment state does not lock an order against modification**;
- **payment composition is recorded directly as cash and card amounts rather than through a manually selected payment-method category**;
- **an order may be closed only when recorded cash + card exactly equals the current order total.**

Closing an order and generating the current-day received-payment summary are separate concepts. An open order does not block the daily summary; the summary simply excludes amounts that have not actually been received.

The following principles remain fixed:

1. A confirmed order is durably persisted and must survive application restart/failure.
2. Cancelling an order must not silently delete required business history.
3. Uncommitted edits can be abandoned without changing the persisted order.
4. Existing orders remain retrievable and reprintable according to retention/archive rules.
5. Future-order behavior is driven by structured planned fulfilment date/time.
6. A future order automatically leaves the future-orders area when its fulfilment date arrives and appears in the due-today advance-order reminder.
7. Due-today advance-order visibility is operational and does not depend on whether payment has already been entered.
8. Partial and mixed payments must be representable structurally.
9. Daily received-payment totals represent money actually received on that date, not unpaid order value.
10. Hiboutik emergency-import records are operational/printing copies, not new POS-originated sales; they remain excluded from ordinary POS turnover/card-entry/export totals.
11. External card-terminal refund/additional-charge handling remains outside the POS in v1.

## 3. Lifecycle dimensions

The target design must avoid overloading one status field with unrelated meanings. At minimum, an order has independent lifecycle dimensions:

- **business/order status** — open, closed or cancelled;
- **payment information** — cumulative cash and card amounts recorded for the order;
- **planned fulfilment date/time** — used to derive future, due-today and overdue attention;
- **source type** — ordinary POS-originated order versus Hiboutik emergency-import copy.

Future-order, due-today and overdue labels are operational views derived from the underlying order data rather than separate destructive lifecycle states.

`Open` versus `Closed` is deliberately lightweight. It does not mean the POS controls the external payment process. It records only whether the order has passed the application's final arithmetic reconciliation check.

## 4. Target lifecycle model

### 4.1 Before confirmation

An in-progress cart/edit is not yet a durable business order. Leaving or cancelling the in-progress operation must not create an active order unless the operator explicitly confirms it.

### 4.2 Confirmation and open order

Confirmation creates the durable order record before printing is attempted. Print failure must therefore be recoverable by reprint and must not cause order loss.

A newly confirmed order may remain **open**. An open order can be saved, printed, retrieved and modified normally even when payment information is still empty or incomplete.

Closing the order is a separate reconciliation action governed by section 4.4.

### 4.3 Payment recording — approved Phase 2 decision

The operator-facing payment model is deliberately minimal.

Each ordinary POS order provides two amount fields:

- **Card / CB amount**;
- **Cash / Espèce amount**.

The operator does not need to choose a separate `CB`, `Espèce` or `Mixte` payment-method value. The practical payment composition is derived automatically from the two amounts.

The entry semantics are:

- if **both fields are empty**, no payment composition has yet been recorded;
- if one field contains an amount and the other field is empty, the empty field is treated as zero for calculation;
- card amount > 0 and cash amount = 0 means card-only payment;
- cash amount > 0 and card amount = 0 means cash-only payment;
- both amounts > 0 means mixed payment.

The two amount fields remain editable while the operator is reconciling the order. The normal workflow can therefore leave both fields empty when the order is created and fill them later when the actual payment outcome is known.

The POS should calculate and display, where useful:

- current order total;
- recorded card amount;
- recorded cash amount;
- total recorded payment;
- difference between recorded payment and current order total.

Operational payment instruments are grouped only into these two business buckets. Instruments processed through the card terminal belong to the card bucket; cash-equivalent paper instruments handled as cash belong to the cash bucket according to Sushi 81's operating practice.

### 4.4 Closing an order — approved Phase 2 decision

An order may be **closed only when**:

`Card amount + Cash amount = Current order total`

The equality is mandatory to the cent.

Therefore:

- if the recorded payment total is **less than** the current order total, closing is rejected;
- if the recorded payment total is **greater than** the current order total, closing is rejected;
- if both payment fields are empty, closing is rejected unless the current order total is itself zero under an explicitly valid future rule;
- if the equality is satisfied, the order may be closed.

When closing is rejected, the application must show a clear arithmetic error and keep the order open. The operator can then correct the cash/card amounts or modify the order total as appropriate.

This validation does **not** prevent saving, printing or editing an open order. It prevents only the explicit close action.

Closing an order is also **not** a prerequisite for producing the current-day payment summary.

### 4.5 Daily received-payment summary — approved Phase 2 decision

The current-day summary must represent **money actually received**, not the nominal value of every order created or due that day.

The summary must therefore show at least:

- today's actual amount received;
- today's actual card / CB amount received;
- today's actual cash / Espèce amount received.

An order does **not** need to be closed in order for an amount already received to contribute to the daily summary.

Examples:

- a €50 order with €20 actually received today contributes **€20**, not €50;
- the remaining unpaid €30 does not appear in today's received-payment summary;
- a fully unpaid open order contributes €0;
- a fully paid order contributes only the amount actually received on the relevant receipt date(s).

The existence of open orders must therefore **not block generation or display of the daily summary**.

The application may separately show a count/list of open or unsettled orders so the operator knows that further reconciliation remains, but this is an operational reminder rather than a condition that prevents the summary from being calculated.

For daily attribution to remain correct when payment is completed across different calendar days, the implementation must retain enough internal date information to distinguish when money was actually received. This requirement does not imply an operator-facing payment-event ledger; the user-facing workflow should remain the simple CB/Espèce amount entry described above.

### 4.6 Future / due today / overdue

These are operational views derived from planned fulfilment date and settlement status:

- **future order:** planned fulfilment date is after today;
- **due-today advance order:** the order was created for a later date and that planned date is today;
- **overdue unsettled:** planned fulfilment date is before today and the order is not closed/fully reconciled.

The due-today advance-order reminder remains visible for the rest of that calendar day; v1 does not introduce a separate collected/processed state merely to dismiss it early.

A legitimate future order may remain open without affecting today's received-payment summary except for any amount actually received today.

### 4.7 Order modification — approved Phase 2 decision

**All non-cancelled orders may be modified regardless of whether they are open or closed.**

Payment/reconciliation state must not technically lock the order or force the operator into a supplementary-order/replacement-order workflow.

When an order is modified:

- the **same business order ID is retained**;
- the operator edits the existing order directly;
- the latest committed version is the version shown by default in normal operational screens;
- current turnover/order reporting uses the latest committed order contents unless another explicit rule says otherwise;
- reprinting uses the latest committed order version;
- abandoning an in-progress edit restores the last persisted version unchanged;
- the system should retain a lightweight internal revision history sufficient to understand that the order was changed, without turning normal operation into an audit workflow.

If a previously closed order is modified so that its current total no longer equals the recorded CB + Espèce amounts, it must no longer satisfy the close validation until the operator corrects the relevant values. The POS does not model the external refund/additional-charge process; that practical settlement remains outside the application.

The business responsibility of Sushi81 POS is to record the order/turnover information that the operator has decided should be retained as the correct record.

### 4.8 Partial-payment / unsettled orders

Partial payment is a derived condition, not a separate manually selected payment method.

If the sum of the recorded card and cash amounts is greater than zero but less than the current order total, the order remains open/unsettled and cannot be closed yet.

A partially paid order remains fully editable. The application shows the recorded CB/Espèce amounts and the difference from the latest order total.

The amount already actually received may contribute to the appropriate daily received-payment summary even though the order remains open. The unpaid remainder does not.

### 4.9 Create a new order from an existing order — approved Phase 2 decision

The operator must be able to use any existing order as a source of customer information for a **new order with a new order ID**.

This action is independent of modification and cancellation:

- the source order may remain active/open/closed;
- the source order may later be cancelled;
- the source order may already be cancelled if it is still available for consultation;
- creating the new order does not automatically change the source order in any way.

The purpose is to avoid retyping recurring customer information when a customer places another order or when the operator decides that creating a fresh order is more convenient than modifying the existing one.

The new-order action should copy customer-related information that is useful to reuse, including at least:

- telephone number, when present;
- delivery address, when present;
- free-text customer/order comment or notes, when present.

The copied values are only starting values for the new order and remain fully editable before confirmation.

The following information must **not** be inherited as if it belonged to the new transaction:

- source order ID;
- source order business/cancellation/open/closed state;
- recorded cash/card payment amounts;
- source order total;
- source order creation timestamp;
- historical revision/audit data.

Product lines are not copied by this customer-information action. If a later workflow requires duplicating an entire prior basket, that should be treated as a separate explicitly approved feature rather than being implied here.

Planned fulfilment date/time must be newly set or reconfirmed for the new order rather than silently inherited from the source order, so that an old same-day or future-order date cannot accidentally become the new order's fulfilment instruction.

The new order receives its own new order ID only when it is confirmed under the normal new-order workflow.

### 4.10 Cancellation

Cancellation is an explicit operator action and is distinct from ordinary modification.

A cancelled order:

- remains retained as historical business data;
- is clearly marked cancelled;
- is excluded from active-order turnover/statistical calculations and ordinary export unless a later export specification explicitly requires otherwise;
- may remain viewable/reprintable for reference where appropriate.

The POS does not automatically cancel and recreate an order merely because its contents were edited.

### 4.11 Hiboutik emergency-import copies

Emergency-imported Hiboutik orders participate in operational printing, reminders and discrepancy review where applicable, but they are not new POS-originated sales.

Their lifecycle must preserve:

- source identity as Hiboutik emergency import;
- original Hiboutik amount;
- POS operational/actual amount;
- recorded cash/card payment outcome used for reconciliation;
- exclusion from ordinary POS-originated received-payment/turnover totals, Hiboutik card-entry totals and `Gestion SUSHI 81.xlsm` export.

They should follow the same general principle of operator-controlled correction: the POS records the final operational information the operator intends to retain, while external monetary settlement remains outside the application.

## 5. Decisions still to freeze

The lifecycle has now been simplified substantially. The remaining Phase 2 decisions are limited to:

1. exact user-facing labels for open, closed and cancelled orders;
2. whether a cancelled order retains previously recorded cash/card amounts for reference and how those amounts are excluded or reversed from summaries;
3. how much revision history is visible to the operator versus retained only internally;
4. whether any additional customer-related fields beyond telephone, address and comment should be copied by the new-order-from-existing action;
5. the simplest internal mechanism for attributing received amounts to the correct date in rare cross-day partial-payment cases while keeping the operator UI limited to CB/Espèce amount entry.

The following earlier possibilities are **not part of the target v1 lifecycle** unless explicitly reintroduced later:

- a manually selected `CB` / `Espèce` / `DIV` / `Mixte` payment-method field;
- payment-state-based editing locks;
- supplementary-order chains for already-paid order edits;
- mandatory cancel-and-replace behavior for ordinary modifications;
- refund-pending/refund-completed states;
- POS-managed card-refund workflow;
- complex replacement-order financial linking;
- an operator-facing payment-event ledger;
- blocking the daily received-payment summary merely because one or more orders remain open.

## 6. Approval rule

This file remains a Draft until the remaining lifecycle presentation/detail decisions are reviewed and explicitly approved. Implementation must preserve the core Phase 2 rules that ordinary order modification is operator-controlled and is not restricted by payment state, that payment composition is entered through cash/card amounts rather than a separate payment-method selector, that an order can close only when CB + Espèce equals its current total, that daily summaries include only amounts actually received, and that a new order can be initialized from an existing order's reusable customer information without changing the source order.