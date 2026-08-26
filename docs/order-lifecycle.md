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

Phase 1 intentionally deferred the exact post-confirmation modification and payment-entry semantics. Phase 2 now refines those areas with these simple rules:

- **payment state does not lock an order against modification**;
- **payment composition is recorded directly as cash and card amounts rather than through a manually selected payment-method category**;
- **the order has one authoritative editable total field**;
- **an order may be closed only when recorded cash + card exactly equals that current order total.**

The application normally calculates and fills the order-total field from products/options/discount rules, but the operator may directly edit that same field under `business-rules.md`. There is no requirement for a second visible system-total field.

Closing an order and generating the current-day received-payment summary are separate concepts. An open order does not block the daily summary; the summary excludes amounts that have not actually been received.

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
12. The operator may directly edit the order total without changing catalogue product base prices.

## 3. Lifecycle dimensions

At minimum, an order has independent lifecycle dimensions:

- **business/order status** — open, closed or cancelled;
- **payment information** — cumulative cash and card amounts recorded for the order;
- **order total** — one authoritative editable amount, normally system-calculated but directly editable by the operator;
- **planned fulfilment date/time** — used to derive future, due-today and overdue attention;
- **source type** — ordinary POS-originated order versus Hiboutik emergency-import copy.

Future-order, due-today and overdue labels are operational views derived from the underlying order data rather than separate destructive lifecycle states.

`Open` versus `Closed` is deliberately lightweight. It records only whether the order has passed the application's final arithmetic reconciliation check; it does not mean the POS controls the external payment process.

## 4. Target lifecycle model

### 4.1 Before confirmation

An in-progress cart/edit is not yet a durable business order. Leaving or cancelling the in-progress operation must not create an active order unless the operator explicitly confirms it.

### 4.2 Confirmation and open order

Confirmation creates the durable order record before printing is attempted. Print failure must therefore be recoverable by reprint and must not cause order loss.

A newly confirmed order may remain **open**. An open order can be saved, printed, retrieved and modified normally even when payment information is still empty or incomplete.

### 4.3 Payment recording — approved Phase 2 decision

Each ordinary POS order provides two amount fields:

- **Card / CB amount**;
- **Cash / Espèce amount**.

The operator does not need to choose a separate `CB`, `Espèce` or `Mixte` value. Payment composition is derived automatically:

- both empty: payment composition not yet recorded;
- one amount entered and the other empty: the empty field is treated as zero;
- CB > 0 and Espèce = 0: card-only;
- Espèce > 0 and CB = 0: cash-only;
- both > 0: mixed payment.

The two amount fields remain editable during reconciliation.

The POS should display where useful:

- current order total;
- recorded CB amount;
- recorded Espèce amount;
- total recorded payment;
- difference between recorded payment and current order total.

Operational payment instruments are grouped only into these two business buckets. Instruments processed through the card terminal belong to the CB bucket; cash-equivalent paper instruments handled as cash belong to the Espèce bucket according to Sushi 81's operating practice.

### 4.4 Editable order total — approved Phase 2 decision

The order has one ordinary **total** field.

The application normally calculates that field from product lines, quantities, options and applicable pricing/discount rules. The operator may then directly edit the same field when needed.

Once manually edited, that entered amount is the authoritative total for the order until it is deliberately changed again. The application does not require a separate visible calculated-total field and does not reject the order because product lines no longer mathematically add up to the edited total.

If product lines or pricing inputs are changed after a manual total edit, the UI must not silently overwrite the operator's intentional total without a clear recalculation action or equivalent explicit behavior defined during UI design.

### 4.5 Closing an order — approved Phase 2 decision

An order may be **closed only when**:

`Card amount + Cash amount = Current order total`

The equality is mandatory to the cent.

Therefore:

- if the recorded payment total is less than the order total, closing is rejected;
- if the recorded payment total is greater than the order total, closing is rejected;
- if both payment fields are empty, closing is rejected unless the order total is itself zero under an explicitly valid future rule;
- if equality is satisfied, the order may be closed.

When closing is rejected, the application must show a clear arithmetic error and keep the order open. The operator may correct the CB/Espèce amounts or directly edit the order total as appropriate.

This validation prevents only the explicit close action. It does not prevent saving, printing or editing an open order.

### 4.6 Daily received-payment summary — approved Phase 2 decision

The current-day summary represents **money actually received**, not the nominal value of every order created or due that day.

It must show at least:

- today's actual amount received;
- today's actual CB amount received;
- today's actual Espèce amount received.

An order does not need to be closed in order for an amount already received to contribute to the daily summary.

Examples:

- a €50 order with €20 actually received today contributes €20, not €50;
- the remaining unpaid €30 does not appear in today's received-payment summary;
- a fully unpaid open order contributes €0;
- a fully paid order contributes only the amount actually received on the relevant receipt date(s).

The existence of open orders must not block generation or display of the daily summary.

The application may separately show open/unsettled orders as an operational reminder.

