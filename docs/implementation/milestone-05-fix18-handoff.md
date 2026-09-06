# M05 FIX-18 — application-wide UI stall root cause and final payment-date alignment

**Handoff ID:** `M05-FINAL-APP-WIDE-STALL-ROOT-CAUSE-FIX-18`  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Active PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Branch:** `codex/m05-lifecycle-payments-search-dashboard`  
**Production baseline to remediate:** FIX-17 `6af11fecb7e4593aaf5bd4e9cc76a048102abf18`  
**Last owner-accepted pre-FIX-16 UX baseline with no reported global stalls:** `0a0fb4caeb4b958adc26f770fa2e034b84e0c8ff`  
**Manual finding:** `docs/implementation/milestone-05-manual-acceptance-findings-11.md`

## 1. Why FIX-18 exists

FIX-17 did not close the responsiveness blocker.

The owner manually tested FIX-17 and reports:

- Caisse category selection remains correct — preserve this;
- the payment-date DatePicker is no longer at the far right, but the owner wants the original two-column edit-field position restored with shorter French wording;
- application responsiveness is still unacceptable: top-level tab switches commonly pause about 3–5 seconds, some pauses exceed ten seconds, and essentially any ordinary click/interaction can be followed by the same stall.

This is application-wide dispatcher/UI responsiveness. Do **not** treat it as merely a Caisse product-filter issue or a single payment-date-row layout problem.

## 2. Critical historical isolation

Before production edits, compare the exact owner-tested baselines:

- pre-FIX-16: `0a0fb4caeb4b958adc26f770fa2e034b84e0c8ff`;
- FIX-17 production: `6af11fecb7e4593aaf5bd4e9cc76a048102abf18`.

The durable GitHub history shows that the production-code difference over this interval is intentionally narrow: `MainWindow.xaml` plus `OrderEntryShellViewModel.cs` (tests/docs aside). Reconstruct that comparison yourself and record the exact result.

Do not assume the FIX-17 explanation (payment-date row layout as the remaining cause) is sufficient: manual evidence disproves closure because stalls remain even during unrelated interactions.

## 3. Mandatory diagnosis before claiming a fix

### 3.1 Global WPF layout/event path

Explicitly inspect and measure the existing global layout code in `MainWindow.xaml.cs`:

- `commandesGrid.LayoutUpdated += OnCommandesGridLayoutUpdated`;
- `OnCommandesGridLayoutUpdated`;
- `ResizeCommandesColumns` writing `DataGridColumn.Width`.

`LayoutUpdated` is a high-frequency WPF event and width mutation can trigger further measure/arrange work. Because any window interaction can participate in a layout pass, this is a concrete app-wide risk path.

Important: this subscription existed in earlier M05 builds, so it is a **suspect, not a pre-proven root cause**. Instrument/counter-test it. Determine:

- how many `LayoutUpdated` callbacks occur during idle and during repeated tab switching;
- how many callbacks actually mutate column widths;
- whether a width mutation causes further callbacks/oscillation;
- whether the handler runs while Commandes is not the selected tab;
- whether the same usable Commandes column layout can be achieved with a bounded event such as initial Loaded/SizeChanged/explicit one-shot sizing instead of a continuous global LayoutUpdated subscription.

If the continuous handler is unnecessary or causes repeated mutation/churn, remove it and replace it with the simplest deterministic low-frequency sizing mechanism. Preserve the accepted Commentaire/Adresse width behavior and horizontal scrolling.

### 3.2 Hidden refresh / I/O triggered by ordinary UI interactions

Using instrumented test doubles/counters, verify repeated operator actions do not cause hidden data work merely because controls become visible/focused:

- switch `Catalogue ↔ Paramètres ↔ Commandes ↔ Caisse` repeatedly;
- select ordinary rows/products without semantic mutations;
- focus numeric/date/text inputs;
- enter/abandon modification without changing data;
- move between tabs while existing selections are retained.

Count calls separately for:

- M03 Catalogue category/product refreshes;
- OrderEntry category/product queries;
- Lifecycle/date/search/order-detail queries;
- dashboard summary queries;
- settings reloads;
- any other SQLite-backed read initiated from WPF event/binding callbacks.

Top-level tab visibility/focus alone must not cause database/service refreshes.

### 3.3 Dispatcher and synchronous work

Audit the affected WPF event handlers and current refresh continuations for synchronous disk/SQLite/collection/layout work on the dispatcher.

- Keep I/O async/cancellable.
- Do not use `.Result`, `.Wait()`, synchronous DB reads, or blocking sleeps on the WPF dispatcher.
- Large ObservableCollection rebuilds must occur only when semantically required.
- Stale async refreshes must be cancellable and must not queue expensive stale UI updates.

### 3.4 Exact pre-FIX-16 A/B

