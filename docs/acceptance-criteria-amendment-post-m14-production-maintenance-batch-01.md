# Acceptance criteria amendment — Post-M14 Production Maintenance Batch 01

**Status:** Approved
**Date:** 2026-09-29
**Applies to:** current frozen V1 behavior plus the approved post-M14 maintenance decision

This amendment adds narrow regression/operability criteria. All unchanged V1 criteria remain authoritative.

## PM01-ORD-001 — One add gesture, one cart addition

Given a writable Caisse order-entry screen and an active selected product, one completed operator add gesture produces exactly one new cart addition.

This must hold for the supported ordinary add paths, including the Add button and product-grid double-click path.

Adding product A and then product B must not create a second unintended A line. Fast sequential deliberate additions must preserve exactly one addition per gesture.

A repair must not satisfy this criterion by coalescing legitimate separate adds of the same product or by changing pricing/quantity semantics.

**Evidence:** real WPF event-path regression tests plus focused application/view-model tests as needed.

## PM01-ORD-002 — Newly inserted cart line is visible

After a successful new cart-line insertion, the cart view scrolls enough to reveal the inserted line.

Editing quantity, removing a line, editing an existing line or repricing alone does not force an unrelated jump to the bottom.

**Evidence:** STA/WPF rendering/event test at a viewport small enough to require vertical scrolling, plus owner PREPROD confirmation.

## PM01-CAT-001 — Natural product-code ordering

Current product lists presented for Catalogue/Caisse operations order codes using deterministic case-insensitive natural alphanumeric comparison.

Required examples include:

- `R1 < R2 < R9 < R10 < R11`;
- `ML1 < ML2 < ML9 < ML10`;
- `R4 < R4a < R4b < R4c < R5`.

Filtering/searching must not revert the same list to lexical `R1, R10, R2` ordering.

No product code or identity is rewritten.

**Evidence:** deterministic comparator/query tests and WPF/list-order regression tests.

## PM01-PRINT-001 — Applied Retrait discount appears on customer ticket

If a committed order has `PickupDiscountApplied = true`, customer output contains one visible negative discount line representing the actual applied Retrait discount amount.

The value is derived only from committed sale-time order/item/adjustment snapshots under the approved pricing semantics. Current Catalogue prices and current business-setting discount values are not consulted for historical printing.

The line appears on initial unpaid customer output, settled customer output and explicit customer reprint. It does not appear when no discount was applied.

Existing authoritative TTC total, persisted VAT breakdown, payment display rules and kitchen output remain unchanged.

**Evidence:** deterministic print-model tests for unpaid/settled/reprint/no-discount cases plus owner physical/safe PREPROD print review.

## PM01-REG-001 — Existing safety invariants

The batch must preserve all applicable existing acceptance criteria, including:

- exact pricing/rounding and Retrait-discount business rules;
- durable commit-before-print;
- print failure not rolling back orders;
- current authority/read-only enforcement;
- catalogue identity/history independence;
- Hiboutik paste-order behavior;
- Production/PREPROD isolation and marking.

## PM01-MANUAL-001 — Owner acceptance scope

For this batch, owner acceptance is computer A PREPROD only.

Minimum owner sequence:

1. add a sequence of products and confirm no unintended duplicate line occurs;
2. add enough lines to overflow the cart and confirm each new bottom line becomes visible;
3. inspect a code family containing 1/2/.../10+ and confirm natural order;
4. create/identify a Retrait order with an actually applied discount and inspect the unpaid customer ticket;
5. settle that same test order and explicitly reprint the customer ticket, confirming the same discount information remains correct;
6. confirm PREPROD marking remains visible on tested output.

No computer-B PREPROD installation is required unless the implementation materially touches a deferred M14 multi-device area.
