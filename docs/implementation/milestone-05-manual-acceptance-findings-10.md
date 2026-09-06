# M05 manual Windows/WPF acceptance findings — batch 10

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `00d365afdee4157067ba36755ea5096c0482bcac`  
**Overall result:** The Caisse category-selection defect is fixed. The French payment-effective-date text is readable, but the new row layout places the DatePicker at the far-right edge instead of near the related label/controls. The owner also reports a severe responsiveness regression in the FIX-16 build: ordinary clicks throughout the application now frequently feel blocked/stalled compared with the smooth pre-FIX-16 build.

## Passed — Caisse category-selection stability

The project owner republished the FIX-16 production build and manually verified the Caisse category navigation behavior. The previously observed selected-row/focus instability is resolved: category selection remains visually stable and product filtering behaves correctly.

This requirement must remain preserved by any subsequent remediation.

## Residual presentation defect — payment-effective-date control placement

FIX-16 made the French label/help text readable, but the implementation spans the row across the full Commandes detail width and uses a star-sized text column plus an Auto DatePicker column. At normal wide-window size this pushes the DatePicker to the extreme right edge of the detail area.

That is not acceptable operator ergonomics. The payment-effective-date control is an input used while modifying an order and must remain visually/physically close to its label and the other edit controls.

Required presentation behavior:

- French label/help text must remain fully readable;
- zh-CN must remain readable;
- the DatePicker must remain compact;
- the DatePicker must be left-aligned within the normal edit-control cluster, not parked at the far-right edge of the window/detail area;
- a preferred layout is a compact local group: label and DatePicker adjacent on the first line, with the explanatory hint below them (or an equivalently compact arrangement);
- at supported small-window size, wrapping is allowed but the input must remain easy to reach and visually associated with the label;
- no unrelated Commandes rows should be widened just to solve this one row.

## Blocker — FIX-16 responsiveness regression

After republishing the FIX-16 production build, the project owner reports a clear regression in application responsiveness: ordinary clicks now frequently cause noticeable stalls/lag, and the application feels substantially less fluid than the immediately preceding build.

This regression was not present before FIX-16 and is a release blocker even though functional tests are green.

The production-code delta introduced by FIX-16 is narrow and therefore must be isolated carefully rather than guessed at:

- `src/Sushi81.Pos.Desktop/MainWindow.xaml` changed the payment-effective-date layout;
- `src/Sushi81.Pos.Desktop/OrderEntryShellViewModel.cs` changed category/product refresh behavior to preserve category selection;
- the parent production baseline before FIX-16 is `aaf710c98c5a77d0addb43eb48a0faace9ce5f97` (the FIX-16 commit parent); current later branch commits are documentation-only after the FIX-16 production commit.

Required diagnosis/remediation:

1. compare the responsive pre-FIX-16 production baseline with the FIX-16 production delta and identify the actual cause(s) of the UI stall;
2. do not assume the category-refresh change is the cause merely because it is the larger code change; also test whether the new WPF row layout causes pathological measure/arrange or invalidation behavior;
3. verify that ordinary category selection performs only the intended product refresh, does not recursively/redundantly trigger refreshes, and does not rebuild Categories;
4. verify that unrelated clicks (tab changes, selecting an order, entering modification, selecting a product, focusing numeric/date controls) do not trigger unnecessary catalogue/database refresh work;
5. keep I/O and expensive work off the WPF UI thread wherever applicable;
6. preserve the now-passed category selected-row behavior;
7. add deterministic regression evidence that guards against repeated refresh/event loops and covers the relevant STA/WPF interaction path;
8. if practical in the available environment, record comparative interaction/refresh timing or call-count evidence between the pre-FIX-16 shape and the corrected implementation. Do not fabricate performance numbers.

The owner should not be asked to accept a build that merely passes unit/STA tests while the visible UI remains sluggish.

## Scope guard

This is final M05 remediation only. No schema/migration changes, no business-rule changes, no M06 work, and no merge of PR #10 without explicit project-owner approval.

PR #10 remains open/unmerged. M06 remains unauthorized/not started.
