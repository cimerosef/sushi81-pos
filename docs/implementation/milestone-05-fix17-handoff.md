# M05 FIX-17 — Final responsiveness and payment-date layout remediation

**Status:** Approved for execution  
**Handoff ID:** `M05-FINAL-RESPONSIVENESS-AND-PAYMENT-DATE-LAYOUT-FIX-17`  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`

## Purpose

Close the two residual blockers recorded in `docs/implementation/milestone-05-manual-acceptance-findings-10.md`:

1. FIX-16 made the French payment-effective-date text readable but moved the DatePicker to the far-right edge of the Commandes detail area; restore compact, nearby operator placement without reintroducing clipping.
2. The project owner reports a severe application-wide responsiveness regression in the FIX-16 production build compared with the immediately preceding production baseline. Diagnose the actual cause and restore pre-FIX-16 fluidity while preserving the now-correct category-selection behavior.

## Authoritative comparison points

- Current branch head at handoff preparation includes documentation after FIX-16.
- FIX-16 production commit: `00d365afdee4157067ba36755ea5096c0482bcac`.
- Immediate pre-FIX-16 parent production baseline: `aaf710c98c5a77d0addb43eb48a0faace9ce5f97`.
- FIX-16 production delta is intentionally narrow:
  - `src/Sushi81.Pos.Desktop/MainWindow.xaml`
  - `src/Sushi81.Pos.Desktop/OrderEntryShellViewModel.cs`
  - focused tests.

Do not guess the lag cause. Isolate it against this exact delta.

## A. Payment effective-date layout

Required behavior:

- keep the DatePicker compact (approximately the existing 145 px control width is acceptable);
- keep the DatePicker physically near its label and the other Commandes edit controls; it must not be aligned to the far-right edge of the detail pane;
- French label and explanatory hint must remain fully readable at normal supported size and `760×520`;
- zh-CN must remain readable at the same sizes;
- preferred composition: a compact left-aligned local group, for example label + DatePicker adjacent on the first line and the explanatory hint beneath them, or an equivalent compact arrangement;
- the hint may wrap;
- do not globally widen unrelated label rows or long-text fields;
- outside edit mode the block remains hidden, preserving FIX-13/15 behavior.

Add/adjust actual STA/WPF assertions so they prove not only readability but also placement. The test must fail if the DatePicker is parked at the far-right edge on a wide window. Assert a bounded horizontal distance from the left edit cluster/label, not merely a positive width.

## B. Responsiveness regression — mandatory diagnosis before final fix

Owner observation is authoritative acceptance evidence: the FIX-16 build is markedly less fluid than the prior build and ordinary clicks frequently produce visible stalls.

Required diagnostic sequence:

1. Compare FIX-16 production behavior against `aaf710c98c5a77d0addb43eb48a0faace9ce5f97` using synthetic/test data only.
2. Isolate the two production deltas independently where practical:
   - payment-date WPF layout change;
   - category/product refresh-path change.
3. Check WPF measure/arrange/layout invalidation behavior introduced by the new row-spanning payment-date Grid. A functional layout can still be pathological; do not dismiss this because tests pass.
4. Check the category/product refresh path for redundant, recursive, or unnecessarily expensive work:
   - one category selection should cause at most the intended one product refresh;
   - ordinary category filtering must not reload/rebuild Categories;
   - unrelated clicks must not trigger catalogue/product refreshes;
   - search/category rapid changes must cancel obsolete work safely rather than queueing expensive reads;
   - no avoidable database/read work should execute synchronously on the WPF UI thread.
5. Inspect actual SQLite catalogue query behavior: `ListProductsAsync` currently reads the product result set then applies category/search filters in managed code. Do not broaden scope into a database redesign unless evidence shows it is required; however, do not introduce a refresh pattern that amplifies this cost on UI interactions.
6. If the root cause is elsewhere inside the exact FIX-16 delta, document it precisely and make the smallest reliable correction.

The goal is not a synthetic micro-optimization. The corrected build must restore the pre-FIX-16 subjective/operator fluidity.

## C. Preserve already-passed FIX-16 category behavior

The following must remain true after remediation:

- selecting `L`, `P`, `R`, empty categories, and `Tous` leaves the chosen row visibly selected after product refresh;
- the whole-list red/dotted focus appearance must not replace the selected-row visual;
- right-side Products match the active category;
- `Tous` clears the category filter;
- ordinary category selection does not rebuild/requery the category source unnecessarily;
- FR ↔ zh-CN localization round trips remain stable.

## D. Deterministic evidence

Add focused evidence that covers the actual regression risks, not only final state:

- category selection produces exactly one intended product refresh per selection in the test seam and does not query Categories;
- unrelated representative UI interactions do not trigger product/category refresh calls;
- rapid category/search changes do not leave duplicate stale refresh work applying after the latest selection;
- selected-row visual remains stable after asynchronous completion;
- payment-date label/hint readable and DatePicker near the label at 980×700 and 760×520 in FR and zh-CN;
- language round-trip remains correct.

Where feasible, record comparative call-count or elapsed responsiveness evidence using a deterministic delayed/fake catalogue or another repeatable test seam. Do not use brittle wall-clock thresholds as the sole proof, and do not fabricate performance numbers.

## E. Verification

Before completion:

- Release build: 0 warnings / 0 errors;
- full solution tests pass;
- focused STA/WPF tests pass;
- win-x64 self-contained evidence publish passes;
- final GitHub CI succeeds on the exact pushed production head;
- no migrations 1–5 changed;
- no real business data touched;
- PR #10 remains open/unmerged;
- M06 remains not started/unauthorized.

## Completion protocol

Push the implementation and evidence to the existing PR #10 branch, then post a separate top-level PR Conversation comment whose first line is exactly:

`CODEX_DONE: M05-FINAL-RESPONSIVENESS-AND-PAYMENT-DATE-LAYOUT-FIX-17`

Include:

- pushed head SHA;
- exact diagnosed root cause of the responsiveness regression and how it was isolated;
- changed files;
- payment-date layout correction;
- category-selection preservation evidence;
- focused responsiveness/call-count evidence;
- Release build/full-test/focused-test/publish/CI evidence;
- blockers/findings or explicitly none;
- confirmation PR remains open/unmerged and M06 was not started;
- browserNotification outcome.

`POST_TASK_POWER_ACTION: NONE`
