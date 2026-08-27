# Business rules

**Status:** Approved — Phase 2 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Freeze Sushi 81 commercial and operational rules that must be implemented consistently by the application.

## 1. Scope

This document defines target business rules that affect order acceptance, pricing, discounts, fulfilment information and other operator-facing validations.

It does not define the order lifecycle, physical database schema, UI layout, printing templates or export file format.

## 2. Authoritative Phase 1 baseline

The following constraints are already established by `current-system.md` and `product-requirements.md`:

- fulfilment modes are `Retrait` and `Livraison`;
- telephone number and delivery address are useful operational information, but Phase 2 may refine when they are mandatory;
- the application retains a free-text operational comment field;
- planned fulfilment date/time is structured separately from comments;
- product discounts depend on catalogue-level discount eligibility;
- ordinary order entry cannot arbitrarily overwrite a product's catalogue base price;
- product options may carry predefined or operator-entered option-price adjustments;
- normal business configuration expected to change must not require source-code edits.

The Phase 1 restriction on changing a catalogue product's **base unit price** does not prevent an operator from directly editing the **order total**. These are separate concepts.

## 3. Current-system rules that require target confirmation

The current Excel/VBA system behaves as follows:

### 3.1 Pickup

- no delivery fee;
- operator may trigger a 10% discount;
- only products marked discount-eligible receive the reduction;
- current VBA checks the resulting total against a €15 threshold but allows the operator to override the warning.

### 3.2 Delivery

- no delivery fee;
- current minimum original order total is €30;
- current delivery orders are charged at normal price and do not use the pickup 10% discount workflow;
- address is normally operationally expected.

These current behaviors are inputs to Phase 2, not automatically frozen target rules unless explicitly approved below.

## 4. Approved business rules

### 4.1 Retrait discount rule — approved Phase 2 decision

For `Retrait` orders, the operator may choose whether to apply the normal pickup discount.

The rule is:

- the default discount rate is **10%**;
- the discount applies only to catalogue items marked as discount-eligible;
- items not marked as discount-eligible remain at their normal price;
- the application first calculates the order using the normal product prices and then applies the configured discount to eligible items;
- after the discount has been applied, the resulting order total must be at least the configured minimum discounted-order amount;
- the default minimum discounted-order amount is **€15.00**;
- if applying the discount would cause the resulting order total to fall below that minimum, the discount must not be applied;
- this discount rule has no separate force-apply/override action.

Examples using the default 10% discount and €15 minimum:

- €20.00 normal total -> €18.00 after discount: discount allowed;
- €16.00 normal total -> €14.40 after discount: discount rejected;
- an order already below €15.00 cannot receive the normal pickup discount if the discounted result would remain below the configured minimum.

Exceptional commercial situations do not require weakening the discount rule. The operator may instead use the already-approved editable order-total field when a deliberately exceptional final amount is needed.

#### Configurability

The following values are **business configuration**, not hard-coded constants:

- pickup discount rate (default: 10%);
- minimum order total required **after discount** (default: €15.00).

The operator must be able to modify these values through the normal application settings/configuration interface and save the new values without recompiling, reinstalling or editing source code.

For example, if the minimum is later changed from €15 to €20, the application must enforce the same approved rule using €20 as the new post-discount minimum.

### 4.2 Livraison rule — approved Phase 2 decision

For `Livraison` orders, the default business rule is:

- the order does **not** receive the normal `Retrait` discount;
- the default minimum merchandise/order amount required for delivery is **€30.00**;
- if the merchandise/order amount used for the minimum check is below the configured delivery minimum, the order cannot be confirmed as `Livraison`;
- there is no ordinary force-override action for bypassing the delivery minimum;
- the delivery minimum is a user-editable business parameter and must not be hard-coded.

The minimum-delivery check is based on the order's merchandise/commercial amount **before any delivery fee is added**. A delivery fee must never be allowed to make an otherwise-under-minimum order qualify for delivery.

Examples if the delivery minimum is €30 and a future delivery fee is €5:

- €28 merchandise + €5 delivery fee = €33 final order total -> delivery still rejected because the merchandise amount is below €30;
- €30 merchandise + €5 delivery fee = €35 final order total -> delivery allowed.

