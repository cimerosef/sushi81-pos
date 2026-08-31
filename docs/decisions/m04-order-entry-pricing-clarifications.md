# M04 order-entry pricing and interaction clarifications

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-08-31  
**Applies to:** M04 order-entry quantity/option pricing, ordinary Retrait discount interaction and deterministic discount rounding

## Context

During the M04 readiness audit, the frozen V1 baselines fully defined Product/Option pricing, Retrait/Livraison rules, VAT treatment, manual authoritative-total behavior and round-half-up cent precision, but left three material implementation details unstated. Because each detail changes price or operator interaction, implementation must not guess.

The project owner explicitly approved the following rules for M04 and V1.

## A1 — Quantity multiplies option and custom adjustments

Configured Product-option adjustments and operator-entered custom adjustments are **per ordered unit**.

For a cart line with quantity `Q`:

- Product base amount is multiplied by `Q`;
- every selected predefined option adjustment is multiplied by `Q`;
- every custom adjustment is multiplied by `Q`.

Example:

- Product base price: €10.00
- selected option adjustment: +€1.00
- quantity: 2

Result before any discount:

`2 × (€10.00 + €1.00) = €22.00`

One cart line therefore represents multiple units with the same option/custom-adjustment configuration. If otherwise-identical Product units require different configurations, they remain separate cart lines.

For historical persistence, `OrderItemAdjustmentSnapshot.adjustment_ttc_snapshot` is the sale-time **per-unit** signed adjustment. The extended adjustment component is derived by multiplying by `OrderItem.quantity`. Quantity greater than one does not require duplicated per-unit adjustment snapshot rows.

## B1 — Ordinary Retrait discount defaults OFF

Selecting `Retrait` does not silently apply the ordinary pickup discount.

For a new Retrait order:

- the ordinary Retrait discount begins OFF / not requested;
- the operator explicitly enables it when applicable;
- the already-approved eligibility, configured rate, post-discount minimum and no-force-override rules then determine whether the discount is actually applied.

A fulfilment mode that does not support the ordinary Retrait discount must not leave the discount financially active.

The UI must make the current discount state clear.

This decision does not change the configured default discount **rate** of 10%; it only defines the new-order interaction default for whether the discount is requested.

## C1 — Line/component-first deterministic discount rounding

Ordinary Retrait discount uses one deterministic **line/component-first** calculation sequence.

Do not first calculate one order-level discount and then invent an arbitrary cent-allocation algorithm across lines.

For each affected line:

1. calculate the extended Product base from integer-cent unit base × quantity;
2. calculate each extended adjustment from integer-cent per-unit adjustment × quantity;
3. for a discount-eligible line, combine the Product base component with negative adjustment components that share the Product VAT treatment to form that line's discountable Product-VAT component;
4. apply the configured Retrait discount rate to that component;
5. round that discounted component to cents using the shared deterministic round-half-up rule;
6. positive adjustments remain outside the discount and retain their own 5.5% VAT treatment;
7. calculate the resulting line total;
8. sum the final line/components for the order.

The same sequence is authoritative for order-entry display, persisted pricing snapshots and later consumers. Tests must include at least one multi-line boundary where line/component-first rounding differs from aggregate-order rounding by one cent, proving this rule is actually enforced.

## Relationship to existing frozen rules

These clarifications do not change the already-approved rules that:

- normal Retrait discount applies only to discount-eligible Products;
- positive option surcharges are not discounted;
- negative option adjustments reduce the discountable Product amount before discount;
- the ordinary discount is rejected if its resulting final calculated order total would be below the configured post-discount minimum;
- Livraison does not receive the ordinary Retrait discount;
- positive adjustments use 5.5% VAT and negative adjustments inherit Product VAT;
- all final business monetary results use deterministic round-half-up cent precision;
- a manual authoritative-total override supersedes normal mixed VAT with one 10% VAT bucket while active.

## Implementation consequence

M04 must implement these three rules in the shared pricing/cart model rather than in WPF-only logic. Later printing, export and other pricing consumers must consume the persisted/shared result and must not independently reinterpret quantity, discount-default or rounding semantics.
