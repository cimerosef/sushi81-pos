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
- lifecycle treatment of Hiboutik emergency-import copies.

It does not define catalogue structure, discount formulas, physical database tables, printing templates, external card-terminal settlement or the final export schema.

## 2. Authoritative baseline and Phase 2 refinement

The lifecycle design remains based on `current-system.md` and the approved `product-requirements.md` Phase 1 baseline.

Phase 2 freezes these simple principles:

- **payment state does not lock an order against modification**;
- **payment composition is recorded directly as cash and card amounts rather than through a manually selected payment-method category**;
- **the order has one authoritative editable total field**;
- **a manual total edit is temporary and any later price-affecting order change automatically recalculates the total under normal pricing rules**;
- **an order may be closed only when recorded cash + card exactly equals that current order total**;
- **daily summaries use the amount actually received on each date, not the nominal value of the whole order**;
- **only the latest saved order version needs to be retained for normal business use; v1 does not require an order revision-history feature.**

The following principles remain fixed:

1. A confirmed order is durably persisted and must survive application restart/failure.
2. Cancelling an order does not delete the retained order record.
3. Uncommitted edits can be abandoned without changing the persisted order.
4. Existing orders remain retrievable and reprintable according to retention/archive rules.
5. Future-order behavior is driven by structured planned fulfilment date/time.
6. A future order automatically leaves the future-orders area when its fulfilment date arrives and appears in the due-today advance-order reminder.
7. Due-today advance-order visibility is operational and does not depend on whether payment has already been entered.
8. Partial and mixed payments are representable structurally through CB and Espèce amounts.
9. Daily received-payment totals represent money actually received on that date, not unpaid order value.
10. Hiboutik emergency-import records are operational/printing copies, not new POS-originated sales; they remain excluded from ordinary POS turnover/card-entry/export totals.
11. External card-terminal refund/additional-charge handling remains outside the POS in v1.
12. The operator may directly edit the order total without changing catalogue product base prices.
13. A later change to products, quantities, options/price adjustments or discounts automatically recalculates and replaces any prior manual total override.
14. Every new order must explicitly select a fulfilment mode: `Retrait` or `Livraison`. No order may be confirmed without that selection.

## 3. Lifecycle dimensions

At minimum, an order has independent lifecycle dimensions:

- **business/order status** — `Open`, `Closed` or `Cancelled`;
- **payment information** — cumulative cash and card amounts recorded for the order;
- **order total** — one authoritative editable amount, normally system-calculated but directly editable by the operator;
- **planned fulfilment date/time** — used to derive future, due-today and overdue attention;
- **source type** — ordinary POS-originated order versus Hiboutik emergency-import copy.

Future-order, due-today and overdue labels are operational views derived from the underlying order data rather than separate destructive lifecycle states.

`Open` versus `Closed` is deliberately lightweight. It records only whether the order has passed the application's final arithmetic reconciliation check; it does not mean the POS controls the external payment process.

## 4. Target lifecycle model

### 4.1 Before confirmation

An in-progress cart/edit is not yet a durable business order. Leaving or cancelling the in-progress operation must not create an active order unless the operator explicitly confirms it.

Before confirmation, the operator must explicitly choose one fulfilment mode:

- `Retrait`; or
- `Livraison`.

There is no inherited/default fulfilment mode from a prior order when creating a new order from existing customer information. If no fulfilment mode has been selected, the application must reject confirmation and keep the order in progress.

### 4.2 Confirmation and open order

Confirmation creates the durable order record before printing is attempted. Print failure must therefore be recoverable by reprint and must not cause order loss.

A newly confirmed order is **Open**. An open order can be saved, printed, retrieved and modified normally even when payment information is still empty or incomplete.

### 4.3 Payment recording — approved Phase 2 decision

Each ordinary POS order provides two current cumulative amount fields:

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

### 4.4 Internal dated payment changes — approved Phase 2 decision

The operator-facing UI remains limited to the current cumulative `CB` and `Espèce` amounts, but the application must internally retain enough dated change information to calculate daily received-payment summaries correctly.