#### Delivery-fee extensibility

V1 keeps delivery free in normal operation, but the product must preserve a simple future delivery-fee entry point.

The approved design direction is:

- `delivery fee enabled` is a user-editable business setting, default **off**;
- `fixed delivery fee amount` is a user-editable amount, default **€0.00**;
- when delivery fee is disabled, the fee contributes €0 to the order;
- when delivery fee is enabled, the configured fee is automatically added at order level after the delivery-minimum check;
- the resulting amount becomes part of the ordinary authoritative order total and therefore participates in printing, closing/reconciliation, turnover and export in the same way as the rest of the order total;
- changing a price-affecting order input must recalculate the merchandise amount and then reapply the current delivery-fee rule before writing the order total;
- the operator may still manually edit the final order total afterwards under section 4.5.

The implementation should keep delivery-fee calculation logically separate from product-price calculation. V1 only needs the simple fixed-fee rule above, but the architecture must not require a rewrite of the order model if Sushi 81 later introduces conditions such as free delivery above a threshold, different fees by area, or other delivery-fee formulas.

The future presence of this extension point does **not** require those advanced fee rules or their UI to be implemented in v1.

#### Delivery-fee VAT — approved Phase 3 amendment

When a non-zero delivery fee is enabled and applied to a `Livraison` order under normal calculated pricing:

- the delivery fee uses a fixed **10% VAT rate**;
- the operator does not select or override the VAT rate for the delivery fee during order entry;
- the delivery fee contributes to the 10% bucket in the normal order VAT/tax breakdown;
- no configurable delivery-fee VAT-rate setting is required in v1.

This rule is scoped to the Sushi81 POS delivery workflow. It must not be generalized in project documentation as a claim that every transport or delivery charge in France is always taxed at 10%.

If the operator subsequently manually edits the authoritative order total, the manual-total VAT rule in section 4.5 supersedes the normal mixed tax breakdown: the entire final authoritative TTC amount is then represented as one 10% VAT bucket.

### 4.3 Required customer/order information — approved Phase 2 decision

Every new order must explicitly select exactly one fulfilment mode:

- `Retrait`; or
- `Livraison`.

Fulfilment mode is a **mandatory order field**. If neither option has been selected, the application must reject order confirmation/completion and keep the order in progress.

When a new order is initialized from an existing order's reusable customer information, the prior order's fulfilment mode is **not inherited**. The operator must explicitly choose `Retrait` or `Livraison` again for the new order.

Telephone number is **optional for both `Retrait` and `Livraison`**. A missing telephone number must never by itself prevent order confirmation.

Delivery address is also **optional at the time a `Livraison` order is confirmed**. A missing address must not prevent the order from being created, saved, printed or otherwise completed in the normal order-entry workflow.

The operator must be able to reopen the same existing `Livraison` order later and add or correct the delivery address without creating a replacement order. The latest saved address then becomes the address used for subsequent viewing and reprinting.

The normal fast-entry workflow should allow the operator to type a standard French 10-digit telephone number as ten continuous digits without inserting spaces manually, for example:

`0612345678`

After the number is accepted/saved, normal order display and printing should format that 10-digit number for readability as:

`06 12 34 56 78`

This formatting is a presentation/normalization convenience, not a reason to make telephone mandatory or to introduce burdensome phone-number validation. The UI should favor fast entry and readable saved/displayed output.

### 4.4 Product-option price adjustments — approved Phase 2 decision

Product options may carry either preset or operator-entered price adjustments associated with a specific order line.

Approved rules:

- option-price adjustments may be **positive or negative**;
- custom adjustment amounts are entered/stored to **€0.01 precision**;
- no business maximum or minimum adjustment amount is required;
- a custom operator-entered adjustment must have a **non-empty text label/description**;
- that description is the commercial name of the adjustment and must be retained with the order line and shown on the customer-facing receipt/printout where the adjustment is displayed;
- an adjustment changes only that order line; it does not change the catalogue product's base price;
- changing the order line, its options or its adjustments is a price-affecting change and therefore triggers normal order-total recalculation under section 4.5.

