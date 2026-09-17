# Post-M09 enhancement — Hiboutik daily CB/Espèce dashboard — implementation contract

**Status:** Approved / implementation authorized  
**Date:** 2026-09-16  
**Issue:** #18  
**Branch:** `codex/post-m09-hiboutik-daily-payment-dashboard`  
**Milestone placement:** independent post-M09 enhancement; M10 remains separate and unauthorized

## 1. Authority and baseline

M09 is Passed and merged through PR #17 at merge commit `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`.

The project owner explicitly authorized this enhancement on 2026-09-16 after approving the business rule that Cancelled Hiboutik orders are excluded.

Controlling sources for this enhancement are:

- `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`;
- this implementation contract;
- inherited payment/source/lifecycle/authority rules in `product-requirements.md`, `order-lifecycle.md`, `data-model.md`, `architecture.md`, `storage-strategy.md` and the M09 amendment.

If a real contradiction is discovered, stop and report it. Do not invent a new business rule.

## 2. Objective

Add exactly two read-only current-business-date values to the existing top Caisse dashboard:

- `Hiboutik CB aujourd'hui`;
- `Hiboutik Espèce aujourd'hui`.

The implementation must reuse existing persisted order/payment facts and the existing dashboard refresh path. It must not create a second Hiboutik workflow.

## 3. Exact reporting semantics

For business date D:

### Hiboutik CB

Sum `PaymentAdjustment.delta_amount` where all are true:

- parent `Order.source_type = HIBOUTIK_PASTE`;
- parent current status is not `CANCELLED`;
- adjustment effective business date = D;
- adjustment bucket = `CB`.

### Hiboutik Espèce

Same rules, with bucket = `ESPECE`.

Use signed deltas. Corrections/reductions are negative and must reduce the selected date's value.

Do not use:

- `Order.total_ttc`;
- `source_total_ttc`;
- current cumulative card/cash values as the daily amount;
- `recorded_at` as business-date attribution;
- order creation date;
- planned fulfilment date.

Open and Closed non-Cancelled Hiboutik orders are included. Cancelled Hiboutik orders are excluded without deleting their retained adjustment rows.

## 4. Existing POS dashboard invariants

The existing values must remain byte-for-byte/business-semantically equivalent:

- operational turnover is POS-originated only;
- ordinary received total is POS-originated only;
- ordinary received CB is POS-originated only;
- ordinary received Espèce is POS-originated only;
- future/due/overdue counts retain their current approved semantics.

Adding a Hiboutik adjustment must not change ordinary POS turnover/received values.

The enhancement adds no combined Hiboutik received-total value and no Hiboutik turnover/order-count/difference metric.

## 5. Expected implementation seam

Prefer the smallest conforming extension of the existing M05 summary path.

Current implementation seam on the M09-merged baseline:

- `OrderOperationalSummary` in `src/Sushi81.Pos.Application/OrderEntry/OrderLifecycleContracts.cs`;
- `IOrderLifecycleStore.GetOperationalSummaryAsync(DateOnly, ...)`;
- `OrderLifecycleService.GetOperationalSummaryAsync(...)`;
- `SqliteOrderStore.GetOperationalSummaryAsync(...)` and its existing received-payment SQL helper;
- `OrderLifecycleShellViewModel` dashboard projection/refresh;
- top Caisse `WrapPanel` in `MainWindow.xaml`;
- existing FR / zh-CN localization resources.

A conforming implementation should normally:

1. extend `OrderOperationalSummary` with two Money values for Hiboutik card/cash daily deltas;
2. keep the same summary method signature;
3. add/read one focused SQLite aggregation for `HIBOUTIK_PASTE`, `status <> CANCELLED`, selected effective business date, grouped by CB/Espèce;
4. preserve the existing POS aggregation unchanged;
5. expose two formatted read-only view-model properties;
6. raise those properties on the existing dashboard refresh;
7. add two compact passive labels/values to the existing top Caisse dashboard;
8. add FR and Simplified Chinese localization keys/resources;
9. update every zero/default/test construction of `OrderOperationalSummary` safely.

Internal helper names/query organization are technical choices. Do not add a new table, migration or write service.

## 6. UI requirements

The new values are passive text in the existing top Caisse dashboard, not buttons.

French labels:

- `Hiboutik CB aujourd'hui`;
- `Hiboutik Espèce aujourd'hui`.

Simplified Chinese labels should preserve the familiar CB term and be operationally clear, for example:

- `Hiboutik 今日 CB`;
- `Hiboutik 今日现金`.

Use the existing dashboard numeric formatting/culture behavior. Do not add a separate currency/input control.

The values must refresh whenever the existing dashboard summary refreshes. Do not introduce a second timer/refresh service.

The existing non-authoritative/stale warning is sufficient. Do not add a Hiboutik-specific authority warning.

## 7. Specification/status reconciliation before production edits

Before changing production code, make one documentation/status reconciliation commit on this branch. It must:

