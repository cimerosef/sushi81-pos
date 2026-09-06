# M05 FIX-19 — payment-date edit-only visibility closure

**Handoff ID:** `M05-FIX18-PAYMENT-DATE-VISIBILITY-FIX-19`  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Active PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Branch:** `codex/m05-lifecycle-payments-search-dashboard`  
**Production baseline:** FIX-18 `e38a688e6314d82c042595537441195e1a3d23d3`  
**Review finding:** `docs/implementation/milestone-05-manual-acceptance-findings-12.md`

## Purpose

Close one concrete code-review escape before asking the owner to republish FIX-18 for broad fluidity acceptance.

FIX-18 correctly restored the payment-date DatePicker to the normal right edit column and shortened the French text, but the standalone left-column `lifecycleEffectivePaymentDateLabel` has no `IsEditing` visibility binding. Outside modification mode the panel/picker/hint collapse while `Date d'encaissement` remains visible by itself.

This is not acceptable because the effective payment date belongs only to payment deltas created by the current modification/save; it is not an order-level historical payment-date field.

## Required production change

Make the smallest safe WPF correction:

- `lifecycleEffectivePaymentDateLabel` must be visible only when `IsEditing` is true, using the same BooleanToVisibility behavior as the editor panel; or use an equivalent container arrangement that hides the entire row while preserving the approved two-column alignment.
- Keep the label in the normal left label column.
- Keep `lifecycleEffectivePaymentDatePanel` in the normal right edit column.
- Keep the DatePicker compact at approximately 145px and left-aligned.
- Keep French exactly `Date d'encaissement`.
- Keep French hint `Date utilisée pour cette modification.` unless a purely equivalent punctuation/typography correction is unavoidable.
- Keep current zh-CN semantics/readability.
- Outside edit mode, label + editor panel + picker/hint must not present the field.
- Do not change payment semantics, effective-date persistence semantics, lifecycle behavior, schema/migration, or business rules.

## Preserve FIX-18 responsiveness remediation

Do not modify or reintroduce:

- the removed `commandesGrid.LayoutUpdated` subscription/handler;
- the bounded Loaded/SizeChanged column-sizing approach;
- the opt-in `SUSHI81_POS_PERF_TRACE=1` diagnostic path;
- the hidden-refresh/call-count behavior;
- Caisse category selection/filter stability.

No performance refactor is authorized in FIX-19. Owner-side broad fluidity acceptance happens after this micro-fix.

## Required automated evidence

Use actual STA/WPF controls, not source-text-only checks.

At minimum prove:

1. enter modification mode: label, panel, DatePicker and hint are visible in FR;
2. FR label is exactly `Date d'encaissement`, compact two-column layout remains intact;
3. switch to zh-CN while editing: label/editor remain readable/visible;
4. `AbandonModification`: label and editor panel both become `Collapsed`;
5. if an existing save/exit path is practical in the focused test, prove the same after successful save; otherwise retain existing save-path coverage and make the Abandon assertion explicit;
6. normal and 760×520 supported window sizes remain usable;
7. FIX-18 top-level-navigation/no-hidden-refresh/no-layout-loop test remains green;
8. FIX-16 category selection test remains green.

Then run:

- Release build: 0 warnings / 0 errors;
- complete solution tests;
- focused FIX-18/FIX-19 STA/WPF tests;
- win-x64 self-contained evidence publish;
- GitHub CI on the exact pushed production head.

## Completion

Push the implementation to PR #10 and post a new top-level PR Conversation comment whose first line is exactly:

`CODEX_DONE: M05-FIX18-PAYMENT-DATE-VISIBILITY-FIX-19`

Include exact SHA, changed files, focused/full build-test-publish-CI evidence, confirmation the label is edit-only, confirmation FIX-18 responsiveness remediation and category selection are unchanged, PR remains open/unmerged, M06 not started, and browserNotification outcome.

`POST_TASK_POWER_ACTION: NONE`
