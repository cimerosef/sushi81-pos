# Business rules

**Status:** Approved — Phase 2 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Freeze the Sushi 81 commercial, pricing, fulfilment, VAT and monetary rules that V1 must implement consistently.

## 1. Scope

This document defines target V1 business rules affecting:

- `Retrait` / `Livraison` validation;
- pickup discount and minimums;
- delivery minimum and optional fixed delivery fee;
- required/optional order information;
- product-option price adjustments;
- manual order-total override;
- VAT treatment associated with those rules;
- monetary precision/rounding;
- normal business configuration.

Order status/payment lifecycle belongs to `order-lifecycle.md`. Catalogue structure belongs to `catalogue-management.md`. Logical persistence belongs to `data-model.md`.

The rules below are approved target behavior. Former Excel/VBA warnings, overrides or mandatory-field behavior described in `current-system.md` do not override this V1 baseline.

## 2. Fulfilment mode and order information

### 2.1 Mandatory fulfilment mode

Every new order must explicitly select exactly one fulfilment mode:

- `Retrait`; or
- `Livraison`.

If neither is selected, confirmation is rejected and the order remains in progress.

When starting a new order from reusable information from an earlier order, fulfilment mode is **not inherited**. It must be chosen again.

### 2.2 Telephone

Telephone is optional for both `Retrait` and `Livraison`.

A missing telephone number must never by itself block confirmation.

For normal fast entry, a standard French ten-digit number may be typed continuously, for example:

`0612345678`

After acceptance/save, normal display and printing should group it for readability as:

`06 12 34 56 78`

This formatting is a presentation/normalization convenience, not a reason to introduce burdensome mandatory validation.

### 2.3 Delivery address

Delivery address is optional at the time a `Livraison` order is first confirmed.

A missing address must not prevent the order from being created, saved or printed.

The operator may reopen the same order later and add/correct the address. The latest saved address is used for later viewing/reprinting.

### 2.4 Operational comment

A flexible free-text order comment remains available for preparation instructions, references and other operational information not represented by structured fields.

## 3. Retrait discount

For `Retrait`, the operator may choose whether to apply the normal pickup discount.

The approved rule is:

- default discount rate: **10%**;
- only catalogue products marked discount-eligible receive the reduction;
- non-eligible products remain at normal price;
- positive option surcharges do not receive the pickup discount;
- negative option adjustments reduce the discountable product amount before discount calculation;
- after discount, the resulting authoritative calculated order total must be at least the configured minimum discounted-order amount;
- default minimum after discount: **€15.00**;
- if applying the discount would produce a total below that minimum, the discount is not applied;
- there is no separate force-apply/override action for the normal discount rule.

Examples with default 10% / €15 minimum:

- €20.00 normal total -> €18.00 after discount: allowed;
- €16.00 normal total -> €14.40 after discount: discount rejected.

Exceptional commercial amounts do not require weakening this normal rule. The operator may use the separately approved editable authoritative order-total field after normal calculation.

The discount rate and post-discount minimum are user-editable business settings.

## 4. Livraison

### 4.1 Normal delivery minimum

`Livraison` does not receive the normal `Retrait` discount.

The default minimum merchandise/commercial amount required for delivery is **€30.00**.

The rule is:

- the minimum is checked before any delivery fee is added;
- if the merchandise/commercial amount is below the configured minimum, the order cannot be confirmed as `Livraison`;
- a delivery fee must never allow an otherwise-under-minimum order to qualify;
- there is no ordinary force-override for bypassing this minimum;
- the minimum is a user-editable setting.

Examples with €30 minimum and hypothetical €5 fee:

- €28 merchandise + €5 fee -> rejected;
- €30 merchandise + €5 fee -> allowed.

### 4.2 Fixed delivery-fee extension point

V1 normal operation keeps delivery free by default while preserving a simple configurable fixed-fee mechanism.

Business settings:

- `delivery fee enabled` — default **off**;
- `fixed delivery fee amount` — default **€0.00**.

When disabled, the fee contributes €0.

When enabled, the configured fee is automatically added at order level **after** the delivery-minimum check.

The applied fee becomes part of the authoritative order total and therefore participates normally in:

- printing;
- close/reconciliation arithmetic;
- operational turnover;
- downstream export.

Changing a price-affecting order input recalculates the merchandise/commercial result and reapplies the current fee rule before writing the calculated order total.

V1 does not implement speculative zone-based/free-above-threshold/other advanced delivery-fee formulas. The internal architecture should keep fee calculation separable so such rules could be added later without redesigning the order model.

### 4.3 Delivery-fee VAT

Whenever an enabled non-zero Sushi 81 delivery fee is present under normal calculated pricing:

- VAT rate is fixed at **10%**;
- the operator does not choose/override its VAT rate during order entry;
- the fee contributes to the 10% bucket of the normal order tax snapshot;
- no delivery-fee VAT-rate business setting exists in V1.

This is the approved Sushi 81 workflow rule, not a general statement about every transport/delivery charge in France.

A later manual order-total override replaces the normal mixed tax snapshot with the manual-total rule in section 6.

This rule is also recorded in `docs/decisions/delivery-fee-vat.md`.