When an operator changes a cumulative payment amount, the application records the **difference** attributable to that change together with its effective date/time and payment bucket.

Example:

- an order total is €50;
- on day 1 the operator records CB = €20: day 1 receives a +€20 CB contribution;
- on day 2 the operator changes CB from €20 to €50: day 2 receives only the additional +€30 CB contribution;
- the whole €50 is not counted again on day 2.

The same principle applies independently to CB and Espèce.

If a later correction reduces a previously entered cumulative amount, the internal dated adjustment must be sufficient to keep daily summaries mathematically consistent. The normal operator UI still shows only the current cumulative amounts and does not expose a payment-event ledger unless a later requirement explicitly introduces one.

### 4.5 Editable order total — approved Phase 2 decision

The order has one ordinary **total** field.

The application normally calculates that field from product lines, quantities, options and applicable pricing/discount rules. The operator may then directly edit the same field when needed.

A manual edit becomes the authoritative order total at that moment. However, the manual value does **not** lock the order total against future system calculation.

If any price-affecting order input is subsequently changed — including products, quantities, product options/price adjustments or discount application — the application must automatically recalculate the total using the normal approved rules and overwrite the earlier manual value.

If the operator still wants a special total after that recalculation, the operator may simply edit the total again.

The application does not require a second visible calculated-total field and does not reject the order because product lines no longer mathematically add up to a manually edited total between recalculations.

### 4.6 Closing an order — approved Phase 2 decision

An order may be **Closed only when**:

`Card amount + Cash amount = Current order total`

The equality is mandatory to the cent.

Therefore:

- if the recorded payment total is less than the order total, closing is rejected;
- if the recorded payment total is greater than the order total, closing is rejected;
- if both payment fields are empty, closing is rejected unless the order total is itself zero under an explicitly valid future rule;
- if equality is satisfied, the order may be closed.

When closing is rejected, the application must show a clear arithmetic error and keep the order open. The operator may correct the CB/Espèce amounts or directly edit the order total as appropriate.

This validation prevents only the explicit close action. It does not prevent saving, printing or editing an open order.

A previously closed order may still be edited. If a later edit causes CB + Espèce to differ from the current order total, the order must again be treated as Open until the arithmetic close condition is satisfied.

### 4.7 Daily received-payment summary — approved Phase 2 decision

The current-day summary represents **money actually received on that date**, not the nominal value of every order created or due that day.

It must show at least:

- today's actual amount received;
- today's actual CB amount received;
- today's actual Espèce amount received.

An order does not need to be closed in order for an amount already received to contribute to the daily summary.

Examples:

- a €50 order with €20 actually received today contributes €20, not €50;
- the remaining unpaid €30 does not appear in today's received-payment summary;
- a fully unpaid open order contributes €0;
- if €20 was received yesterday and an additional €30 is received today, yesterday's summary contains €20 and today's summary contains €30.

The existence of open orders must not block generation or display of the daily summary.

The application may separately show open/unsettled orders as an operational reminder.

### 4.8 Future / due today / overdue

These are operational views derived from planned fulfilment date and settlement status:

- **future order:** planned fulfilment date is after today;
- **due-today advance order:** the order was created for a later date and that planned date is today;
- **overdue unsettled:** planned fulfilment date is before today and the order is not closed/fully reconciled.

The due-today advance-order reminder remains visible for the rest of that calendar day. A legitimate future order may remain open without affecting today's received-payment summary except for any amount actually received today.

### 4.9 Order modification — approved Phase 2 decision

**All non-cancelled orders may be modified regardless of whether they are Open or Closed.**

When an order is modified:

- the same business order ID is retained;
- the operator edits the existing order directly;
- only the latest saved business version is required for normal use;
- abandoning an in-progress edit restores the last persisted version unchanged;
- reprinting and current reporting use the latest saved version.

V1 does **not** require an operator-visible or business-retained order revision-history feature. The application only needs whatever minimal technical metadata is required for safe persistence and diagnostics; it does not need to preserve prior business versions as a user feature.

