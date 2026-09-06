# M05 manual Windows/WPF acceptance findings — batch 09

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `0a0fb4caeb4b958adc26f770fa2e034b84e0c8ff`  
**Overall result:** FIX-13 through FIX-15 targeted UX regression passed manually except for two residual presentation defects: French payment-effective-date text clipping and Caisse category-selection visual/focus instability.

## Passed manual regression

The project owner manually confirmed the targeted FIX-13 through FIX-15 behaviors on the reviewed/published head:

- Caisse dashboard emphasis is correct: Future / due-today / overdue counts are red, operational turnover is bold, CB is blue, Espèce is green;
- Caisse short fields use compact practical widths;
- Commandes Total TTC / CB / Espèce / effective-payment-date controls use compact practical widths;
- Caisse and Commandes quantity +/- controls work, including `1 -> 0` removal semantics;
- existing-order `1 -> 0` removal remains draft-only until Save and Abandon restores the persisted line;
- quantity edit dialogs expose adjacent +/- controls;
- editable numeric fields select the current value on focus as intended;
- decimal monetary input accepts both comma and dot, including numeric-keypad dot;
- payment effective date is hidden outside edit and appears only in modification mode with current BusinessDate default;
- operational-view escape through normal date browsing / localized date-browse action works;
- FR -> zh-CN -> FR switching preserves the tested layout and behavior.

## Residual defect 1 — French payment-effective-date wording clipped

In the French Commandes modification UI, the payment-effective-date label/help block is visibly clipped/narrow. The equivalent Simplified Chinese presentation is acceptable.

Current XAML uses a Commandes detail grid whose first column is fixed at `145` and places both `OrderEffectiveDateEdit` and wrapped `OrderEffectiveDateHint` in that narrow column. The date picker itself is correctly compact and should remain so.

Required correction:

- the full French label/help text must remain readable without truncation at normal supported window size and at the supported minimum/small window size;
- zh-CN must remain readable;
- do not make the DatePicker itself unnecessarily wide;
- prefer a row-local layout adjustment rather than globally widening unrelated short-label rows if that gives the cleanest result.

## Residual defect 2 — Caisse category selection loses normal selected/focus appearance

The Caisse product category navigation list (`Tous`, category short-code letters such as `L`, `P`, `R`) intermittently loses the normal selected-item highlight after selecting some categories. The owner observed a red/dotted focus rectangle around the list while the expected selected row highlight disappears. The expected behavior is the stable selected-row appearance shown when another category remains normally selected.

Code inspection of the exact tested head identifies a likely cause worth testing directly:

- `SelectedCategoryId` triggers `RefreshProductsAsync()`;
- `RefreshProductsAsync()` delegates to the full `RefreshAsync()`;
- `RefreshAsync()` clears and rebuilds the entire `Categories` collection before repopulating Products;
- rebuilding the category collection on every category filter click recreates ListBox item containers and can disturb selection/focus visual state even though only the product result set needs refreshing.

Required behavior:

- choosing any category must leave that category visibly selected after the product grid refresh completes;
- no transient/remaining whole-list error-like focus rectangle should replace the selected-row visual;
- the category ListBox should retain normal mouse/keyboard usability;
- changing category must still refresh Products correctly;
- `Tous` must still clear the category filter correctly;
- language refresh may rebuild localized category labels if genuinely needed, but ordinary category-filter selection should not unnecessarily rebuild the category collection;
- do not change Catalogue/business semantics.

## Acceptance state

All targeted FIX-13 through FIX-15 manual regression items are Passed except the two presentation defects above. They are narrow final-M05 polish/blockers before final documentation/status closure and merge consideration.

PR #10 remains open/unmerged. M06 remains unauthorized/not started.
