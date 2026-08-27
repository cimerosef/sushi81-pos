# Delivery-fee VAT rule

**Status:** Approved — Phase 3 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS delivery-fee calculation and tax snapshot

## Decision

When the Sushi81 POS fixed delivery-fee feature is enabled and a non-zero delivery fee is added to a `Livraison` order:

- the delivery fee uses a fixed **10% VAT rate**;
- the operator does not select or override the VAT rate for the delivery fee during order entry;
- the delivery fee is represented as its own 10% component in the normal order VAT/tax breakdown;
- the delivery fee is added only after the configured minimum-delivery merchandise check, as defined in `business-rules.md`;
- the delivery fee remains part of the authoritative order total and therefore participates in printing, closing/reconciliation, turnover and downstream export;
- if the operator subsequently applies a manual order-total override, the approved manual-total rule supersedes the normal mixed tax breakdown and the entire final authoritative TTC amount is represented in a single 10% VAT bucket.

## Legal/operational scope

This is the approved Sushi81 POS tax rule for Sushi 81's restaurant-delivery workflow.

It must not be generalized as a claim that every delivery or transport charge in France is always taxed at 10%. VAT treatment of transport/accessory charges can depend on the underlying transaction; this project rule is deliberately scoped to Sushi 81's approved workflow.

## Data-model consequence

No configurable delivery-fee VAT-rate field is needed in `BusinessSettings` for V1.

When non-zero under normal calculated pricing:

- `Order.delivery_fee_ttc_snapshot` uses 10% VAT;
- `OrderTaxBreakdown` includes the fee in the 10% bucket;
- no additional entity is required.

## Phase 5 incorporation

This decision is incorporated into the frozen V1 baselines in:

- `docs/business-rules.md`;
- `docs/data-model.md`;
- `docs/acceptance-criteria.md`.

It no longer represents an open Phase 3 question or pending documentation action.
