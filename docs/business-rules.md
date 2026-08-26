# Business rules

**Status:** Draft — Phase 2 first batch  
**Last updated:** 2026-08-26  
**Product:** Sushi81 POS  
**Purpose:** Freeze Sushi 81 commercial and operational rules that must be implemented consistently by the application.

## 1. Scope

This document defines target business rules that affect order acceptance, pricing, discounts, fulfilment information and other operator-facing validations.

It does not define the order lifecycle, physical database schema, UI layout, printing templates or export file format.

## 2. Authoritative Phase 1 baseline

The following constraints are already established by `current-system.md` and `product-requirements.md`:

- fulfilment modes are `Retrait` and `Livraison`;
- delivery address is required for delivery;
- telephone number is useful but is not universally mandatory;
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
- address is mandatory.

These current behaviors are inputs to Phase 2, not automatically frozen target rules.

## 4. Rule groups to freeze

### 4.1 Discount rule

Phase 2 must define:

- when the 10% discount is available;
- whether it applies only to `Retrait`;
- how eligible and ineligible items combine in one order;
- exact €15 threshold semantics;
- whether the threshold is calculated before or after discount;
- whether an order that would fall below the threshold is blocked from receiving the discount rather than merely warned;
- whether any operator override remains allowed.

### 4.2 Delivery rule

Phase 2 must define:

- exact minimum delivery order value;
- whether the minimum is based on original or adjusted total;
- whether delivery remains free;
- whether discounts are disallowed for delivery;
- whether any exceptional override is permitted.

### 4.3 Required customer/order information — partially approved Phase 2 decision

Every new order must explicitly select exactly one fulfilment mode:

- `Retrait`; or
- `Livraison`.

Fulfilment mode is a **mandatory order field**. If neither option has been selected, the application must reject order confirmation/completion and keep the order in progress.

When a new order is initialized from an existing order's reusable customer information, the prior order's fulfilment mode is **not inherited**. The operator must explicitly choose `Retrait` or `Livraison` again for the new order.

Delivery address remains mandatory for `Livraison`.

Phase 2 must still define when telephone is:

- optional;
- strongly recommended but not required;
- required, if any case exists.

### 4.4 Product-option price adjustments

Phase 2 must define:

- whether custom option adjustments may be zero, positive and/or negative;
- allowed decimal precision;
- whether a maximum/minimum adjustment is needed;
- whether the operator must choose an option label before entering a custom adjustment;
- whether a custom adjustment requires a comment/reason;
- how adjustments interact with discount eligibility and VAT.

### 4.5 Editable order total — approved Phase 2 decision

The order-entry interface has **one authoritative order-total field**, not separate "system total" and "final total" fields.

Normal behavior is:

- the application calculates the order total from product lines, quantities, product-option adjustments and approved pricing/discount rules;
- that calculated amount is written directly into the ordinary order-total field;
- the operator may directly edit that same order-total field when an exceptional real-world situation requires a different amount;
- after a manual edit, the entered amount itself becomes the authoritative total for the order at that moment.

A manual total edit is deliberately a **temporary override of the current calculation**, not a lock on future automatic calculation.

If any price-affecting order input is subsequently changed — including product lines, quantities, product options/price adjustments or discount application — the application must automatically recalculate the order total using the normal approved pricing rules and replace any earlier manually entered total.

After that recalculation, if the operator still wants a different exceptional amount, the operator may simply edit the total again.

The application must not require a second visible amount field merely to preserve the previously calculated or previously overridden value.

Manual editing of the order total:

- does **not** change catalogue product base prices;
- does **not** require the product lines to mathematically add up to the manually entered total at the time of the override;
- does **not** require a mandatory reason, refund, surcharge or correction workflow;
- must not be rejected merely because it differs from what the normal pricing rules calculate;
- controls the order amount used for printing, closing/reconciliation, retained turnover and downstream export until a later price-affecting order change triggers normal recalculation or the operator manually changes the total again.

This capability is intentional. Sushi81 POS is a practical operational/turnover-recording tool, and the operator may encounter exceptional situations not anticipated by the normal pricing rules. The software should allow the operator to record the business amount that has actually been decided while keeping automatic calculation behavior predictable whenever the underlying order changes.

### 4.6 Rounding and monetary consistency

All target pricing rules must define deterministic euro-cent rounding so that cart totals, receipts, payment totals, exports and reconciliation agree.

The exact rounding point for percentage discounts and VAT presentation remains to be frozen.

The authoritative editable order total is stored to euro-cent precision.

## 5. Configuration principle

Values that Sushi 81 may reasonably change during normal operation should be represented as business configuration rather than hard-coded constants when practical. This includes thresholds and discount rates once their semantics are approved.

Configuration must not weaken the rule model: changing a value should not require changing application source code, but the application should still validate resulting orders consistently.

## 6. Decisions still to freeze

Before this document becomes baseline, Phase 2 must explicitly approve at least:

1. final pickup discount eligibility and threshold behavior;
2. whether the discount can ever be force-applied below the threshold;
3. final delivery minimum and whether any override exists;
4. final delivery discount policy;
5. final telephone-required/optional rules;
6. custom option-price adjustment validation;
7. discount interaction with product options;
8. rounding rules for percentage discounts and order totals;
9. which commercial values are operator-configurable in v1.

The following are already approved and are no longer open questions:

- every new order must explicitly select `Retrait` or `Livraison` before confirmation;
- fulfilment mode is not inherited when creating a new order from an existing order's customer information;
- the ordinary order-total field is directly editable;
- later price-affecting order changes automatically recalculate and replace any manual total override.

## 7. Approval rule

This file remains a Draft until the remaining target rules above are explicitly approved. Codex must not use current VBA warning/override behavior as the default target rule unless this document later approves it.