Where practical in the Windows test environment, create detached/self-contained local evidence builds for `0a0fb4ca...` and the candidate corrected head using synthetic data and exercise the same scripted WPF interaction sequence. Record actual measured timings/callback counts only if the environment can measure them reliably; do not invent owner-machine-equivalent performance numbers.

If automated tests cannot reproduce the multi-second owner stall, that is acceptable only if you still isolate/remediate concrete event/refresh risks and provide an owner-runnable diagnostic path as described in section 6.

## 4. Payment-date final presentation — owner decision

Restore the normal two-column Commandes edit alignment.

Required:

- French label exactly: `Date d'encaissement`;
- label occupies the normal left label column;
- DatePicker occupies the normal right edit column, left-aligned with the other compact controls (Total TTC / CB / Espèce), approximately 145px wide;
- it must **not** be placed at the far-right edge and must not use a row-spanning star/auto arrangement that pushes it away;
- keep the semantic hint, but shorten it so it does not create clipping or an oversized row. Preferred French hint: `Date utilisée pour cette modification.` or an equivalently concise clear phrase;
- zh-CN remains readable and semantically correct;
- the payment-date control remains visible only during modification, as already approved;
- no business/payment semantics change.

## 5. Preserve already-passed behavior

Do not regress:

- Caisse category selected-row/highlight stability;
- one semantic product refresh per actual category/search change and no Categories rebuild for ordinary category filtering;
- rapid stale product-filter cancellation;
- compact Total/CB/Espèce fields;
- numeric select-all-on-focus and comma/dot parsing;
- quantity +/- and 1 -> 0 delete semantics;
- operational-view exit behavior;
- FR/zh-CN dynamic localization;
- all frozen lifecycle/payment/dashboard rules.

## 6. Diagnostic fallback if the owner-visible stall cannot be reproduced

Do **not** claim responsiveness PASS merely because CI/STA tests are green.

If the multi-second stall cannot be reproduced or causally isolated in the agent environment, add a narrowly scoped **opt-in** diagnostic mode for the next owner acceptance run:

- disabled by default, with effectively zero normal-runtime overhead;
- enabled only by an explicit environment variable such as `SUSHI81_POS_PERF_TRACE=1`;
- log only non-sensitive technical events: monotonic timestamps/durations, top-level UI interaction markers, dispatcher-gap markers, refresh/query start/end categories, cancellation, LayoutUpdated callback and width-mutation counts;
- do **not** log order/customer/business contents, phone/address/comment, prices, or database rows;
- write to an obvious temporary/local diagnostic file and report the exact path in CODEX_DONE;
- ensure the owner can reproduce for 1–3 minutes and send the log back without touching production data semantics.

If this diagnostic fallback is required, say so explicitly in CODEX_DONE and do not characterize FIX-18 as final manual PASS until the owner run confirms it.

## 7. Required automated evidence

At minimum add/retain deterministic tests that prove:

1. repeated top-level tab switching does not generate catalogue/order/dashboard/settings service calls merely because tabs become selected;
2. unrelated focus/selection interactions produce no hidden refresh calls;
3. the Commandes column sizing mechanism has no unbounded continuous `LayoutUpdated` width-mutation loop;
4. normal and 760×520 layouts retain usable Commandes list/detail widths and scroll behavior;
5. Caisse category selection remains stable and each semantic category change performs only the intended product refresh;
6. stale rapid filter work cancels;
7. payment-date layout is original-style two-column aligned in FR and zh-CN, normal and 760×520, with French `Date d'encaissement` and concise hint;
8. no regression in current focused FIX-13 through FIX-17 STA/WPF suites.

Then run:

- Release build: 0 warnings / 0 errors;
- complete solution tests;
- focused FIX-18 STA/WPF tests;
- win-x64 self-contained evidence publish;
- GitHub CI on the exact pushed production head.

## 8. Scope / safety

- No schema/migration changes.
- No business-rule changes.
- No real business-data mutation for evidence.
- No M06.
- Do not merge PR #10.
- `POST_TASK_POWER_ACTION: NONE`.

## 9. Completion record

When complete, push to PR #10 and post a **new top-level PR Conversation comment** whose first line is exactly:

`CODEX_DONE: M05-FINAL-APP-WIDE-STALL-ROOT-CAUSE-FIX-18`

Include:

- exact pushed SHA;
- exact A/B production-delta reconstruction;
- proven root cause(s), or clearly state if owner-side diagnostic capture is still required;
- layout-event callback/mutation evidence;
- hidden refresh/service call-count evidence for repeated tab/unrelated interactions;
- payment-date layout/localization evidence;
- changed files;
- focused/full build-test-publish-CI evidence;
- blockers/findings;
- confirmation PR #10 remains open/unmerged and M06 is not started;
- browserNotification outcome.
