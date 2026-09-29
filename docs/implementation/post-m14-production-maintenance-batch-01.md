# Post-M14 Production Maintenance Batch 01 — implementation contract

**Status:** OWNER-AUTHORIZED
**Start baseline:** `71024888a9cfd08109496ad076002be96e708c1a`
**Target environment:** computer A PREPROD first
**Production deployment:** not authorized by this contract

## 1. Mission

Repair one Production order-entry correctness defect and implement three small operator-facing improvements without reopening approved pricing, payment, catalogue identity, printing architecture, authority/recovery or cross-device semantics.

Controlling records:

- `docs/decisions/post-m14-production-maintenance-batch-01.md`;
- `docs/acceptance-criteria-amendment-post-m14-production-maintenance-batch-01.md`;
- existing frozen V1 specs/decisions;
- M14 deployment/promotion rules;
- `AGENTS.md`.

## 2. WP1 — duplicate product-add repair

### Goal

A single operator add gesture creates exactly one cart addition, including when products are added sequentially and quickly.

### Required investigation

Before changing behavior, identify a reproducible event/state path through the real WPF `MainWindow` Caisse flow.

Inspect at minimum:

- `orderProductsGrid` MouseDoubleClick routing;
- `orderAddButton` Click routing;
- `OnAddOrderProduct`;
- `orderProductAddInProgress`;
- `SelectedProduct` changes;
- asynchronous `AddSelectedProductAsync` / `PendingProduct`;
- option/no-option branches;
- any Hiboutik cart/session refresh interaction that can append lines.

Do not assume the first suspected cause without a failing regression.

### Required behavior

- button add A once -> exactly one A addition;
- then button add B once -> cart delta exactly one B, no extra A;
- grid double-click A once -> exactly one A addition;
- then grid double-click B once -> exactly one B, no extra A;
- fast sequential deliberate adds across different products -> one addition per deliberate gesture;
- separate deliberate adds of the same product remain legitimate separate actions under current semantics;
- option-enabled path must not double-add after dialog confirmation;
- cancelled option dialog adds nothing;
- Hiboutik presentation refresh must not duplicate an ordinary manually added line.

### Repair constraints

Fix the root event/reentrancy/state defect. Do not:

- deduplicate the cart by product identity;
- merge lines merely to hide duplicates;
- drop legitimate repeated adds;
- use a broad arbitrary delay as the correctness mechanism;
- change quantity/pricing/discount behavior.

Add deterministic regression tests, preferably through the real WPF event path where the defect exists.

### WP1 stop point

After implementation:

- run focused tests;
- run full Release solution tests;
- Release build must be 0 warnings / 0 errors;
- `git diff --check`;
- obtain exact-head CI when available;
- update the worklog;
- publish matching `CODEX_DONE`;
- stop for controller review.

WP2 is not auto-authorized by completion of WP1; controller publishes the next handoff.

## 3. WP2 — cart auto-reveal

After a true `Cart` insertion, bring the inserted `OrderEntryCartLineViewModel` into view in `orderCartList`.

Prefer a small WPF presentation seam/event driven by successful collection insertion or the completed add path. Preserve MVVM boundaries where practical.

Tests must prove with an overflowed ListBox that:

- the newest line is outside the viewport before the insertion/scroll behavior is exercised;
- after insertion the new line is realized/visible;
- quantity change/edit/repricing does not forcibly jump to bottom.

## 4. WP3 — natural product-code ordering

Introduce one reusable deterministic natural code comparer/ordering strategy.

Numeric runs compare by numeric value; non-numeric runs compare case-insensitively. Define deterministic tie-breaking for equivalent numeric/case forms.

Apply it consistently to operator-facing current product lists. Avoid SQL tricks that only work for one prefix or fixed digit count.

Tests cover mixed examples including `R1/R2/R9/R10/R11`, `ML1/ML2/ML9/ML10`, `R4/R4a/R4b/R4c/R5`, mixed case and deterministic ties.

Preserve search/filter/category behavior and product identity.

## 5. WP4 — customer-ticket Retrait discount display

When `PickupDiscountApplied` is true, compute the actual discount from committed sale-time line snapshots:

- start from committed line base plus committed adjustments according to existing pricing semantics;
- compare with committed calculated line total;
- aggregate the actual applied discount;
- do not load current Catalogue/settings to reconstruct the amount.

Render one customer-facing negative discount line in the customer receipt between merchandise/tax detail and final total at a clear conventional position. Exact block kind/internal rendering shape is a technical choice if layout remains stable.

Required tests:

- unpaid applied discount -> visible exact negative amount;
- settled applied discount -> same discount plus existing truthful payment rows;
- explicit reprint -> same discount plus `DUPLICATA`;
- no applied discount -> no discount row;
- changed current Catalogue/settings after commit cannot change historical printed discount;
- VAT/Total/payment snapshots remain unchanged;
- kitchen ticket remains unchanged except existing PREPROD/reprint/cancel markers.

## 6. WP5 — integration, docs, candidate

After WP1–WP4 controller acceptance:

- run full Release suite and Release build;
- retain all M14 environment/promotion regressions;
- reconcile living docs, including stale pre-merge M14 status;
- produce a new immutable PREPROD candidate using the M14 candidate pipeline;
- do not overwrite the accepted historical M14 C01;
- use a new candidate identity;
- owner tests only computer A PREPROD using the dedicated final checklist;
- after owner PASS, production promotion/deployment/merge remain separate owner decisions.

## 7. Global hard restrictions

No work in this batch may:

- use real production customer/order/payment data in Git/tests/artifacts;
- mutate Production data for automated testing;
- alter pairing/authority/handoff/DR semantics;
- weaken PREPROD/Production isolation;
- resolve M12 deferred real archive verification by assumption;
- mark M14 two-PC PREPROD verification Passed;
- merge the PR;
- deploy to Production.
