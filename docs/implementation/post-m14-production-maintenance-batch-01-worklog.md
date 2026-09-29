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

## 2026-09-29 — WP4 printing improvements implementation

Handoff: `POST-M14-PM01-WP4-PRINTING-IMPROVEMENTS-04`, starting at `080ae7adafde3f14291e81aeaf74528386d479be` on Draft PR #31. Issue #4 was OPEN and pointed to PR #31 when execution began.

The customer print factory now emits one visible negative `Remise` row only when the committed snapshot says the Retrait discount was applied. It reconstructs the amount from each committed eligible line's extended base, committed options and calculated line total; positive options remain outside the discounted product component. The committed rate is used only to reject inconsistent line arithmetic. An invalid or nonpositive derived amount fails document generation through the existing safe `GenerationFailed` path. A manual total override is not counted as a discount. The row sits with the customer totals and leaves VAT, final total and settled-payment rows in place. No current Catalogue or current discount setting is consulted for its amount.

The kitchen print factory now adds exactly one semantic handwriting-space block after its final `TOTAL`. The WPF renderer measures it as three normal kitchen body-line heights and includes that height in pagination and the physical page. The total and space share a pagination group, so ordinary page boundaries keep them together. The diagnostic text omits the spacer; customer documents do not receive it. Initial, retry, explicit reprint, PREPROD and cancelled outputs use the same factory path.

Synthetic automated regressions cover discount arithmetic and inconsistent snapshots, unpaid/CB/cash/mixed settlement, manual total override, cancelled/PREPROD/DUPLICATA variants, kitchen output variants, exact visual spacer height and long multipage placement. Focused/full Release test and build results, diff check and exact-head CI are recorded in the matching PR `CODEX_DONE`. Computer-A PREPROD owner printing acceptance and the WP1 Production extra-add symptom remain pending; this package does not implement WP5, deploy Production or merge the PR.

## 2026-09-29 — WP5 C02 integration and publication preparation

Handoff: `POST-M14-PM01-WP5-INTEGRATION-PREPROD-C02-05`, starting at controller-accepted WP4 head `08fac6cb0d291fcbb3cd826d99608f36dde5f769` on Draft PR #31. Issue #4 was OPEN and pointed to PR #31 when execution began. WP1 R1's blocked review and R2 correction remain historical; controller acceptance of WP1 implementation does not resolve the owner's original intermittent extra-add symptom without computer-A PREPROD empirical verification. WP2, WP3 and WP4 implementation/automated evidence were separately controller-accepted.

The Batch 01 living status, README and owner checklist now distinguish accepted implementation from pending manual acceptance and identify C02 as the next owner-testable candidate. Existing immutable C01 remains historical. The candidate publisher's residual C01-only error and release-note wording now use its validated C01–C99 candidate identity; a self-test guards that presentation while retaining the one-publish, five-asset, exact-source and immutable-release checks. The approved lightweight dispatch-tag path already accepts an exact source commit from the authorized maintenance branch, so no workflow permission or publication-gate change is required.

C02 candidate source/publication evidence, full Release checks, asset identities and owner NOT RUN status will be appended after verification. No real Production business data, Production deployment/promotion, computer-B PREPROD installation or PR merge is involved.

## 2026-09-29 — WP5 C02 dispatch and publication blocker

The owner explicitly approved C02 tag push/publication. After rechecking Issue #4 OPEN, active Draft PR #31, the unmatched WP5 handoff, absent C02 final tag/release/dispatch tag, and exact source head, the lightweight dispatch tag `m14-preprod-dispatch-c02` was pushed at `0aa3a0282a432da38bff9b35b38ffa03cf3bbada`. GitHub Actions run `36629889943` checked out that exact source. Its `build-candidate` job succeeded: full Release tests, Release build, repository safety scan, pipeline self-test, one application publish, zero installer-side republishes, five synthetic PREPROD installer lifecycle checks, and five generated candidate assets. Artifact `Sushi81-POS-M14-PREPROD-C02-0aa3a02` / ID `11062068062` contains the validated but **unpublished** assets. Payload tree SHA-256 is `006f5db911b71c5ecf7e61ffcaf42b9950d9196b4e19c0f9da49b5bb4b32fb6c`; application archive SHA-256 is `c814e7b9a17fcf31cbf3337e770b8c98327d49704f4b3eabb09d50f8a9edfa07`; PREPROD installer `Sushi81POS-PREPROD-Setup-1.0.1-0aa3a02.exe` SHA-256 is `3ef1346e857914e9898dacafe522c9308d5d740a9db00413c19b9415daa44e4a`. Ordinary exact-source PR CI #984 / run `36627914465` passed.

