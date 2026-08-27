# Order lifecycle

**Status:** Approved — Phase 2 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Freeze the target order, modification, cancellation and payment lifecycle before implementation.

## 1. Scope

This document defines the target lifecycle semantics for Sushi81 POS. It translates the approved product boundary into implementable lifecycle rules without choosing physical database schema or UI technology.

The lifecycle remains deliberately simple. Sushi81 POS is primarily an operational order and turnover-recording relay tool. It is not a payment-control, refund-accounting or financial-workflow system.

It covers:

- when an order becomes a durable business record;
- Open / Closed / Cancelled semantics;
- payment recording and arithmetic validation;
- future-order, due-today and overdue behavior;
- order modification and cancellation;
- creation of a new order from existing customer information;
- real-time operational turnover;
- daily received-payment summaries;
- source treatment of Hiboutik paste-created orders.

It does not define catalogue structure, detailed pricing formulas, physical database tables, printing templates, external card-terminal settlement or the export-file schema.

## 2. Frozen lifecycle principles

The following principles are authoritative for V1:

1. A confirmed order is durably persisted and must survive application restart/failure.
2. Printing happens after persistence; print failure does not undo the order.
3. Business status is limited to `OPEN`, `CLOSED` and `CANCELLED`.
4. Payment information is independent from business status.
5. Payment composition is recorded through current cumulative CB and Espèce amounts, not a separate manually selected payment-method category.
6. The order has one authoritative editable total field.
7. A manual total edit is temporary: any later price-affecting order change recalculates the total under normal pricing rules and replaces the manual override.
8. An order may be Closed only when current CB + current Espèce equals the current authoritative order total exactly to the cent.
9. All non-cancelled orders remain modifiable regardless of Open/Closed state.
10. Ordinary modification keeps the same stable business order ID and replaces the latest saved business state; V1 does not require operator-visible order revision history.
11. Cancelling an order does not delete its retained business record or its recorded CB/Espèce facts.
12. Uncommitted edits can be abandoned without changing the persisted order.
13. Future/due-today/overdue are derived operational views, not destructive lifecycle statuses.
14. Real-time operational turnover and received-payment summaries are separate metrics.
15. Operational turnover is attributed to planned fulfilment date and does not depend on payment/closure state.
16. Daily received-payment summaries use the payment amount actually attributed to each date, not the nominal whole order value.
17. Every new order must explicitly select `Retrait` or `Livraison`; confirmation is rejected if neither is selected.
18. External card-terminal refund/additional-charge execution remains outside Sushi81 POS V1.
19. A Hiboutik paste-created order follows the same ordinary order lifecycle and UI as any other order; only a hidden source discriminator remains to enforce anti-double-counting exclusions.

## 3. Lifecycle dimensions

An order has independent lifecycle dimensions:

- **business status** — `OPEN`, `CLOSED`, `CANCELLED`;
- **payment information** — current cumulative CB and Espèce amounts derived from persisted dated adjustments;
- **authoritative order total** — normally system-calculated but directly editable by the operator;
- **planned fulfilment date/time** — used for future/due-today/overdue derivation and operational-turnover attribution;
- **advance-order marker** — sticky persisted fact that the order has ever participated in the future-order workflow;
- **source discriminator** — ordinary POS-originated versus Hiboutik paste-created, hidden from normal operator workflow.

The source discriminator is not a user-facing order type and does not create a separate lifecycle.

## 4. Target lifecycle model

### 4.1 Before confirmation

An in-progress new cart is not yet a durable business order.

Leaving/cancelling it before confirmation creates no active order.

Before confirmation, the operator must explicitly choose exactly one fulfilment mode:

- `Retrait`; or
- `Livraison`.

When a new order is initialized from reusable information from an older order, fulfilment mode is not inherited. The operator must choose it again.

### 4.2 Confirmation and initial Open state

Confirmation:

1. validates the current order under approved business rules;
2. allocates the stable Sushi81 POS order ID;
3. commits the complete order transaction durably;
4. only after successful commit invokes the normal printing workflow.

A newly confirmed order starts `OPEN`.

An Open order can be saved, viewed, printed, retrieved and modified even when payment information is empty or incomplete.

### 4.3 Payment recording

The operator works with two current cumulative amount fields:

- **Card / CB**;
- **Cash / Espèce**.

No separate `CB`, `Espèce`, `DIV` or `Mixte` order-level selector is required.