#### Discount interaction — approved Phase 2 decision

For a discount-eligible product in a discounted `Retrait` order:

- a **positive option adjustment does not receive the pickup discount**;
- a **negative option adjustment reduces the discountable product amount before the discount is calculated**.

With a 10% discount on a €10 discount-eligible product:

- product €10 + positive option €2 -> `€10 × 90% + €2 = €11.00`;
- product €10 + negative option €2 -> `(€10 - €2) × 90% = €7.20`.

This asymmetry is intentional: extra-charge options keep their full added price, while negative adjustments reduce the amount that is subject to the product's discount.

#### VAT treatment — approved Phase 2 decision

Option-price adjustments use the following VAT rules automatically; the operator does not choose a VAT rate during order entry:

- every **positive option adjustment** uses a fixed VAT rate of **5.5%**;
- every **negative option adjustment** inherits the VAT rate of the product/order line to which it belongs.

This rule applies to the adjustment amount itself. The underlying product continues to retain its own catalogue/product VAT rate.

### 4.5 Editable order total — approved Phase 2 decision

The order-entry interface has **one authoritative order-total field**, not separate "system total" and "final total" fields.

Normal behavior is:

- the application calculates the order total from product lines, quantities, product-option adjustments and approved pricing/discount rules;
- that calculated amount is written directly into the ordinary order-total field;
- the operator may directly edit that same order-total field when an exceptional real-world situation requires a different amount;
- after a manual edit, the entered amount itself becomes the authoritative total for the order at that moment.

A manual total edit is deliberately a **temporary override of the current calculation**, not a lock on future automatic calculation.

If any price-affecting order input is subsequently changed — including product lines, quantities, product options/price adjustments, discount application or a configured delivery fee — the application must automatically recalculate the order total using the normal approved rules and replace any earlier manually entered total.

After that recalculation, if the operator still wants a different exceptional amount, the operator may simply edit the total again.

The application must not require a second visible amount field merely to preserve the previously calculated or previously overridden value.

Manual editing of the order total:

- does **not** change catalogue product base prices;
- does **not** require the product lines to mathematically add up to the manually entered total at the time of the override;
- does **not** require a mandatory reason, refund, surcharge or correction workflow;
- must not be rejected merely because it differs from what the normal pricing rules calculate;
- controls the order amount used for printing, closing/reconciliation, retained turnover and downstream export until a later price-affecting order change triggers normal recalculation or the operator manually changes the total again.

This capability is intentional. Sushi81 POS is a practical operational/turnover-recording tool, and the operator may encounter exceptional situations not anticipated by the normal pricing rules. The software should allow the operator to record the business amount that has actually been decided while keeping automatic calculation behavior predictable whenever the underlying order changes.

#### VAT after a manual total override — approved Phase 3 amendment

If the operator manually edits the authoritative order total, the VAT treatment of that manually overridden final amount is simplified deliberately:

- the **entire authoritative final TTC amount is treated as subject to 10% VAT**;
- the normal mixed VAT breakdown derived from the individual products/options is no longer used for the final receipt/tax breakdown while that manual override remains authoritative;
- the tax snapshot must therefore contain a single 10% VAT bucket whose TTC base equals the manually entered `Order.total_ttc`;
- the VAT amount is calculated from that TTC total using the approved round-half-up rule;
- this rule applies whether the manual total is higher or lower than the system-calculated product-line total;
- no proportional allocation back across the original product VAT rates is required.

If a later price-affecting order change automatically recalculates the order total and thereby replaces the manual override, the application returns to the normal product/option/delivery-fee VAT calculation rules. If the operator then manually edits the recalculated total again, the single-rate 10% override VAT rule applies again.

### 4.6 Rounding and monetary consistency — approved Phase 2 decision

All operator-facing and persisted monetary amounts use **€0.01 precision** unless another explicit rule states otherwise.

The application uses ordinary decimal **round-half-up** behavior to the nearest cent for final monetary results. For example, `€13.635` becomes `€13.64`.

This same cent-precision rule applies consistently to:

