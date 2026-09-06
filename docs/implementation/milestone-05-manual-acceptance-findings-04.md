# M05 manual Windows/WPF acceptance findings — batch 04

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-05  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Initial production-code baseline under test:** `e4796207c0ffa7c8e3c3655b8a14d950e0d421a6`  
**Current state:** pricing/rounding portion resolved and manually accepted through FIX-08/FIX-09; a follow-up D2 snapshot-preservation regression was found by code review after FIX-09 and blocks continuation of items 8–10 until fixed.

## Finding — acceptance item 7 existing-order quantity modification

During acceptance item 7, the project owner tested order `20260902-004` and observed the following simple product amounts:

- `TST002`: quantity 1, unit base price €12.00;
- `TST001A`: quantity 2, unit base price €8.50, extended base €17.00;
- simple merchandise base total: €29.00.

The order also contained historical signed option adjustments and a 12.5% Retrait discount. Follow-up diagnosis showed that the old persisted €25.49 total reflected the pre-FIX-08 rounding sequence. FIX-08 corrected the shared Domain pricing rule to the already-approved line/component-first sequence.

The owner then manually verified:

- merely viewing the old persisted order did not silently rewrite its historical total;
- a genuine price-affecting edit repriced the old order to the corrected authoritative total of **€25.51**;
- a new equivalent order also calculated **€25.51**;
- after changing `TST001A` quantity from 2 to 3, the application calculated **€32.94**, matching the corrected approved arithmetic.

Result for the FIX-08 pricing/rounding defect: **Passed**.

## Approved pricing authority to preserve

The existing frozen V1 rules remain unchanged:

1. Ordinary Retrait discount applies only to Product lines whose persisted sale-time `ProductDiscountEligible` snapshot is true.
2. A non-eligible Product line receives no ordinary Retrait discount; its normal line amount remains fully included.
3. For an eligible Product line, negative option/custom adjustments reduce the discountable Product component before discount.
4. Positive option/custom surcharges remain outside the ordinary Retrait discount.
5. Discount rounding is deterministic line/component-first round-half-up to cents.
6. Existing-order quantity edits use persisted sale-time line snapshots plus current BusinessSettings; current Catalogue price/eligibility must not silently rewrite an existing line.
7. A prior manual total override is cleared by a price-affecting change and the ordinary calculated total becomes authoritative again.

## FIX-09 visual result

FIX-09 removed the redundant inline quantity TextBox from selected-order rows. The owner manually confirmed:

- each row now keeps only the right-aligned edit/pencil and delete actions;
- the layout is visually acceptable;
- quantity can still be changed through the pencil dialog;
- the modified quantity is saved and repriced.

Visual/UI result: **Passed**.

## Follow-up finding — FIX-09 quantity-edit path can violate D2 snapshot preservation

**Status:** Open / blocker for continuing acceptance items 8–10.

During review immediately after FIX-09 manual UI acceptance, the production path at head `d7f5146cb051482c192c7217f337da447909a098` was rechecked against approved D2 semantics.

Current production behavior:

- `MainWindow.OnReconfigureOrderLine` always loads the current active Catalogue product before opening the edit dialog;
- it builds the draft with `line.ToCurrentDraft(product)`;
- confirmation calls `ReplaceLineAsync`;
- `ReplaceLineAsync` calls `CreateCurrentCatalogueLineAsync`, rebuilding the line from the current Catalogue product.

Consequences:

1. a quantity-only edit through the pencil action can adopt current Catalogue product/option facts instead of preserving the persisted sale-time snapshot;
2. if the historical product is now inactive or unavailable, the only remaining quantity-edit entry path may be blocked entirely;
3. this conflicts with approved D2 behavior: quantity changes use persisted line snapshots; explicit option reconfiguration uses current Catalogue; removed/inactive historical lines remain viewable, quantity-changeable and removable.

This is implementation-review evidence, not a speculative UI preference. The owner should not need to mutate real test Catalogue data merely to prove a code path already demonstrated by inspection.

Required remediation is tracked by handoff `M05-MANUAL-ACCEPTANCE-SNAPSHOT-QUANTITY-RECONFIG-FIX-10`.

## Acceptance state

- Item 7 pricing/rounding behavior: Passed after FIX-08 manual retest.
- FIX-09 visual layout: Passed.
- Items 8–10: paused pending FIX-10 because they exercise the historical-snapshot/current-Catalogue boundary directly.

Do not mark M05 Passed. PR #10 remains open/unmerged. M06 remains not authorized.
