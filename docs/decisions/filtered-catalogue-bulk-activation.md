# Filtered catalogue bulk activation/deactivation

**Status:** Approved — V1 specification amendment  
**Date:** 2026-08-30  
**Applies to:** Sushi81 POS current catalogue maintenance / M03

## Context

During M03 interactive catalogue acceptance, the operator identified a practical maintenance need that was not explicitly covered by the frozen V1 baseline: after narrowing the current catalogue with the existing code/name search plus category and active/inactive filters, the operator must be able to activate or deactivate the complete filtered set without editing products one by one.

This is ordinary current-catalogue maintenance. It does not change product identity, historical snapshots, order-entry pricing, option semantics, Excel import/export rules or the meaning of Product active/inactive state.

## Decision

V1 adds explicit **bulk Activate** and **bulk Deactivate** actions to the in-application catalogue maintenance workflow.

The behavior is frozen as follows:

1. The existing catalogue filters remain the selection boundary:
   - code/name keyword search;
   - category filter;
   - status filter (`All` / `Active` / `Inactive`);
   - all filters compose together.
2. A bulk action applies to the **complete current filtered result set**, not merely rows currently visible in the rendered viewport.
3. Starting the action captures an immutable snapshot of the matching current Product IDs and the target state. The confirmation and eventual mutation refer to that captured set; a later UI/filter change must not silently retarget an already-open confirmation.
4. The confirmation UI must make the impact explicit before any write. It shows at least:
   - total products matched by the captured filtered result;
   - products that would actually change state;
   - target action (Activate or Deactivate).
5. Products already in the requested target state are skipped and are not rewritten merely to manufacture a change.
6. If zero products would actually change, the UI must not execute a business write. The action may be disabled in advance or may return a clear no-change message.
7. After explicit operator confirmation, all required active-state changes are committed **atomically**. A failure/conflict must not leave a partially activated/deactivated set.
8. The bulk mutation changes only Product active/inactive state. It must not alter code, name, category, TTC price, VAT, Retrait-discount eligibility, product option-enable state, OptionGroups, Options or their order/values.
9. Historical order snapshots are never changed by this current-catalogue operation.
10. The operation is not a bulk-delete mechanism. V1 adds **no bulk permanent deletion** workflow.
11. After success, the catalogue automatically refreshes while preserving the current search/category/status filter values. Depending on the status filter, successfully changed products may naturally disappear from the visible result.
12. All new operator-facing labels, confirmation text and result/error messages are localized in French and Simplified Chinese. Catalogue business data itself is never translated.

## Atomicity and application boundary

The implementation must provide one Application-owned bulk activation/deactivation use case and one atomic persistence operation (or an equivalent single transaction boundary). WPF must not implement the feature by looping over the existing single-product command and committing one product at a time.

Before committing, the mutation validates that every captured Product ID still identifies a current product. A stale/missing/conflicting target causes the complete operation to fail without partial state changes, followed by a catalogue refresh and clear operator feedback.

M03 must preserve the existing application/write-authority seams so M06 can later attach centralized authoritative/read-only enforcement without redesigning this workflow.

## Relationship to Excel import

This decision does not replace or weaken M10 catalogue `.xlsx` batch maintenance. Excel import may still explicitly activate/deactivate records under its own preview/validation/atomic-import contract. The new feature is the lightweight in-application bulk state-change workflow for ordinary operations.

## Acceptance impact

This amendment adds the V1 catalogue acceptance criterion defined as `AC-CAT-013 — Filtered bulk activation/deactivation` in the accompanying approved acceptance amendment.

M03 owns implementation and manual acceptance of this criterion.
