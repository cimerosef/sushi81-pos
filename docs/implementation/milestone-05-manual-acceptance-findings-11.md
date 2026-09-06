# M05 manual Windows/WPF acceptance findings — batch 11

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `6af11fecb7e4593aaf5bd4e9cc76a048102abf18` (FIX-17)  
**Overall result:** Caisse category selection remains fixed. The FIX-17 payment-date control is no longer at the extreme right, but the owner prefers the original two-column edit alignment with a shorter French label. More importantly, FIX-17 did **not** resolve the release-blocking responsiveness regression: stalls remain application-wide and can affect essentially any interaction, including tab switches, with common pauses around 3–5 seconds and occasional pauses of ten seconds or more.

## Passed — Caisse category selection remains stable

The owner manually verified the FIX-17 build and confirmed that Caisse category filtering/selection remains correct. The selected category stays visually stable and product filtering is correct.

This passed behavior must not regress.

## Payment-date presentation refinement

The owner does not want the payment effective-date DatePicker moved away from the normal Commandes edit-field alignment.

Approved final presentation direction:

- French label: `Date d'encaissement` (replace the longer `Date d'encaissement effective` wording);
- restore the DatePicker to the original normal edit-control position/alignment used before the row-spanning FIX-16/FIX-17 experiments: label in the left label column, DatePicker left-aligned in the ordinary right edit column near Total TTC / CB / Espèce;
- the DatePicker remains compact (approximately the current 145px width);
- explanatory copy may be shortened/reworded to avoid clipping; a short hint under or adjacent to the normal edit cluster is acceptable;
- preserve the semantic meaning already frozen: this is the effective date for payment delta(s) created by the current modification, not a historical single “order payment date”;
- keep zh-CN readable; do not disturb already-passed compact widths elsewhere.

A suitable short French hint is `Date utilisée pour cette modification.` or an equivalently clear localized phrase.

## Release blocker — application-wide UI stalls remain

The FIX-17 diagnosis/remediation is **not accepted as closure** for responsiveness.

The owner reports that FIX-17 is only slightly better than the prior build. Severe intermittent UI stalls still occur throughout the application:

- switching between top-level tabs can take roughly 3–5 seconds to react;
- some stalls last ten seconds or more;
- the problem is not limited to tab switching: essentially any click or ordinary UI operation can be followed by the same kind of pause;
- therefore the defect is application-wide WPF/dispatcher responsiveness, not merely a Caisse category-query issue or one Commandes payment-date row.

This is a hard M05 release blocker.

## Important diagnostic correction after FIX-17

FIX-17 correctly established that ordinary Caisse category selection is not recursively rebuilding Categories and that stale rapid product-filter work is cancellable. However, that evidence does **not** explain the owner-visible application-wide stalls. The remaining diagnosis must reopen the full UI-thread/event/layout path rather than assuming the FIX-16 payment-date row was the sole cause.

One concrete global-risk path already visible in current production code must be investigated explicitly:

- `MainWindow` subscribes `commandesGrid.LayoutUpdated += OnCommandesGridLayoutUpdated`;
- `LayoutUpdated` is a high-frequency WPF layout event and the handler calls `ResizeCommandesColumns`;
- `ResizeCommandesColumns` can write `DataGridColumn.Width`, which itself can trigger further measure/arrange/layout work;
- because layout updates can be caused by interactions anywhere in the window, this path is capable of imposing global UI-thread churn even when Commandes is not the active operator task.

This is a **suspect, not a proven root cause**. It existed in earlier M05 builds, so the next remediation must measure/instrument it rather than simply asserting causality. If it is unnecessary or produces repeated layout work, replace it with a deterministic low-frequency mechanism (for example SizeChanged/one-shot/explicit layout sizing) that cannot form a layout feedback loop.

The next diagnosis must also verify whether top-level tab switching or unrelated clicks unexpectedly trigger any Catalogue, OrderEntry, Lifecycle, dashboard, search, SQLite, localization, or other refresh/I/O work.

## Required next-step evidence

Before claiming the stall fixed, the next implementation must provide deterministic evidence for all of the following:

1. repeated top-level tab switching (`Catalogue ↔ Paramètres ↔ Commandes ↔ Caisse`) does not trigger database/service refreshes merely because a tab became visible;
2. unrelated ordinary interactions do not trigger catalogue/order/dashboard refreshes without a semantic reason;
3. no high-frequency WPF `LayoutUpdated`/measure/arrange feedback path repeatedly mutates widths or other layout properties;
4. any necessary column sizing remains correct at normal and 760×520 window sizes without a global continuous LayoutUpdated mutation loop;
5. async refresh/search paths remain cancellable and do not marshal expensive synchronous work back onto the dispatcher;
6. UI-thread code paths do not perform avoidable synchronous disk/SQLite work;
7. the final build retains the passed Caisse category selection behavior;
8. the final manual acceptance must explicitly test broad interaction fluidity for at least several minutes, not just one focused click path.

If automated evidence cannot reproduce the real stall, add a narrowly scoped diagnostic mechanism that can capture event/refresh/layout counts or elapsed dispatcher gaps on the owner’s Windows acceptance machine without exposing business data. Such diagnostic code must be disabled by default and removable before M05 closure unless it is appropriate as lightweight long-term telemetry.

## Scope guard

This remains final M05 remediation only. No schema/migration changes, no business-rule changes, no M06 work, and no merge of PR #10 without explicit project-owner approval.

PR #10 remains open/unmerged. M06 remains unauthorized/not started.
