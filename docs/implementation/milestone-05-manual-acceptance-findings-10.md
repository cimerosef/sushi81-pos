# M05 manual Windows/WPF acceptance findings — batch 10

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner / ChatGPT review  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test/review:** FIX-18 `e38a688e6314d82c042595537441195e1a3d23d3`  
**Overall result:** FIX-18 correctly removes the continuous `commandesGrid.LayoutUpdated` width-feedback path, adds opt-in diagnostic tracing, restores the DatePicker to the normal right edit column, and preserves the already-passed Caisse category-selection behavior. GitHub CI and the full automated suite are green. However, code review found one concrete presentation regression that must be fixed before owner republish: the payment-date label is now outside the edit-only panel and lacks an `IsEditing` visibility binding, so `Date d'encaissement` remains visible even when the order is not in modification mode.

## Passed in FIX-18 code/evidence review

- The global `commandesGrid.LayoutUpdated += OnCommandesGridLayoutUpdated` subscription/handler is removed.
- Commandes long-text column sizing now runs only from bounded `Loaded` / `SizeChanged` paths.
- STA/WPF evidence counts hidden Catalogue / OrderEntry / Lifecycle / dashboard / settings calls during repeated top-level navigation and ordinary focus/selection and finds no semantically-unnecessary refreshes.
- The opt-in `SUSHI81_POS_PERF_TRACE=1` diagnostic mode is disabled by default and logs only technical timing/refresh/layout markers to `%TEMP%\Sushi81-POS\perf-trace.log`.
- French wording is shortened to `Date d'encaissement`; the hint is `Date utilisée pour cette modification.`; the 145px DatePicker is restored to the normal right edit column.
- Caisse category selection/filter stability from FIX-16 remains covered and must not regress.
- Release build is 0 warnings / 0 errors; complete test matrix is 340/340; CI run `34033234605` succeeded on exact head `e38a688e6314d82c042595537441195e1a3d23d3`.

## Remaining review blocker — payment-date label visible outside modification

Current FIX-18 XAML separates the label from the edit-only panel:

- `lifecycleEffectivePaymentDateLabel` is a standalone `TextBlock` in `Grid.Row="12"` / left label column;
- `lifecycleEffectivePaymentDatePanel` in the right edit column has `Visibility={Binding IsEditing, ...}`;
- the standalone label itself has no visibility binding.

Therefore outside modification mode the DatePicker/hint collapse but the `Date d'encaissement` label remains visible by itself. That violates the already-approved M05 payment effective-date UX: outside modification mode this current-save attribution field must not be presented as an order-level payment-date fact.

The existing STA test only asserts that `lifecycleEffectivePaymentDatePanel` collapses after `AbandonModification`; it does not assert the label collapses, which is why the regression escaped the 340/340 suite.

Required minimal correction:

1. bind `lifecycleEffectivePaymentDateLabel.Visibility` to the same `IsEditing` + BooleanToVisibility behavior as the panel (or use an equivalent parent container that hides label + editor together while keeping the ordinary two-column alignment);
2. preserve the exact final layout decision: label in left column, 145px DatePicker in right column, short hint under the DatePicker, FR `Date d'encaissement`, current zh-CN wording;
3. extend the actual STA/WPF test so both label and panel/picker/hint are visible in edit mode and both label and edit panel are collapsed after Abandon/save exit;
4. do not touch the FIX-18 layout/performance remediation, category filtering, business semantics, schema, or M06 scope.

## Responsiveness status

FIX-18 is **not** yet declared owner-manual PASS for application-wide fluidity. The agent could not reproduce the owner-visible 3–5 second / occasional 10+ second stalls, so the owner must still perform broad interaction testing on the corrected production build. If stalls remain, the opt-in trace path must be used before another speculative performance change.

## Scope guard

This is final M05 remediation only. PR #10 remains open/unmerged. M06 remains unauthorized/not started. No merge without explicit project-owner approval.