The operator may directly change the order total even if it differs from the product-line arithmetic.

If the operator later changes any product, quantity, option/price adjustment or discount, the application automatically recalculates the order total according to the approved pricing rules, replacing any prior manual total. The operator can then manually edit the newly calculated total again if needed.

External refund/additional-charge handling remains outside the POS.

### 4.10 Partial-payment / unsettled orders

If recorded CB + Espèce is greater than zero but less than the current order total, the order remains Open/unsettled and cannot be closed yet.

A partially paid order remains fully editable. Amount already actually received contributes only to the daily summary for the date on which it was received; the unpaid remainder does not.

### 4.11 Create a new order from an existing order — approved Phase 2 decision

The operator must be able to use any existing order as a source of customer information for a **new order with a new order ID**.

This action does not automatically alter the source order, whether that source is Open, Closed or Cancelled.

The new-order action copies reusable customer-related information including:

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
- product lines;
- planned fulfilment date/time;
- fulfilment mode (`Retrait` / `Livraison`).

The operator must explicitly choose `Retrait` or `Livraison` for the new order. This is a mandatory field and confirmation is rejected until one of the two modes is selected.

The new order receives its new ID only when confirmed.

### 4.12 Cancellation — approved Phase 2 decision

Cancellation is an explicit operator action and is distinct from ordinary modification.

A Cancelled order:

- remains retained as historical business data;
- is clearly marked `Cancelled`;
- retains its previously recorded CB/Espèce amounts for reference;
- is excluded from normal active-order turnover/statistical calculations, ordinary received-payment summaries and ordinary export unless a later export specification explicitly requires otherwise;
- may remain viewable/reprintable for reference where appropriate.

The POS does not automatically cancel and recreate an order merely because its contents were edited.

### 4.13 Hiboutik emergency-import copies

Emergency-imported Hiboutik orders participate in operational printing, reminders and discrepancy review where applicable, but they are not new POS-originated sales.

Their lifecycle must preserve:

- source identity as Hiboutik emergency import;
- original Hiboutik amount;
- POS operational/actual amount;
- recorded cash/card payment outcome used for reconciliation;
- exclusion from ordinary POS-originated received-payment/turnover totals, Hiboutik card-entry totals and `Gestion SUSHI 81.xlsm` export.

External monetary settlement remains outside the application.

## 5. Phase 2 lifecycle decisions frozen in this document

The following lifecycle decisions are now approved:

1. user-facing order statuses are limited to `Open`, `Closed` and `Cancelled` (with final FR/ZH wording to be handled by localization/UI design);
2. Cancelled orders retain CB/Espèce values for reference but are excluded from normal summaries and exports;
3. v1 keeps only the latest saved business version of an order and does not provide an order revision-history feature;
4. creating a new order from an existing order copies customer information but does not inherit `Retrait`/`Livraison`; fulfilment mode is mandatory and must be explicitly selected for every new order;
5. cross-day and partial payments are attributed by internally dated payment-amount changes while the operator-facing UI remains limited to the current cumulative CB/Espèce fields.

The following are not part of the target v1 lifecycle unless explicitly reintroduced later:

- a manually selected `CB` / `Espèce` / `DIV` / `Mixte` payment-method field;
- payment-state-based editing locks;
- supplementary-order chains for already-paid order edits;
- mandatory cancel-and-replace behavior for ordinary modifications;
- refund-pending/refund-completed states;
- POS-managed card-refund workflow;
- complex replacement-order financial linking;
- an operator-facing payment-event ledger;
- an operator-visible order revision-history feature;
- blocking the daily summary because one or more orders remain open;
- requiring a second visible system-calculated total alongside the editable order total;
- preserving a manual total override after later price-affecting order changes.

## 6. Approval rule

The core lifecycle behavior described above is now sufficiently specified for Phase 2 baseline review. Implementation must preserve these decisions and must not reintroduce more complex payment/replacement/audit workflows without explicit product approval.