The `publish-candidate` job failed **before release creation**: `Publish-M14-Candidate.ps1` calls `GET /repos/cimerosef/sushi81-pos/immutable-releases` as a fail-closed preflight, and GitHub returned HTTP 403 `Resource not accessible by integration` to the job's `GITHUB_TOKEN`. That endpoint requires repository Administration read permission, which `GITHUB_TOKEN` cannot request through workflow `permissions`. At this record, C02 release and final `v1.0.1-preprod-c02` tag remain absent. The existing C01 release was not touched. The owner checklist is still NOT RUN and C02 is not installable from a published immutable release. Publication needs an owner-authenticated, administration-read-capable execution path or an explicitly approved equivalent that preserves the preflight; the current handoff forbids manual asset reconstruction/upload outside the approved pipeline. No `CODEX_DONE` is claimed for WP5 while publication remains incomplete.

## 2026-09-29 — WP5 C02 owner-authenticated publication recovery

Handoff: `POST-M14-PM01-WP5-C02-OWNER-AUTH-PUBLISH-RECOVERY-05R1`, from docs-only PR head `7cca1a293ec338960ae7263ca7c18c6082c110b5`. The preceding failed Actions publication remains historical evidence. Before the recovery write, Issue #4 was OPEN and pointed to Draft PR #31; the dispatch tag was a lightweight ref to exact C02 source `0aa3a0282a432da38bff9b35b38ffa03cf3bbada`; the final tag, published Release and owner-authenticated paginated Releases-list C02 draft were absent. Artifact `11062068062` was unexpired and bound to run `36629889943` / that exact source. No later application/runtime commit was substituted.

The official GitHub CLI was obtained from its official immutable release and its ZIP SHA-256 matched the published checksum. The existing `cimerosef` Git Credential Manager login was used only in process memory; no PAT, Actions secret or credential file was created. Owner-authenticated API preflight returned repository admin=true and immutable-releases enabled=true. The exact five files were downloaded into a fresh temporary directory, and local hashes matched the already-built artifact evidence. The package summary, provenance and manifest agreed on C02, version 1.0.1, exact source, 418 payload files and payload tree SHA-256 `006f5db911b71c5ecf7e61ffcaf42b9950d9196b4e19c0f9da49b5bb4b32fb6c`. They recorded one application publish, zero installer-side republishes and no real Production/runtime data. Local forbidden-content scan passed for all five assets. No application build or `dotnet publish` was rerun.

The tracked `Publish-M14-Candidate.ps1` at the exact C02 source performed its usual asset validation, immutable-release preflight, draft-aware single-use guard, five-asset draft upload/verification and publication. GitHub Release ID `399541988` is [immutable C02](https://github.com/cimerosef/sushi81-pos/releases/tag/v1.0.1-preprod-c02): tag `v1.0.1-preprod-c02`, draft=false, prerelease=true, immutable=true, non-latest, direct tag target `0aa3a0282a432da38bff9b35b38ffa03cf3bbada`. Independent GitHub API reads showed the published asset names, byte lengths and SHA-256 digests equal the downloaded files exactly:

| Asset | GitHub asset ID | Bytes | SHA-256 |
| --- | ---: | ---: | --- |
| `application-payload.zip` | `599265273` | 70028491 | `c814e7b9a17fcf31cbf3337e770b8c98327d49704f4b3eabb09d50f8a9edfa07` |
| `package-summary.json` | `599265277` | 2506 | `65c0549979a7f1632e3fce0224a4dffaf4d8783a35fefd45bb4e84b0b9502cea` |
| `payload-manifest.json` | `599265276` | 72773 | `9cb7f8b13480e1b1664062bd1045f1ba759ab09fb65aafc8f6c3bf2ccaa668a6` |
| `release-provenance.json` | `599265278` | 1700 | `cf56025d91bbe4184f878cb92da30ec23084d6524b2c850c211f23c72c17e5ed` |
| `Sushi81POS-PREPROD-Setup-1.0.1-0aa3a02.exe` | `599265272` | 49822179 | `3ef1346e857914e9898dacafe522c9308d5d740a9db00413c19b9415daa44e4a` |

Historical immutable C01 Release `398945249`, its source tag and all five asset identities/digests were re-read and remained unchanged. Owner computer-A PREPROD acceptance remains NOT RUN pending controller's independent C02 verification. No Production deployment, promotion, computer-B PREPROD installation or PR merge occurred.
