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

## 2026-09-29 — WP2 cart auto-reveal implementation

Handoff: `POST-M14-PM01-WP2-CART-AUTO-REVEAL-02`, starting at controller-accepted WP1 R2 head `e9438bd26f9d107db08ad580d06f594dceeeefc7`. The owner's intermittent Production extra-add symptom remains unresolved and is carried forward to the computer-A PREPROD empirical verification in WP5/final owner acceptance.

`MainWindow` now observes `Entry.Cart.CollectionChanged` while loaded and detaches when closed. For an actual `Add`, it schedules one WPF Dispatcher Loaded-priority callback per inserted line and calls `orderCartList.ScrollIntoView(line)` only if that exact line is still in the list and the window remains open. The callback neither changes selection nor focus. Collection remove/reset, quantity updates, existing-line reconfiguration and repricing do not schedule a reveal. This is a UI-only presentation seam; cart, pricing and order semantics are unchanged.

The new STA regression opens a real Caisse window with a small cart viewport and enough lines to overflow. Before implementation it failed because a newly inserted line was not realized in the viewport. After implementation it checks two consecutive new lines are each visible by their realized `ListBoxItem` bounds, then verifies quantity change, reconfiguration, reprice and removal leave a manually restored top scroll offset unchanged; clearing the cart leaves no pending reveal or selected item. Final focused/full Release tests, build, diff check and exact-head CI are recorded in the matching PR `CODEX_DONE`.

WP3–WP5, real Production data, Production deployment and merge remain outside this package.

## 2026-09-29 — WP3 natural current-product code ordering

Handoff: `POST-M14-PM01-WP3-NATURAL-PRODUCT-CODE-ORDER-03`, starting at controller-accepted WP2 head `499084f3cbdbbde4b3f635a678093e6048253e80`. WP1's owner-observed intermittent Production extra-add symptom remains pending computer-A PREPROD empirical verification.

`NaturalProductCodeComparer` in the Application Catalogue layer compares ASCII digit runs by significant length and digits, without parsing them into bounded numeric types. Other characters compare with invariant case folding. Equal natural keys use ordinal-ignore-case raw code, then ordinal raw code as deterministic tie-breakers; for example `R001 < R01 < R1 < r1`. Exact code ties are resolved by `ProductId` in the current-product query.

`SqliteCatalogueStore.ListProductsAsync` applies the comparer after its existing category, active and code/name search filters. Both `CatalogueService.ListProductsAsync` and `OrderEntryCatalogueService.ListActiveProductsAsync` consume that query; their Desktop view models copy the returned product order without re-sorting. Category order, the separate workbook snapshot/export query and import identity rules are unchanged. This is a current-product read-order change only; it does not modify rows or historical snapshots.

Unit regressions cover the approved R/ML/R4a code families, mixed case, multi-run codes, leading-zero/equal-natural-value ties, exact ties and digit runs beyond Int64. Synthetic SQLite integration checks unfiltered, active/inactive, category, name search and code search results, Catalogue/Caisse service agreement, and preservation of product values and IDs. Focused/full Release tests, build, diff check and exact-head CI are recorded in the matching PR `CODEX_DONE`.

WP4–WP5, real Production data, Production deployment and merge remain outside this package.


## 2026-09-29 — owner-approved WP4 printing scope extension

While WP3 was executing, the owner added one small printing optimization to the same batch: kitchen tickets should leave approximately three writable blank lines after the final `TOTAL` so staff can handwrite information after printing.

Controller classification: this is a protected business-facing printing-layout change, so it is recorded explicitly in the Approved decision, acceptance amendment, implementation contract, authorization and final PREPROD checklist before WP4 execution. It is grouped into WP4 with the already-approved customer-ticket Retrait discount line. Customer tickets do not receive the kitchen handwriting spacer. The change remains computer-A PREPROD acceptance only and does not touch pairing/authority/handoff/DR or the M14/M12 deferred obligations.
