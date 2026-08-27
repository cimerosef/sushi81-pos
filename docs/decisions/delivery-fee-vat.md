# Delivery-fee VAT rule

**Status:** Approved — Phase 3 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS delivery-fee calculation and tax snapshot

## Decision

When the Sushi81 POS fixed delivery-fee feature is enabled and a non-zero delivery fee is added to a `Livraison` order:

- the delivery fee uses a fixed **10% VAT rate**;
- the operator does not select or override the VAT rate for the delivery fee during order entry;
- the delivery fee is represented as its own 10% component in the normal order VAT/tax breakdown;
- the delivery fee is added only after the configured minimum-delivery merchandise check, as already defined in `business-rules.md`;
- the delivery fee remains part of the authoritative order total and therefore participates in printing, closing/reconciliation, turnover and downstream export;
- if the operator subsequently applies a manual order-total override, the already-approved manual-total rule supersedes the normal mixed tax breakdown and the entire final authoritative TTC amount is represented in a single 10% VAT bucket.

## Legal/operational scope

This is the approved Sushi81 POS tax rule for Sushi 81's restaurant-delivery workflow.

It should not be generalized in project documentation as a statement that every delivery or transport charge in France is always taxed at 10%. French VAT treatment of transport/accessory charges can depend on the underlying transaction. The project rule is intentionally scoped to Sushi 81's delivery workflow.

## Consequence for `data-model.md`

The Phase 3 open question titled `Delivery-fee VAT treatment` is resolved:

- no configurable delivery-fee VAT-rate field is needed in `BusinessSettings` for v1;
- `Order.delivery_fee_ttc_snapshot` is taxed at 10% whenever it is non-zero under normal calculated pricing;
- `OrderTaxBreakdown` includes that fee in the 10% bucket during normal calculated pricing;
- the delivery-fee VAT rule does not require a new entity.

This decision must be incorporated into `business-rules.md` and `data-model.md` before the Phase 3 data-model baseline is marked Approved.