Composition is derived automatically:

- CB = 0 and Espèce = 0 -> unpaid/payment not recorded;
- CB > 0 and Espèce = 0 -> card only;
- CB = 0 and Espèce > 0 -> cash only;
- both > 0 -> mixed.

Where useful the UI should show:

- current order total;
- current CB;
- current Espèce;
- total recorded payment;
- difference between recorded payment and current total.

The two amounts remain editable during later reconciliation/correction.

### 4.4 Internally dated payment changes

The operator-facing UI remains cumulative, but the application internally persists enough signed dated changes to calculate received-payment totals correctly across days.

When a current cumulative amount changes, the application records the difference for the affected CB/Espèce bucket with an effective date/time.

Example:

- day 1: CB changes €0 -> €20 => day 1 receives `+€20` CB;
- day 2: CB changes €20 -> €50 => day 2 receives only `+€30` CB;
- a later reduction records a negative delta on its effective date.

The application does not need to expose a payment-event ledger as a normal V1 operator feature.

### 4.5 Editable order total

The order has one ordinary authoritative total field.

Normal pricing writes the calculated result into that field. The operator may directly edit the same field when an exceptional real-world amount is needed.

A manual edit becomes authoritative immediately but does not lock the total.

A later price-affecting change — including products, quantities, options/adjustments, discount application or applicable delivery fee — automatically recalculates the total under normal rules and replaces the earlier manual value.

The operator may then manually edit the recalculated value again if needed.

The operator is not required to maintain a second visible calculated-total field or provide a mandatory reason for a manual total.

VAT behavior for a manual total is defined in `business-rules.md` and `data-model.md`.

### 4.6 Closing an order

An order may be Closed only when:

`CB + Espèce = authoritative order total`

Equality is exact to €0.01.

Therefore:

- underpayment blocks closing;
- overpayment blocks closing;
- no recorded payment blocks closing unless a valid zero-total order rule applies;
- equality permits the explicit Close action.

A rejected close shows a clear arithmetic error and leaves the order Open/editable.

Saving and printing an Open order are not blocked merely because it is unsettled.

A previously Closed order may be modified. If the latest saved change makes CB + Espèce differ from the current authoritative total, status becomes Open again until the close condition is satisfied again.

### 4.7 Daily received-payment summary

For a business date D, the received-payment summary represents money actually received/effectively attributed on D.

It shows at least:

- actual total received on D;
- actual CB received on D;
- actual Espèce received on D.

It is based on dated payment deltas, not nominal order totals.

Examples:

- €50 order with €20 received today contributes €20 today;
- unpaid €30 remainder contributes €0 today;
- €20 yesterday + €30 today appears as €20 yesterday and €30 today;
- an Open order does not block the summary.

Ordinary summaries exclude Cancelled orders and Hiboutik paste-created orders according to the approved source/status boundaries.

### 4.8 Real-time operational turnover

Operational turnover answers the practical question: **how much valid ordinary POS business belongs to this fulfilment date?**

For a date D:

- include ordinary POS-originated orders whose planned fulfilment date is D;
- exclude Cancelled orders;
- sum each included order's current authoritative total;
- ignore whether the order is Open/Closed/unpaid/partial/settled.

Consequences:

- unpaid ordinary order due today contributes its full total to today's turnover;
- a future order created today for tomorrow contributes to tomorrow's turnover, not today's;
- if that future order is prepaid today, the payment contributes to today's received-payment summary while the order contributes to tomorrow's turnover;
- an old order receiving payment today contributes the new payment delta today but does not move its operational turnover to today.

Hiboutik paste-created orders are excluded because the underlying sale already exists in Hiboutik and must not be counted as new POS-originated turnover.

### 4.9 Future, due-today and overdue views

These are derived operational views:

- **future order**: `planned_fulfilment_date > current_business_date` and not Cancelled;
- **due-today advance order**: planned date = current business date, `advance_order_marker = true`, not Cancelled;
- **overdue unsettled**: planned date < current business date, not Cancelled and not fully Closed/reconciled under the approved lifecycle.

The persisted `advance_order_marker` is sticky:

- new order begins `false`;
- if a non-cancelled order is saved while planned date is later than the then-current business date, set it `true`;
- once true, it never resets for that order;
- later changes to date/time/products/payment/customer data do not reset it;
- cancellation need not clear it because Cancelled status independently excludes the order from active reminder views.