## 5. Product-option price adjustments

Product options may carry preset or custom price adjustments attached to a specific order line.

### 5.1 Allowed amounts and labels

Adjustments may be:

- positive;
- negative;
- exactly €0.00.

Custom operator-entered adjustments:

- use €0.01 business precision;
- have no additional V1 business minimum/maximum cap;
- require a non-empty text label/description;
- change only the current order line;
- never change the catalogue product's base unit price.

The label is retained in the order-line snapshot and shown on customer-facing output where the adjustment is displayed.

### 5.2 Discount interaction

For a discount-eligible product in a discounted `Retrait` order:

- a **positive** option adjustment is not discounted;
- a **negative** option adjustment reduces the discountable product amount before discount calculation.

Example with a €10 eligible product and 10% discount:

- product €10 + positive option €2 -> `€10 × 90% + €2 = €11.00`;
- product €10 + negative option €2 -> `(€10 - €2) × 90% = €7.20`.

This asymmetry is intentional.

### 5.3 VAT treatment

The operator does not choose option-adjustment VAT during order entry.

Automatic rule:

- every **positive** option adjustment uses **5.5% VAT**;
- every **negative** option adjustment inherits the VAT rate of the associated product/order line;
- €0.00 adjustments have no monetary tax effect.

Sale-time adjustment amount/VAT is persisted in the historical order snapshot.

## 6. Editable authoritative order total

The order-entry interface has **one authoritative order-total field**.

Normal pricing calculates the total from product lines, quantities, options/adjustments, discount and applicable delivery fee and writes the result into that field.

The operator may then directly edit the same field when a different exceptional real-world amount is needed.

A manual edit:

- immediately becomes the authoritative order total;
- does not change catalogue product base prices;
- does not require the lines to mathematically equal the manual total while the override is active;
- does not require a mandatory reason/refund/surcharge workflow;
- controls printing, close/reconciliation, operational turnover and export until replaced.

The manual value is a **temporary override**, not a lock.

Any later price-affecting change — including product, quantity, options/adjustments, discount application or applicable delivery fee — automatically recalculates the ordinary total and replaces the prior manual override.

If the operator still wants an exceptional amount afterwards, the total may be edited again.

A second mandatory visible “calculated total” field is not required.

### 6.1 VAT while manual total is authoritative

When the authoritative final total is a manual override:

- the ordinary mixed product/option/fee VAT allocation is not the final tax snapshot;
- the **entire final TTC amount is represented in one 10% VAT bucket**;
- `taxable_ttc_amount` for that bucket equals the authoritative final total;
- included VAT is calculated at 10% using the approved round-half-up cent rule;
- the rule applies whether the manual total is above or below the calculated total.

When a later price-affecting change replaces the manual override, normal product/option/delivery-fee VAT calculation resumes. Another manual edit reactivates the single 10% bucket rule.

## 7. Monetary precision and rounding

All operator-facing and persisted business amounts use **€0.01 precision** unless another explicit specification states otherwise.

Final monetary rounding uses ordinary **round-half-up** to the nearest cent.

Example:

`€13.635 -> €13.64`

The same deterministic rule applies across:

- percentage discounts;
- VAT amounts;
- product/option monetary results when rounding is required;
- authoritative order totals;
- CB/Espèce amounts;
- turnover/received-payment summaries;
- print output;
- export output.

Implementation may use additional internal precision for intermediate calculations, but all modules must share one deterministic sequence so screen/close/print/export results cannot disagree because different rounding conventions were used.

Banker's rounding and module-specific business rounding are not allowed.

## 8. V1 business configuration

The following normal business parameters are persisted and editable through the application settings UI without recompilation/reinstallation:

| Setting | Default |
|---|---:|
| Retrait discount rate | 10% |
| Minimum total after Retrait discount | €15.00 |
| Livraison merchandise/commercial minimum | €30.00 |
| Delivery fee enabled | false |
| Fixed delivery fee amount | €0.00 |

Changing a setting changes the parameter used by the same approved rule; it does not change the meaning of the rule.

Delivery-fee VAT is not a configurable setting in V1; it remains fixed at 10%.

## 9. Frozen V1 business invariants

Implementation must not reintroduce former or speculative behavior that conflicts with these rules, including:

- force-applying the normal Retrait discount below its configured post-discount minimum;
- allowing a delivery fee to satisfy an under-minimum Livraison order;
- applying the normal Retrait discount to positive option surcharges;
- assigning option-adjustment VAT contrary to section 5;
- making telephone mandatory;
- making delivery address mandatory at initial Livraison confirmation;
- requiring a second visible calculated-total field;
- permanently locking a manual total against later price-affecting recalculation;
- allocating an active manual total across the original mixed product VAT rates;
- using banker's/module-specific monetary rounding;
- hard-coding normal configurable business values in source code.

## 10. Approval

This file is the **Approved — Phase 2 baseline**, including the approved Phase 3 VAT amendments and the Phase 5 consistency cleanup.

There are no remaining unresolved V1 commercial/business-rule decisions in this document.

Implementation may choose presentation/layout details only where they preserve every rule above and the acceptance criteria in `acceptance-criteria.md`.