- percentage-discount results;
- VAT amounts shown or persisted by the application;
- product/option monetary results where rounding is required;
- authoritative order totals;
- CB and Espèce amounts;
- turnover and received-payment summaries;
- printed/exported monetary values.

The implementation may retain additional internal precision during intermediate calculations when useful, but it must use one deterministic calculation/rounding sequence so that the values shown on screen, printed on receipts, used for closing validation, included in summaries and exported downstream never disagree because different modules rounded the same business amount differently.

Banker's rounding or module-specific rounding conventions must not be introduced.

## 5. Configuration principle — approved Phase 2 principle

Values that Sushi 81 may reasonably change during normal operation must be represented as user-editable business configuration rather than hard-coded constants when practical.

The operator must be able to change such values through the application's normal configuration/settings UI without recompiling the software.

The v1 business-configuration scope includes:

- pickup discount percentage (default 10%);
- post-discount minimum `Retrait` order amount (default €15.00);
- minimum `Livraison` merchandise/order amount (default €30.00);
- delivery-fee enabled/disabled setting (default disabled);
- fixed delivery-fee amount (default €0.00).

The delivery-fee VAT rate is not a configurable business setting in v1; it is fixed at 10% by the approved Sushi81 delivery-fee rule.

No additional commercial configuration values are required for the Phase 2 baseline. New parameters may be added later without changing the approved semantics above.

Configuration must not weaken the rule model: changing a value changes the parameter used by the approved rule, not the underlying meaning of the rule itself.

## 6. Phase 2 business rules frozen in this document

The following are approved and are no longer open questions:

- Retrait orders may use the configurable pickup discount;
- the default pickup discount is 10%;
- only discount-eligible catalogue products receive that discount;
- the discounted order total must reach the configured post-discount minimum, default €15;
- there is no force-apply override for the normal discount rule;
- the discount rate and post-discount minimum are editable by the operator without recompilation;
- Livraison does not use the normal Retrait discount;
- the default Livraison minimum is €30 and is user-configurable;
- the Livraison minimum is checked before any delivery fee is added and cannot be satisfied by the delivery fee itself;
- below-minimum Livraison orders cannot be confirmed through an ordinary override;
- delivery fee is currently free/default €0 but an application-level enable/amount configuration entry point is preserved;
- delivery-fee calculation is kept logically extensible so future fee rules do not require rebuilding the order model;
- whenever a non-zero delivery fee is enabled under normal calculated pricing, that fee uses fixed 10% VAT and contributes to the 10% tax bucket;
- every new order must explicitly select `Retrait` or `Livraison` before confirmation;
- fulfilment mode is not inherited when creating a new order from an existing order's customer information;
- telephone is optional for both Retrait and Livraison;
- delivery address is optional at initial Livraison confirmation and may be added or corrected later on the same order;
- standard 10-digit telephone entry may be typed without spaces and is displayed/printed in grouped form such as `06 12 34 56 78` after save;
- custom option adjustments may be positive or negative, have no amount cap, require a text description and use €0.01 precision;
- positive option adjustments do not receive pickup discount, while negative adjustments reduce the discountable product amount before discount calculation;
- positive option adjustments use fixed 5.5% VAT, while negative adjustments inherit the associated product VAT rate;
- all final monetary results use €0.01 precision with ordinary round-half-up behavior;
- the ordinary order-total field is directly editable;
- while a manual order-total override is authoritative, the entire final TTC amount uses a single 10% VAT bucket for receipt/tax purposes;
- later price-affecting order changes automatically recalculate and replace any manual total override and restore normal product/option/delivery-fee VAT calculation until another manual total edit occurs.

## 7. Approval rule

This file is the approved Phase 2 business-rules baseline, including the approved Phase 3 amendments defining VAT treatment for a manual order-total override and for the Sushi81 delivery fee. Codex must implement these semantics and must not restore former VBA warning/override behavior, mandatory telephone/address checks, hard-code configurable commercial thresholds, apply pickup discount to positive option surcharges, assign option-adjustment or delivery-fee VAT contrary to the rules above, allocate a manually overridden order total across the original mixed VAT rates, use inconsistent monetary rounding, or introduce additional commercial constraints without explicit product approval.