- record M09 as Passed/Merged through PR #17 / merge `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5` in living current-state docs;
- record this post-M09 enhancement as explicitly authorized/in progress and M10+ as still unauthorized;
- register the 2026-09-16 post-freeze amendment in `v1-specification-freeze.md`;
- align `product-requirements.md` by adding a narrow requirement for exactly these two passive Hiboutik daily values and clarifying that the no-emergency-dashboard rule still forbids counters/discrepancy/special workflow, not these two approved values;
- align `acceptance-criteria.md` to reference the new amendment / AC-HIB-010;
- align `order-lifecycle.md` to distinguish the unchanged ordinary POS daily summary from the separate Hiboutik daily CB/Espèce reference values;
- align `data-model.md` derived reporting rules so the same persisted facts support the separate source-specific daily values without schema expansion;
- update root/docs README current-state wording as needed.

Do not rewrite historical evidence. Do not start M10/M11.

Only after that documentation reconciliation commit may production implementation begin.

## 8. Required automated evidence

Add focused automated evidence covering at least all of the following.

### SQLite/reporting

1. A non-Cancelled Hiboutik order with same-day +CB contributes to Hiboutik CB only.
2. A non-Cancelled Hiboutik order with same-day +Espèce contributes to Hiboutik Espèce only.
3. Mixed CB/Espèce adjustments are separated correctly.
4. POS adjustments never enter either Hiboutik value.
5. Hiboutik adjustments never change existing POS `ReceivedTtc`, `ReceivedCardTtc`, `ReceivedCashTtc` or turnover.
6. Cancelled Hiboutik orders contribute zero to both new values while their `PaymentAdjustment` rows remain in the database.
7. Open and Closed non-Cancelled Hiboutik orders are both included.
8. Back-dated effective business date controls attribution; later `recorded_at` does not move the amount.
9. A signed negative correction reduces the target date value exactly.
10. A different effective date does not appear in today's values.
11. Zero/no matching rows returns exact zero values.

### Application/presentation

12. `OrderOperationalSummary` carries the two values end-to-end through `OrderLifecycleService`.
13. In-memory/no-lifecycle-store fallback returns zero for both new values.
14. `OrderLifecycleShellViewModel.RefreshDashboardAsync` updates both formatted properties together with the existing dashboard.
15. Existing dashboard fields/counts continue to project their old values.

### WPF/localization

16. Main Caisse contains exactly two new passive Hiboutik payment value bindings and no new button/action/drill-down.
17. FR and zh-CN localization resources contain both new labels and existing localization parity tests remain green.
18. Language switching changes only labels/formatting, not source/payment amounts.
19. Read-only/non-authoritative presentation does not introduce or enable a business write path.

Use synthetic test data only.

## 9. Regression and verification

Before completion run:

- focused new reporting/application/presentation tests;
- relevant existing M05 dashboard/payment tests;
- relevant M09 anti-double-counting/source tests;
- full `Sushi81.Pos.sln` Release test suite;
- Release build with warnings/errors reported;
- `git diff --check`;
- self-contained `win-x64` Desktop publish using the established project procedure.

No new package/dependency is expected. If one appears necessary, stop and report before adding it.

## 10. Owner manual acceptance candidate

Prepare one exact self-contained `win-x64` candidate after all accepted implementation/test/docs commits are pushed.

Record:

- exact candidate head SHA;
- executable path/size/SHA-256;
- ZIP path/size/SHA-256;
- exact-head CI result;
- full Release test/build totals;
- privacy/safety audit confirming no real order/payment/customer data or secrets are committed/published.

The owner manual checklist is `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-manual-acceptance.md`.

Codex must not check owner-observation boxes or declare final owner acceptance.

## 11. Explicit exclusions

Do not implement:

- Hiboutik turnover;
- Hiboutik total-received combined value;
- Hiboutik order count;
- source-vs-POS discrepancy/difference;
- Hiboutik status/lifecycle/reconciliation screen;
- payment-event ledger UI;
- Hiboutik API/email/clipboard automation;
- automatic export changes;
- schema migration/new persistent field;
- M10 Catalogue `.xlsx`;
- M11 Gestion export;
- M12/M13;
- merge.

Do not change the accepted M09 paste/import behavior except where compilation mechanically requires summary-record constructor updates with no behavioral effect.

## 12. Completion protocol

Push all work to the dedicated branch/PR and leave one top-level PR Conversation comment beginning exactly with the authorized handoff ID.

Completion must report:

- pushed head SHA;
- docs-first reconciliation commit SHA;
- implementation files/components changed;
- confirmation of no schema/migration/dependency/new write path;
- focused tests;
- full Release tests/build;
- exact-head CI;
- self-contained candidate hashes/sizes;
- privacy/safety result;
- Issue #4 state observed;
- blockers/unresolved findings, or explicitly none;
- execution topology required by the agent contract;
- `browserNotification: succeeded|unavailable|failed`.

Then stop. Do not merge and do not start M10/M11 or any additional enhancement.