For daily attribution to remain correct when payment is completed across different calendar days, the implementation must retain enough internal date information to distinguish when money was actually received. This does not imply an operator-facing payment-event ledger.

### 4.7 Future / due today / overdue

These are operational views derived from planned fulfilment date and settlement status:

- **future order:** planned fulfilment date is after today;
- **due-today advance order:** the order was created for a later date and that planned date is today;
- **overdue unsettled:** planned fulfilment date is before today and the order is not closed/fully reconciled.

The due-today advance-order reminder remains visible for the rest of that calendar day. A legitimate future order may remain open without affecting today's received-payment summary except for any amount actually received today.

### 4.8 Order modification — approved Phase 2 decision

**All non-cancelled orders may be modified regardless of whether they are open or closed.**

When an order is modified:

- the same business order ID is retained;
- the operator edits the existing order directly;
- the latest committed version is shown by default and used for current printing/reporting;
- abandoning an in-progress edit restores the last persisted version unchanged;
- the system should retain lightweight internal revision history without turning normal operation into an audit workflow.

The operator may directly change the order total even if it differs from the product-line arithmetic.

If a previously closed order is modified so that its order total no longer equals recorded CB + Espèce, it no longer satisfies the close validation until the operator corrects the relevant values. External refund/additional-charge handling remains outside the POS.

### 4.9 Partial-payment / unsettled orders

If recorded CB + Espèce is greater than zero but less than the current order total, the order remains open/unsettled and cannot be closed yet.

A partially paid order remains fully editable. Amount already actually received may contribute to the appropriate daily summary; the unpaid remainder does not.

### 4.10 Create a new order from an existing order — approved Phase 2 decision

The operator must be able to use any existing order as a source of customer information for a **new order with a new order ID**.

This action does not automatically alter the source order, whether that source is open, closed or cancelled.

The new-order action should copy reusable customer-related information including at least:

- telephone number, when present;
- delivery address, when present;
- free-text customer/order comment or notes, when present.

The copied values remain editable before confirmation.

The following are not inherited as transaction data:

- source order ID;
- source order status;
- recorded CB/Espèce amounts;
- source order total;
- source order creation timestamp;
- revision/audit data.

Product lines are not copied by this customer-information action. Planned fulfilment date/time must be newly set or reconfirmed. The new order receives its new ID only when confirmed.

### 4.11 Cancellation

Cancellation is an explicit operator action and is distinct from ordinary modification.

A cancelled order:

- remains retained as historical business data;
- is clearly marked cancelled;
- is excluded from active-order turnover/statistical calculations and ordinary export unless a later export specification explicitly requires otherwise;
- may remain viewable/reprintable for reference where appropriate.

The POS does not automatically cancel and recreate an order merely because its contents were edited.

### 4.12 Hiboutik emergency-import copies

Emergency-imported Hiboutik orders participate in operational printing, reminders and discrepancy review where applicable, but they are not new POS-originated sales.

Their lifecycle must preserve:

- source identity as Hiboutik emergency import;
- original Hiboutik amount;
- POS operational/actual amount;
- recorded cash/card payment outcome used for reconciliation;
- exclusion from ordinary POS-originated received-payment/turnover totals, Hiboutik card-entry totals and `Gestion SUSHI 81.xlsm` export.

External monetary settlement remains outside the application.

## 5. Decisions still to freeze

The remaining Phase 2 lifecycle decisions are limited to:

1. exact user-facing labels for open, closed and cancelled orders;
2. whether a cancelled order retains previously recorded cash/card amounts for reference and how those amounts are excluded or reversed from summaries;
3. how much revision history is visible to the operator versus retained only internally;
4. whether any additional customer-related fields beyond telephone, address and comment should be copied by the new-order-from-existing action;
5. the simplest internal mechanism for attributing received amounts to the correct date in rare cross-day partial-payment cases while keeping the operator UI limited to CB/Espèce amount entry.

The following are not part of the target v1 lifecycle unless explicitly reintroduced later:

- a manually selected `CB` / `Espèce` / `DIV` / `Mixte` payment-method field;
- payment-state-based editing locks;
- supplementary-order chains for already-paid order edits;
- mandatory cancel-and-replace behavior for ordinary modifications;
- refund-pending/refund-completed states;
- POS-managed card-refund workflow;
- complex replacement-order financial linking;
- an operator-facing payment-event ledger;
- blocking the daily summary because one or more orders remain open;
- requiring a second visible system-calculated total alongside the editable order total.

## 6. Approval rule

This file remains a Draft until the remaining lifecycle presentation/detail decisions are reviewed and explicitly approved. Implementation must preserve the core Phase 2 rules that ordinary order modification is operator-controlled and not restricted by payment state, payment composition is entered through CB/Espèce amounts, the order uses one authoritative editable total field, closing requires CB + Espèce to equal that total, daily summaries include only amounts actually received, and a new order can be initialized from an existing order's reusable customer information without changing the source order.