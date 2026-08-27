# Advance-order marker semantics

**Status:** Approved — Phase 3 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS future-order / due-today reminder derivation

## Decision

`Order.advance_order_marker` is a persistent boolean marker used only to remember that an order has participated in the advance-order workflow.

The rule is:

- when a non-cancelled order is saved with `planned_fulfilment_date` later than the then-current business date, `advance_order_marker` is set to `true`;
- once set to `true`, it is never reset to `false` during the lifetime of that business order;
- changing the planned fulfilment date to another future date does not reset the marker;
- changing the planned fulfilment date to today or to an earlier date does not reset the marker;
- changing only the planned fulfilment time does not reset the marker;
- modifying products, payment information, address, telephone, comments or other order data does not reset the marker;
- cancellation does not need to clear the marker because cancelled orders are excluded from active reminder views by order status.

A normal same-day order that has never been saved with a future planned fulfilment date keeps `advance_order_marker = false`.

## Derived reminder behavior

The marker is not itself a lifecycle status.

A due-today advance-order reminder is derived when:

- `planned_fulfilment_date = current_business_date`;
- `advance_order_marker = true`;
- `status != CANCELLED`.

This preserves the distinction between:

- an ordinary order created for today; and
- an order created in advance whose planned date has now arrived.

Payment state and `OPEN` / `CLOSED` status do not remove the due-today operational reminder.

## Data-model consequence

`advance_order_marker` is a required persisted field on `Order`.

No separate future-order history table, reminder-state table or date-change audit trail is required in V1 solely to support this behavior.

## Phase 5 incorporation

This decision is incorporated into the frozen V1 baselines in:

- `docs/order-lifecycle.md`;
- `docs/data-model.md`;
- `docs/acceptance-criteria.md`.

It no longer represents an open Phase 3 question or pending documentation action.
