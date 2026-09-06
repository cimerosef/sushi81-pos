# M05 manual Windows/WPF acceptance findings — batch 09

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `0a0fb4caeb4b958adc26f770fa2e034b84e0c8ff`  
**Overall result:** FIX-13 through FIX-15 targeted UX regression passed manually except for two residual presentation defects: French payment-effective-date text clipping and Caisse category-selection visual/focus instability. FIX-16 at `00d365afdee4157067ba36755ea5096c0482bcac` has passed ChatGPT code/evidence review and is pending only the project owner's final two-item Windows/WPF confirmation.

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

FIX-16 implementation review: the effective-payment-date row now spans both detail columns, keeps the date picker at 145 px, and gives the label/hint the remaining width with wrapping. The focused STA/WPF test covers fr-FR normal, fr-FR 760x520, zh-CN 760x520, and FR after a language round trip. Manual recheck remains pending.

## Residual defect 2 — Caisse category selection loses normal selected/focus appearance

The Caisse product category navigation list (`Tous`, category short-code letters such as `L`, `P`, `R`) intermittently loses the normal selected-item highlight after selecting some categories. The owner observed a red/dotted focus rectangle around the list while the expected selected row highlight disappears. The expected behavior is the stable selected-row appearance shown when another category remains normally selected.

Code inspection of the exact tested head identified the presentation-state cause:

- `SelectedCategoryId` triggers `RefreshProductsAsync()`;
- before FIX-16, `RefreshProductsAsync()` delegated to the full `RefreshAsync()`;
- the full refresh cleared and rebuilt the entire `Categories` collection before repopulating Products;
- rebuilding the category collection on every category filter click recreated ListBox item containers and disturbed selection/focus visual state even though only the product result set needed refreshing.

Required behavior:

- choosing any category must leave that category visibly selected after the product grid refresh completes;
- no transient/remaining whole-list error-like focus rectangle should replace the selected-row visual;
- the category ListBox should retain normal mouse/keyboard usability;
- changing category must still refresh Products correctly;
- `Tous` must still clear the category filter correctly;
- language refresh may rebuild localized category labels if genuinely needed, but ordinary category-filter selection should not unnecessarily rebuild the category collection;
- do not change Catalogue/business semantics.

FIX-16 implementation review: ordinary category filtering now refreshes Products only; full refresh updates Categories only when the desired category source actually differs. The focused STA/WPF test switches multiple categories including an empty category and `Tous`, verifies exactly one selected ListBox item remains selected, verifies the product results, and verifies category-source calls do not increase during ordinary filtering; it also round-trips zh-CN -> fr-FR. Manual recheck remains pending.

## FIX-16 evidence review

ChatGPT reviewed exact production-code head `00d365afdee4157067ba36755ea5096c0482bcac` and found no new blocker.

- Release build: 0 warnings / 0 errors.
- Full Release suite: 338 passed / 0 failed / 0 skipped.
- CI #416: success on the exact PR merge ref containing FIX-16; suite breakdown was Domain 33, Application 39, Infrastructure integration 52, Architecture 90, OneDrive feasibility 32, OneDrive tools 92.
- Focused FIX-16 STA/WPF evidence: 2/2 passed.
- win-x64 self-contained evidence publish: passed.
- no migration/schema changes; no M06 work; PR remains open/unmerged.

ChatGPT then made a documentation-only follow-up commit `323b556c392bcdb58100a43328b2983d03a21d5f` to record this review state; it does not alter production code. CI for that documentation-only head may run independently and does not replace the exact FIX-16 production evidence above.

## Acceptance state

All prior FIX-13 through FIX-15 manual regression items are Passed. FIX-16 has passed code/evidence review; only the two narrow project-owner visual confirmations remain before final M05 documentation/status closure and merge consideration.

PR #10 remains open/unmerged. M06 remains unauthorized/not started.
