# Post-M14 Production Maintenance Batch 01 — worklog

**Status:** ACTIVE
**Baseline:** `71024888a9cfd08109496ad076002be96e708c1a`

This is an append-only implementation/evidence ledger. Historical entries must not be rewritten to make later outcomes look earlier.

## 2026-09-29 — owner scope approval

The owner reported and approved one combined maintenance batch containing:

1. intermittent unintended duplicate product addition in Caisse — correctness bug / first priority;
2. cart auto-reveal after a new line is appended;
3. natural alphanumeric current product-code ordering;
4. applied Retrait discount amount on customer tickets.

Controller ordering is WP1 -> WP2 -> WP3 -> WP4 -> WP5 integration/candidate.

Owner acceptance is planned on computer A PREPROD only because the approved scope does not touch the M14 deferred multi-device areas. M12 populated real archive verification remains separately deferred. M14 two-PC PREPROD verification remains deferred/not Passed.

## 2026-09-29 — WP1 duplicate-add repair implementation

Handoff: `POST-M14-PM01-WP1-DUPLICATE-ADD-REPAIR-01` on Draft PR #31, starting at `b879937ebb05697983c895e031c0631a0a4160be`. Issue #4 was OPEN when execution began.

The real Caisse WPF event path exposed two separate failures. `Control.MouseDoubleClick` also reached `OnAddOrderProduct` on the third click of a continuous click sequence, so a single three-click burst added the same simple product twice. While an earlier asynchronous product lookup was pending, `orderProductAddInProgress` discarded subsequent deliberate Add button gestures, including different selected products. Before the repair, deterministic STA regressions failed with respectively two lines instead of one and one line instead of three.

The handler now accepts one grid add per completed double-click pair (click counts 2, 4, etc.), captures the product identity for each valid grid or button gesture, and processes those requests serially. Each request uses its own fetched product rather than shared `PendingProduct` state. The entry view model checks writability around the asynchronous lookup. There is no cart product deduplication or delay-based filter; repeated deliberate adds remain separate lines. The option dialog still adds only on confirmation. An existing Hiboutik transition regression now also asserts that an ordinary manually added line survives a presentation refresh without duplication.

Automated coverage: A→B→C grid additions, a three-click continuation, a fourth click completing a second valid pair, fast A→B→C button additions during a pending lookup, repeated deliberate A adds, option confirmation/cancellation, and Hiboutik manual-line preservation. Focused Release tests passed 4/4 before the final fourth-click refinement. Full Release solution tests passed 975/975 with 0 failures and 0 skipped, and Release solution build passed with 0 warnings and 0 errors before that refinement. Final post-refinement validation and exact-head CI are recorded in the PR completion evidence.

Manual computer A PREPROD acceptance remains for the owner/controller after WP1 review. No WP2–WP5 implementation or Production promotion is included in this entry.

## 2026-09-29 — WP1 R2 WPF double-click reality repair

Handoff: `POST-M14-PM01-WP1-WPF-DOUBLECLICK-REALITY-REPAIR-01R2`, starting at R1 head `092924df7f9eb1bf6611ff7eaeb2c344d7d6422e`. The preceding R1 entry is retained as history; its claim that WPF `Control.MouseDoubleClick` exposes click counts 2/3/4 to the handler was incorrect. Controller review blocked R1 on that finding. In WPF, `Control.HandleDoubleClick` responds to an underlying mouse-down with `ClickCount == 2`, constructs fresh `MouseButtonEventArgs` for `MouseDoubleClick`, and leaves their `ClickCount` at the constructor default of 1. The R1 handler therefore rejected a normal grid double-click.

The existing M04 grid test no longer forces a click count. A new focused STA regression uses constructor-default `MouseDoubleClick` event args for A/C and an underlying second `MouseLeftButtonDown` with `ClickCount == 2` for B, letting WPF's class handler synthesize the grid event. It checks exact A→B→C cart deltas and identity, plus a separate deliberate A add. Against R1 code, the default-event regression failed with zero lines after A; after removing the invalid `MouseDoubleClick` click-count filter, it passed. The serialized captured-product queue remains in place, and the delayed-lookup button, option confirmation/cancellation, and Hiboutik manual-line regressions remain covered.

The delayed-lookup A→B→C test proves that R1's former in-progress early return lost deliberate additions. It does not reproduce the owner's intermittent *extra* product symptom. The earlier claimed three-click duplicate reproduction was based on an impossible synthesized-event shape and is withdrawn. Whether the Production duplicate symptom is fully resolved remains subject to the integrated computer A PREPROD owner acceptance in this batch; no automated result is presented as that manual proof.

R2 stays within WP1. Final focused/full Release tests, build, diff check, exact-head CI, and delivery status are recorded in the matching PR `CODEX_DONE`. WP2–WP5, real Production data, Production deployment, and merge remain outside this repair.
