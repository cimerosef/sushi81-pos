# M05 manual Windows/WPF acceptance findings — batch 12

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** ChatGPT code/evidence review after owner-requested FIX-18 review  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline reviewed:** FIX-18 `e38a688e6314d82c042595537441195e1a3d23d3`  
**Overall result:** FIX-18 responsiveness remediation and evidence are accepted for owner-side retest, but one concrete payment-date visibility regression blocks republish until a minimal correction is made.

## Accepted FIX-18 evidence

- The global `commandesGrid.LayoutUpdated` subscription/handler is removed.
- Commandes long-text column sizing is bounded to `Loaded` / `SizeChanged` paths instead of continuous window-wide layout callbacks.
- STA/WPF counting evidence verifies repeated top-level tab switching and ordinary focus/selection do not, by themselves, trigger Catalogue / OrderEntry / Lifecycle / dashboard / settings reads and do not create continuing width-mutation loops.
- The agent did not reproduce the owner-visible multi-second stall and therefore correctly did not claim final responsiveness PASS.
- An opt-in diagnostic mode exists via `SUSHI81_POS_PERF_TRACE=1`, disabled by default, writing only technical markers to `%TEMP%\Sushi81-POS\perf-trace.log`.
- French payment-date wording is now `Date d'encaissement`, hint `Date utilisée pour cette modification.`, and the compact 145px DatePicker is back in the ordinary right edit column.
- CI run `34033234605` is success on exact production head `e38a688e6314d82c042595537441195e1a3d23d3`; complete suite 340/340; Release build 0 warnings / 0 errors.

## Review blocker — orphan payment-date label outside edit mode

FIX-18 moved `lifecycleEffectivePaymentDateLabel` back into the ordinary left label column, but it is now a standalone `TextBlock` without the `IsEditing` visibility binding used by `lifecycleEffectivePaymentDatePanel`.

Result: when modification mode ends, the DatePicker/hint collapse but the label `Date d'encaissement` remains visible by itself. This conflicts with the approved payment effective-date UX: this is a current-save payment-attribution input and must not be presented outside modification mode as if it were persisted order history.

The current STA test checks only `panel.Visibility == Collapsed` after `AbandonModification`; it does not check `label.Visibility`, so the regression escaped the green suite.

Required correction is deliberately narrow:

1. make the label use the same edit-only visibility as the panel (or equivalently hide the full row while preserving the normal two-column alignment);
2. keep FR `Date d'encaissement`, current short hint, zh-CN wording, 145px DatePicker, left label/right editor layout;
3. extend actual STA/WPF evidence so label + panel/picker/hint are visible while editing and both label + panel are collapsed after Abandon/save exit;
4. do not change FIX-18 performance/layout-event remediation, category behavior, business semantics, migration, or M06 scope.

## FIX-19 review closure

FIX-19 `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70` closes the above visibility blocker with the smallest approved production change: the standalone payment-date label now uses the same `IsEditing` BooleanToVisibility binding as the editor panel. Actual STA/WPF assertions now cover label visibility in edit mode and collapse after Abandon. The FIX-18 responsiveness remediation and FIX-16 category-selection behavior are unchanged.

Automated evidence reviewed:

- focused FIX-18/FIX-19 regression set: 4/4 passed;
- complete Release suite: 340/340 passed;
- Release build: 0 warnings / 0 errors;
- self-contained win-x64 publish: passed, 414 files;
- GitHub Actions run `34034418001`: success on exact pushed production head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`.

## Remaining acceptance

No further code-review blocker is known at this point. Owner-side broad Windows/WPF fluidity acceptance remains the only release-blocking manual check from FIX-18/FIX-19. Republish the current branch; the only commit after the FIX-19 production head is this documentation-only review record. Operate the application broadly for several minutes. If any 3–5 second or 10+ second stall remains, enable the existing `SUSHI81_POS_PERF_TRACE=1` diagnostic mode and capture `%TEMP%\Sushi81-POS\perf-trace.log` before any further speculative remediation.

PR #10 remains open/unmerged. M06 remains unauthorized/not started.
