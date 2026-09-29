# Post-M14 decision — Production Maintenance Batch 01

**Status:** Approved
**Date:** 2026-09-29
**Decision owner:** Sushi81 POS owner
**Applies to:** Post-M14 Production Maintenance Batch 01

## Context

M14 PreProd Foundation is merged into `main` at `71024888a9cfd08109496ad076002be96e708c1a`. PREPROD is currently operated only on computer A; computer B remains Production-only. The real two-PC PREPROD pairing/handoff/acquisition verification remains deferred under the M14 owner waiver and is not Passed. M12 populated real annual-archive verification remains separately deferred.

The owner reported four current Production maintenance findings after M14:

1. a customer ticket for an order with an applied Retrait discount does not show the discount;
2. after a new cart line is added below the visible cart viewport, the cart does not automatically reveal the new line;
3. product codes are displayed in lexical order such as `R1, R10, ... R2` rather than operator-friendly natural alphanumeric order;
4. while adding products to an order, a prior product can intermittently be added a second time when a later product is added.

The owner approved treating all four initial findings as one maintenance batch, with the duplicate-add defect first because it can create an incorrect order and total.

During execution of the batch, the owner added one further printing optimization: the kitchen ticket must reserve approximately three handwriting-line heights of blank paper after the final `TOTAL`, so staff can add handwritten information after printing. The owner explicitly requested this behavior for inclusion in the same batch.

## Decision

### 1. Batch and validation model

All four initial findings plus the later owner-approved kitchen handwriting-space optimization belong to one Production Maintenance Batch 01 and one owner-tested PREPROD candidate.

Implementation is split into ordered work packages:

- WP1 — diagnose and repair intermittent duplicate product addition;
- WP2 — reveal the newly added cart line automatically;
- WP3 — natural alphanumeric product-code ordering;
- WP4 — printing improvements: show applied Retrait discount on customer tickets and reserve kitchen handwriting space after `TOTAL`;
- WP5 — integrated regression, documentation reconciliation, immutable PREPROD candidate and owner acceptance.

No work package auto-authorizes a Production deployment or PR merge.

### 2. Duplicate-add defect

One deliberate operator add gesture must produce exactly one cart addition.

The repair must address the actual event/state/reentrancy cause. It must not merely hide the symptom by merging equal products, discarding legitimate repeated operator adds, or introducing an arbitrary timing delay.

Separate deliberate add gestures remain separate valid actions under existing cart semantics.

The repair must preserve:

- current product/option selection behavior;
- quantity controls;
- line editing/removal;
- Hiboutik paste-order behavior;
- pricing and discount semantics;
- authority/write gating.

### 3. Cart auto-reveal

After a successful new cart-line insertion, the newly inserted line must be brought into the visible cart viewport.

The behavior applies to a true new cart-line addition. Quantity changes, edits of an existing line, repricing and unrelated refreshes must not cause gratuitous scroll jumps.

No order or pricing data semantics change.

### 4. Natural product-code order

Operator-facing active product lists use case-insensitive natural alphanumeric ordering so numeric runs compare numerically.

Examples:

- `R1, R2, ... R9, R10, R11`;
- `ML1, ML2, ... ML9, ML10`;
- `R4, R4a, R4b, R4c, R5`.

Ordering must remain deterministic for ties and must not modify product codes, internal IDs, categories, historical order snapshots or import/export identity.

The same current-catalogue query behavior used by Catalogue/Caisse must not present conflicting code orderings.

### 5. Printing improvements

#### 5.1 Customer-ticket Retrait discount line

When `PickupDiscountApplied` is true, the customer ticket displays the actual applied discount amount as a negative customer-facing line, using the existing French ticket style, for example:

`Remise : -2,63 EUR`

The amount must be derived from committed sale-time order/item/adjustment values and existing pricing semantics. Printing must not query the current Catalogue or current business settings to reconstruct historical discount values.

The discount line is shown consistently on:

- the initial unpaid customer ticket;
- the paid customer ticket when payment is already settled;
- explicit customer reprints / `DUPLICATA`.

If no Retrait discount was actually applied, no discount line is printed.

This change does not alter discount eligibility, rate, minimum, rounding, VAT, authoritative total or payment state.

#### 5.2 Kitchen-ticket handwriting space

Every kitchen ticket must reserve a blank handwriting area immediately after the final kitchen `TOTAL`. The target is approximately three normal kitchen handwriting-line heights of vertical paper space.

The reserved space applies consistently to ordinary kitchen printing, kitchen `RÉIMPRESSION`, PREPROD-marked kitchen output and cancelled-order kitchen output. It contains no pseudo-data and does not change the business content of the ticket.

The customer ticket receives no corresponding blank handwriting area.

The implementation must create real rendered vertical space that survives the Windows thermal-print layout/pagination path; simply appending empty strings that are trimmed/collapsed by the renderer is not sufficient.

### 6. Acceptance environment

These changes are ordinary business/UI/print/catalogue-maintenance changes and do not materially alter pairing, authority handoff, target acquisition, Disaster Recovery or OneDrive/GitHub cross-device coordination.

Accordingly, owner manual acceptance is performed on computer A PREPROD only.

A real two-PC PREPROD test is not required for this batch unless implementation unexpectedly changes one of those deferred M14 multi-device areas. If that happens, implementation stops and the controller expands the acceptance scope before accepting the change.

## Explicit non-goals

This batch does not:

- change payment or close semantics;
- change Retrait-discount business rules;
- merge identical cart products automatically;
- redesign product codes;
- change catalogue identity/import/export semantics;
- change authority/recovery/handoff/DR behavior;
- resolve M12 deferred populated real archive verification;
- satisfy or relabel the M14 deferred two-PC PREPROD verification;
- authorize Production deployment or PR merge.