Payment state and Open/Closed status do not remove the due-today advance-order reminder.

### 4.10 Modification

All non-cancelled orders may be modified regardless of Open/Closed state.

Saving a modification:

- retains the same order ID;
- replaces the latest saved business values;
- uses the latest saved state for current reporting and reprinting;
- does not create a revision-history chain as a V1 business feature.

Abandoning an in-progress modification leaves the last persisted state unchanged.

A later price-affecting modification recalculates the total as described in section 4.5.

External refund/additional-charge handling remains outside the POS.

### 4.11 Partial-payment / unsettled order

If CB + Espèce is greater than zero but below the current total, the order remains Open/unsettled and cannot be Closed.

The order remains editable. Only amounts actually received on each date contribute to that date's received-payment summary.

### 4.12 Create a new order from existing customer information

The operator may use an existing order as a source of reusable text information for a completely new order.

Reusable information may include:

- telephone;
- delivery address;
- comment/notes.

The copied values remain editable.

The new order does **not** inherit:

- source order ID/status;
- CB/Espèce amounts;
- source total;
- creation timestamp;
- products;
- planned fulfilment date/time;
- fulfilment mode.

The operator explicitly selects Retrait/Livraison again. A new order ID is allocated only on confirmation.

The source order is unchanged whether it is Open, Closed or Cancelled.

### 4.13 Cancellation

Cancellation is an explicit operator action distinct from ordinary modification.

A Cancelled order:

- remains retained;
- is clearly marked Cancelled;
- retains previously recorded CB/Espèce facts for reference;
- is excluded from ordinary active turnover and ordinary received-payment summaries;
- is excluded from initial positive-sale export;
- remains viewable and printable/reprintable under `printing.md`;
- is not automatically recreated as another order.

### 4.14 Hiboutik paste-created orders — simplified approved V1 rule

A Hiboutik paste-created order uses the **same ordinary order lifecycle** after the operator confirms it.

It may therefore use ordinary:

- Open / Closed / Cancelled status;
- planned fulfilment date/time and advance-order reminders;
- products/options/cart editing;
- authoritative-total rules;
- CB/Espèce fields;
- modification/cancellation;
- printing/reprinting.

Its Hiboutik origin is represented only by a hidden source discriminator required for anti-double-counting.

The operator does not manage a special source type or emergency workflow.

V1 does **not** preserve or require:

- a dedicated emergency-order UI/style/count;
- `EmergencyImportDetail`;
- immutable original Hiboutik total;
- Hiboutik-vs-POS discrepancy status;
- Hiboutik-specific reconciliation/payment workflow;
- dedicated Hiboutik reference field.

If a Hiboutik reference is useful, it may be entered in the ordinary order comment.

Because the underlying web sale already exists in Hiboutik, the hidden source discriminator automatically excludes the order from:

- ordinary POS-originated operational turnover;
- ordinary POS-originated received-payment summaries;
- the ordinary POS CB amount that must newly be represented/entered in Hiboutik;
- export to `Gestion SUSHI 81`.

This section incorporates and is subordinate to the later approved Phase 4 rules in `paste-order-import.md` and `docs/decisions/hiboutik-paste-simplification.md`.

## 5. Lifecycle features explicitly not required in V1

Unless a later approved specification amendment reintroduces them, V1 does not include:

- a manually selected `CB` / `Espèce` / `DIV` / `Mixte` payment-method field;
- payment-state-based editing locks;
- supplementary-order chains for already-paid order edits;
- mandatory cancel-and-replace behavior for ordinary modifications;
- refund-pending/refund-completed states;
- POS-managed card-refund execution;
- complex replacement-order financial linking;
- an operator-facing payment-event ledger;
- an operator-visible order revision-history feature;
- blocking daily received-payment summaries because one or more orders remain Open;
- a second mandatory visible calculated total alongside the editable authoritative total;
- persistence of a manual total override after later price-affecting order changes;
- a separate Hiboutik emergency-order lifecycle, discrepancy model or reconciliation workflow.

## 6. Approval

This document is the **Approved — Phase 2 baseline**, including the approved Phase 3 advance-order-marker refinement and the Phase 5 alignment to the later approved Phase 4 Hiboutik paste-import simplification.

Implementation must preserve these lifecycle semantics and must not reintroduce the older emergency-order, payment/replacement or revision-history models without explicit product approval.