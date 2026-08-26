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

The Phase 1 restriction on changing a catalogue product's **base unit price** does not prevent an operator from overriding the **final order total** at order level. These are separate concepts.

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

### 4.3 Required customer/order information

Phase 2 must define when telephone is:

- optional;
- strongly recommended but not required;
- required, if any case exists.

Delivery address remains mandatory for `Livraison`.

### 4.4 Product-option price adjustments

Phase 2 must define:

- whether custom option adjustments may be zero, positive and/or negative;
- allowed decimal precision;
- whether a maximum/minimum adjustment is needed;
- whether the operator must choose an option label before entering a custom adjustment;
- whether a custom adjustment requires a comment/reason;
- how adjustments interact with discount eligibility and VAT.

### 4.5 Manual final order total — approved Phase 2 decision

The application must calculate a normal order total from the current product lines, quantities, product-option adjustments and approved pricing/discount rules.

However, the operator must also be able to manually set a **final recorded order total** when an exceptional real-world situation requires a value different from the system-calculated total.

The rules are:

- the product catalogue base unit prices remain unchanged;
- historical order-item snapshots remain unchanged unless the operator separately edits the actual items/options;
- the system-calculated total should remain available for reference;
- the manually entered final order total becomes the authoritative order total used for the retained business record, printing, close/reconciliation validation and downstream turnover/export behavior unless another specification explicitly says otherwise;
- a difference between the system-calculated total and the final recorded total may be visibly indicated to the operator, but the difference must **not** block saving or confirming the order;
- no mandatory refund, surcharge, correction or reason workflow is created merely because the two totals differ.

This capability is intentional. Sushi81 POS is a practical operational/turnover-recording tool, and the operator may encounter exceptional situations not anticipated by the normal pricing rules. The software should allow the operator to record the business amount that has actually been decided rather than forcing the calculated product total to remain authoritative.

The exact UI control for invoking/resetting a manual total override will be decided during UI design.

### 4.6 Rounding and monetary consistency

All target pricing rules must define deterministic euro-cent rounding so that cart totals, receipts, payment totals, exports and reconciliation agree.

The exact rounding point for percentage discounts and VAT presentation remains to be frozen.

A manually overridden final order total is itself stored to euro-cent precision and is not silently recalculated back to the product-derived total.

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
9. which commercial values are operator-configurable in v1;
10. exact UI interaction for applying or resetting a manual final-order-total override.

The ability to manually override the final order total itself is already approved and is no longer an open question.

## 7. Approval rule

This file remains a Draft until the remaining target rules above are explicitly approved. Codex must not use current VBA warning/override behavior as the default target rule unless this document later approves it.