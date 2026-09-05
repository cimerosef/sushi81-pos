# M05 manual Windows/WPF acceptance findings — batch 04

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-05  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `e4796207c0ffa7c8e3c3655b8a14d950e0d421a6`  
**Overall result:** Failed / blocked — existing-order quantity-edit pricing produced an unexplained incorrect total. Remaining M05 manual acceptance is paused until this pricing path is diagnosed and corrected.

## Finding — acceptance item 7 existing-order quantity modification

During acceptance item 7, the project owner tested order `20260902-004` and observed the following simple product amounts:

- `TST002`: quantity 1, unit base price €12.00;
- `TST001A`: quantity 2, unit base price €8.50, extended base €17.00;
- simple merchandise base total: €29.00.

With a 10% ordinary Retrait discount and no other price component, two discount-eligible lines would produce €26.10 under the approved line/component-first round-half-up rule (`12.00 × 90% + 17.00 × 90%`). The application instead showed an authoritative order total of **€25.49** after the existing-order edit path.

The observed €25.49 cannot be explained by the simple €29.00 base total under any ordinary combination of the two lines being discount-eligible/non-eligible at a 10% rate:

- both eligible: €26.10;
- only €17.00 line eligible: €27.30;
- only €12.00 line eligible: €27.80;
- neither eligible: €29.00.

The live order may contain persisted line adjustments or other historical pricing state not represented in the concise operator report, so the defect diagnosis must not assume the live data shape. Codex must not access or modify the owner's real business database. Reproduce with synthetic snapshots first and determine whether the existing-order repricing path itself is wrong or whether UI/detail presentation hides a persisted price component that legitimately contributes to the total.

## Approved pricing authority to preserve

The existing frozen V1 rules remain unchanged:

1. Ordinary Retrait discount applies only to Product lines whose persisted sale-time `ProductDiscountEligible` snapshot is true.
2. A non-eligible Product line receives no ordinary Retrait discount; its normal line amount remains fully included.
3. For an eligible Product line, negative option/custom adjustments reduce the discountable Product component before discount.
4. Positive option/custom surcharges remain outside the ordinary Retrait discount.
5. Discount rounding is deterministic line/component-first round-half-up to cents.
6. Existing-order quantity edits use persisted sale-time line snapshots plus current BusinessSettings; current Catalogue price/eligibility must not silently rewrite an existing line.
7. A prior manual total override is cleared by a price-affecting change and the ordinary calculated total becomes authoritative again.

## Required regression matrix

The remediation must add direct tests for the M05 existing-order snapshot repricing path using synthetic prices equivalent to the operator scenario:

- €12.00 × 1 plus €8.50 × 2, both discount-eligible, 10% => €26.10;
- €12.00 line non-eligible, €8.50 line eligible => €27.30;
- €12.00 line eligible, €8.50 line non-eligible => €27.80;
- both non-eligible => €29.00;
- eligible line with a negative adjustment: adjustment reduces discountable component before the 10%;
- eligible line with a positive adjustment: positive surcharge is added after discount and is not discounted;
- non-eligible line with signed adjustments: the entire resulting line remains outside the ordinary Retrait discount;
- quantity change must preserve historical `ProductBasePriceTtc`, `ProductDiscountEligible`, adjustment snapshots and VAT snapshots rather than consulting current Catalogue data.

Tests should cover the pure `OrderPricingService.CalculateSnapshots` boundary and the real M05 existing-order `SaveModificationAsync` path. A WPF/view-model regression should verify that the operator-visible total after quantity save equals the persisted authoritative total.

## Acceptance state

Acceptance item 7 is Failed / blocked. Items 8–10 are not yet accepted and should wait for this pricing correction because they exercise the same snapshot repricing boundary.

Do not mark M05 Passed. PR #10 remains open/unmerged. M06 remains not authorized.
