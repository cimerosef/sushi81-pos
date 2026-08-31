# M04 — First complete order-entry vertical slice

**Status:** Approved implementation contract  
**Approved by:** project owner  
**Approval date:** 2026-08-31  
**Phase:** 6 — Implementation  
**Milestone:** M04  
**Approved starting baseline:** `main` after M03 merge commit `57f89cac0672d6d98dda7dc3c9a8ba7b3434e292`, plus the approved M04 preparation documentation commits made before branch creation  
**Codex execution authorization:** granted only through a matching durable `CODEX_HANDOFF_READY` record on the active M04 PR while issue #4 is OPEN  
**POST_TASK_POWER_ACTION:** `NONE`

## 1. Mission

M04 implements the first complete production order-entry vertical slice of Sushi81 POS.

The milestone starts from the merged M03 Catalogue / BusinessSettings / SQLite / WPF foundation and delivers one complete path:

**browse current catalogue → configure product/options → edit cart → price/validate → confirm → commit complete order snapshots → reload committed order → dispatch committed state through a deterministic test print boundary**

M04 establishes the first durable `Order` business records.

M04 does **not** implement the later payment, close/reopen/cancel, dashboard, search, Windows printing, Hiboutik import, Excel/export, recovery/handoff or archive milestones.

GitHub current Approved sources are authoritative. Legacy Excel/VBA behavior and chat memory are not specification sources.

## 2. Mandatory source review before coding

Before modifying production code, the Codex main agent must re-read current branch/main authority including at minimum:

- `AGENTS.md`
- root `README.md`
- `docs/README.md`
- `docs/v1-specification-freeze.md`
- `docs/acceptance-criteria.md`
- `docs/implementation-plan.md`
- `docs/implementation-status.md`
- `docs/product-requirements.md`
- `docs/order-lifecycle.md`
- `docs/business-rules.md`
- `docs/catalogue-management.md`
- `docs/data-model.md`
- `docs/architecture.md`
- `docs/storage-strategy.md`
- `docs/printing.md`
- all Approved files under `docs/decisions/`, including `m04-order-entry-pricing-clarifications.md`
- `docs/implementation/agent-execution-contract.md`
- `docs/implementation/interactive-quality-gate.md`
- `docs/implementation/control-state-preservation.md`
- `docs/implementation/post-task-power-policy.md`
- `docs/implementation/milestone-03-catalogue-settings.md`
- `docs/implementation/milestone-03-filtered-bulk-activation-extension.md`
- `docs/implementation/milestone-03-worklog.md`
- `src/README.md`
- `tests/README.md`

If an Approved source conflicts materially with another Approved source on pricing, VAT, order semantics, lifecycle, persisted business meaning or user workflow, stop that affected path and report the conflict. Do not guess.

## 3. Approved M04 clarification amendment

`docs/decisions/m04-order-entry-pricing-clarifications.md` is authoritative and must be implemented exactly.

### A1 — quantity multiplies adjustments

Configured option adjustments and operator-entered custom adjustments are per ordered unit. For quantity `Q`, Product base and every predefined/custom adjustment are multiplied by `Q`. One line therefore represents multiple units with the same configuration. Different configurations remain separate lines.

`OrderItemAdjustmentSnapshot.adjustment_ttc_snapshot` is the sale-time **per-unit** signed adjustment. Extended adjustment = snapshot × line quantity.

### B1 — Retrait discount defaults OFF

Selecting `Retrait` does not silently request the ordinary pickup discount. New Retrait orders begin with ordinary discount OFF. The operator explicitly enables it. Existing eligibility/threshold/no-force rules then determine whether it is actually applied.

### C1 — line/component-first rounding

Ordinary Retrait discount uses deterministic line/component-first calculation and half-up cent rounding. Do not implement aggregate order-level discount followed by arbitrary cent allocation.

## 4. Explicit M04 scope

M04 must implement all of the following.

### 4.1 Catalogue order-entry selection

Provide a production order-entry catalogue experience supporting:

- active current Products only;
- category browsing/filtering;
- code/name search;
- explicit Add action;
- double-click Add action;
- deterministic current Product display.

Inactive Products are not ordinary new-order choices. Existing M03 Catalogue maintenance remains the current-catalogue authority.

### 4.2 Product option workflow

For a Product whose `options_enabled` is false, adding it does not require structured option selection.

For a Product with options enabled, use the ordinary option-selection workflow and enforce:

- required SINGLE = exactly one active Option;
- optional SINGLE = zero or one active Option;
- required MULTI = selection count within configured min/max with required minimum at least one;
- optional MULTI = selection count within configured min/max, minimum may be zero;
- inactive Options are unavailable for new orders and cannot remain valid merely because a stale UI object references them.

Invalid option configuration blocks the affected line/order from valid confirmation.

No Hiboutik-specific option UI is created. This ordinary workflow must remain reusable by M09.

### 4.3 Custom adjustment

Every cart line supports operator-entered custom adjustments.

A custom adjustment:

- may be positive, negative or exactly €0.00;
- uses cent precision;
- has no additional arbitrary V1 min/max cap;
- requires a non-empty trimmed description;
- affects that order line only;
- never modifies Catalogue Product base price;
- follows A1 per-unit quantity semantics;
- follows the same sign-based VAT rule as monetary Product options.

Before confirmation custom adjustments can be added, edited and removed.

### 4.4 Cart editing

Before confirmation the operator can:

- add Products;
- remove an existing cart line;
- directly change line quantity;
- open an already-added line and modify its option selections;
- add/edit/remove custom line adjustments.

Changing quantity must not require deleting/recreating the line. Editing the line restores its current configuration. Quantity is a positive whole number.

Do not automatically merge independently added lines. Separate lines remain separate so otherwise-identical Products may retain different configurations.

### 4.5 Order information

New order entry includes:

- fulfilment mode;
- planned fulfilment date;
- required planned fulfilment time for new POS confirmation;
- optional telephone;
- optional delivery address;
- optional free-text comment.

Fulfilment mode is initially unselected. Confirmation requires exactly one of `RETRAIT` or `LIVRAISON`.

Planned fulfilment date and time are required for new POS confirmation; the UI may initialize the date from `IBusinessClock.BusinessDate`, but must start a new order with no selected time until the operator chooses an approved slot. The selectable time is exactly hours `11, 12, 13, 14, 18, 19, 20, 21, 22` and minutes `00, 05, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55`. The persisted time field remains nullable only so historical snapshots with no time remain readable.

Telephone is optional for both modes. Delivery address is optional even for initial Livraison confirmation. Do not introduce mandatory telephone/address validation absent from Approved sources.

### 4.6 Telephone normalization

Provide one shared telephone presentation-normalization function. At minimum `0612345678` saves/displays as `06 12 34 56 78`.

Already-spaced standard French ten-digit input should normalize to the same result where practical. Telephone remains ordinary text, not a Customer entity. Unrecognized text must not become a confirmation blocker merely because it cannot be normalized. Never invent digits.

### 4.7 Future-order foundation

At confirmation:

- planned fulfilment date later than `IBusinessClock.BusinessDate` → `advance_order_marker = true`;
- same-day ordinary order → marker false.

Persist the marker. Do not implement future/due-today/overdue dashboard views in M04.

## 5. Pricing engine

Pricing must be implemented in a testable Domain/Application seam with no WPF or SQLite dependency.

Persisted business money uses `Money` / integer cents. Percentage arithmetic uses `decimal`. Do not use `double`, `float` or SQLite `REAL` for business money/percentage.

### 5.1 Normal line calculation

For quantity `Q`:

- extended Product base = base unit TTC × `Q`;
- every extended line adjustment = per-unit adjustment × `Q`.

A zero adjustment has no monetary effect but its label may remain snapshotted.

### 5.2 Adjustment VAT

Normal treatment:

- Product base → Product VAT;
- positive predefined/custom adjustment → 5.5%;
- negative predefined/custom adjustment → associated Product VAT;
- zero adjustment → no monetary tax contribution.

The operator never selects adjustment VAT.

### 5.3 Retrait without requested discount

For Retrait with discount OFF:

- no pickup discount;
- no delivery fee;
- ordinary Product/adjustment pricing applies;
- persist `pickup_discount_applied = false` and null rate snapshot.

### 5.4 Retrait with requested discount

For every discount-eligible line:

- positive adjustments remain undiscounted;
- negative adjustments reduce the Product-VAT discountable component before discount;
- apply current configured `pickup_discount_rate`;
- use C1 line/component-first half-up cent rounding.

Non-eligible Product lines are not discounted.

After building the candidate discounted order total:

- candidate below current `pickup_discount_min_total_ttc` → ordinary discount is not applied and normal undiscounted total is used;
- candidate exactly equal → discount allowed;
- candidate above → discount allowed.

No force-apply override exists.

When actually applied, persist `pickup_discount_applied = true` and the rate actually used. If threshold rejects it, persist false/null and show clear operator feedback rather than silently changing the requested outcome.

### 5.5 Livraison minimum

Livraison receives no ordinary Retrait discount.

Calculate merchandise/commercial amount from Product lines plus their line adjustments before delivery fee.

Require:

`commercial amount >= current delivery_min_merchandise_total_ttc`.

Below minimum blocks confirmation. Delivery fee cannot help qualify. No ordinary force override exists. Exact €30 qualifies when setting is €30.

### 5.6 Delivery fee

After minimum passes, if fee is enabled and non-zero, add configured fixed fee once at order level.

Fee:

- is not multiplied by line count;
- is not included in minimum qualification;
- uses fixed 10% VAT;
- persists as `delivery_fee_ttc_snapshot`.

If disabled or zero, snapshot = €0.00. Do not add an editable delivery-fee VAT control.

### 5.7 Normal tax snapshot

When manual override is false, build final TTC tax buckets from final calculated components and aggregate by canonical VAT rate:

- Product/negative adjustment components at Product VAT;
- positive adjustments at 5.5%;
- delivery fee at 10%.

For each TTC bucket:

`included VAT = TTC × rate / (100 + rate)`

Round final VAT amount to cents using shared half-up behavior. Persist VAT rate, taxable TTC and VAT amount.

### 5.8 Authoritative total and manual override

There is one authoritative total field. Normal pricing writes it. Operator may directly overwrite it without changing Catalogue prices and without a mandatory reason.

Manual edit:

- becomes `Order.total_ttc`;
- sets `manual_total_override_active = true`;
- may be above or below calculated total;
- does not require lines to mathematically equal the override while active.

Do not add undocumented surcharge/refund workflows.

### 5.9 Manual-total VAT

While override is active:

- normal mixed tax rows are not the final authoritative tax snapshot;
- persist exactly one tax row;
- VAT rate = 10%;
- taxable TTC = authoritative total;
- included VAT calculated at 10% with shared half-up rounding.

Normal calculated line snapshots remain persisted.

### 5.10 Clearing manual override

Any later **price-affecting** draft change must recalculate normal pricing, replace the manual total, set override false and restore normal mixed VAT.

At minimum price-affecting changes include:

- add/remove Product;
- quantity change;
- structured option change;
- custom adjustment add/edit/remove;
- Retrait discount request change;
- fulfilment-mode change when pricing applicability changes;
- applicable delivery-fee result.

Non-price-affecting edits such as telephone, address, planned date/time or comment must not clear the override merely because text changed.

## 6. Application contracts

Keep current four-layer architecture:

1. `Sushi81.Pos.Domain`
2. `Sushi81.Pos.Application`
3. `Sushi81.Pos.Infrastructure`
4. `Sushi81.Pos.Desktop`

Do not add a framework merely for M04. Do not create generic repository abstractions.

### 6.1 Order-entry Catalogue query

Provide a narrow Application query seam for order entry supporting:

- category list;
- active Product list with code/name/category/price/VAT/discount/options flags;
- code/name search;
- category filter;
- load current active Product option configuration for adding/editing a draft line.

Reuse existing M03 current Catalogue authority. Do not create a second current Catalogue source.

### 6.2 Pricing service

Provide a deterministic pricing service accepting current cart lines/configurations, fulfilment mode, transient Retrait-discount request and current `BusinessSettings`.

Return a complete result including calculated lines, total before manual override, discount applied/not applied, rate snapshot, delivery commercial amount, applied fee, tax buckets and validation result.

WPF must not duplicate formulas. SQLite store must not independently recalculate different formulas.

### 6.3 New-order confirmation service

Provide one Application-owned operation equivalent to `ConfirmNewOrderAsync(NewOrderDraft, ...)`.

Responsibilities:

1. reject invalid draft;
2. require fulfilment mode;
3. validate option selections;
4. validate quantity;
5. load/use current approved BusinessSettings;
6. calculate/revalidate pricing;
7. validate Livraison minimum;
8. normalize applicable sale-time text;
9. allocate stable Order/line/adjustment/tax IDs through `IIdGenerator`;
10. build complete immutable persistence snapshot;
11. set status `OPEN`;
12. set source type `POS`;
13. set advance-order marker;
14. commit everything atomically through existing transaction seam;
15. complete DB transaction;
16. load/use the successfully committed representation;
17. only then cross print-dispatch boundary.

The print boundary must never execute inside the DB transaction.

### 6.4 Order read/reload

Provide exact stable-ID lookup (`GetOrderByIdAsync` or equivalent).

Loaded representation must come from persisted Order/OrderItem/Adjustment/Tax snapshots and must not reconstruct historical business values from current Catalogue.

M04 may expose only the minimum exact-ID committed-order reload needed for verification. Do not implement M05 live telephone/comment/order search.

Loaded confirmed order is read-only with respect to post-confirmation business modification in M04. Same-ID saved modification belongs to M05.

## 7. Domain model

Add explicit M04 controlled values and records without introducing speculative aggregate frameworks.

At minimum:

- `OrderStatus`: Open / Closed / Cancelled;
- `FulfilmentMode`: Retrait / Livraison;
- `OrderSourceType`: Pos / HiboutikPaste;
- adjustment kind: PredefinedOption / CustomAdjustment.

M04 production creation writes only Pos. Do not expose HiboutikPaste in M04 UI.

Order must represent all frozen logical fields needed later, with new M04 confirmation using status Open, null closed timestamp and null cancelled timestamp.

Persist OrderItem snapshots with stable line ID/position, optional non-authoritative Product source ID, Product code/name/category/base price/VAT/discount-eligibility snapshots, quantity, extended base total and final normal calculated line total before order-level manual override.

Persist adjustment snapshots with stable ID/order, kind, optional non-authoritative Option source ID, applicable group label, required label, per-unit signed amount and VAT-rate snapshot when monetary.

Persist final authoritative OrderTaxBreakdown. Current Product/Option records must not be authoritative dependencies of historical rows.

## 8. SQLite migration 3

Add production migration version **3**. Keep migrations 1 and 2 unchanged. Preserve all M03 Catalogue/BusinessSettings rows. Migration failure must never reset/recreate live data.

Migration 3 creates at least:

- `orders`
- `order_items`
- `order_item_adjustments`
- `order_tax_breakdown`

Do not expose PaymentAdjustment/payment behavior in M04.

### 8.1 `orders`

Physical schema must equivalently store:

- stable text primary key;
- source type;
- status;
- created/updated timestamps;
- nullable closed/cancelled timestamps;
- fulfilment mode;
- planned date and nullable historical-compatible time;
- advance marker;
- nullable telephone/address/comment;
- total TTC cents;
- manual override boolean;
- pickup discount applied boolean;
- nullable exact decimal pickup rate snapshot;
- delivery fee cents.

Use controlled CHECK constraints where practical. No SQLite REAL for business money/percentage. Exact rate representation must round-trip `decimal`.

### 8.2 `order_items`

Store stable line identity, parent Order, deterministic line position, nullable non-authoritative source Product ID, all Product/category/pricing/VAT/discount snapshots, positive whole quantity, extended base total and calculated line total.

Do not create a current-Catalogue FK that prevents Product deletion or rewrites history. Add deterministic uniqueness for `(order_id, line_position)`.

### 8.3 `order_item_adjustments`

Store stable snapshot ID, parent line FK, deterministic display order, kind, nullable non-authoritative source Option ID, nullable group name, required label, signed **per-unit** adjustment cents and nullable canonical VAT-rate representation.

Do not enforce current Option FK authority.

### 8.4 `order_tax_breakdown`

Store stable row ID, parent Order, canonical VAT rate, taxable TTC cents and VAT amount cents.

Application/domain invariant: normal mode one row per effective rate; manual total mode exactly one 10% row.

## 9. Transactional persistence

One successful new-order confirmation transaction includes:

- Order;
- all OrderItems;
- all adjustment snapshots;
- complete authoritative tax breakdown.

If any write/commit fails, no partial Order aggregate remains and print dispatcher is never called.

Use existing `ITransactionRunner` / `IApplicationTransaction`. Do not create WPF-owned transactions and do not keep a write transaction open while showing UI or dispatching output.

## 10. Historical independence

Regression must prove a confirmed Order remains unchanged after subsequent current Catalogue mutation/deactivation/deletion.

After confirming an Order, modify current Product code/name/category/price/VAT/discount eligibility, rename Category, change/deactivate/delete Options where current M03 rules permit, deactivate Product and permanently delete Product. Reload the Order.

Reloaded historical data must retain saved Product code/name/category/base price/VAT/discount eligibility, option labels, adjustment amounts/VAT, line totals, order total and tax breakdown.

No current Catalogue lookup may rewrite these values. This closes the historical-independence regression deferred from M03 AC-CAT-003.

## 11. Print-dispatch boundary

M04 does **not** implement final Windows printing.

Create the minimum Application-owned seam proving:

**commit first → dispatch second**

Dispatch input comes from successfully committed Order state, not mutable draft state.

Use deterministic fake/test sink capable of recording call status, stable Order ID, committed snapshot, call ordering relative to commit, and injected failure.

Do not add production `PrintQueue`, printer configuration UI, final ticket layouts, reprints, `RÉIMPRESSION`, `DUPLICATA` or cancelled-ticket behavior. Those belong to M08.

If dispatch fails after commit:

- Order remains committed;
- stable ID remains reloadable;
- UI must distinguish persistence success from output failure;
- do not create a second Order automatically.

A restart after this point must reload the Order.

## 12. WPF order-entry workflow

Add a production Order Entry / Caisse destination while preserving M03 Catalogue and Settings.

Follow `interactive-quality-gate.md` and `control-state-preservation.md`.

### 12.1 Main screen

Provide clearly separated practical areas equivalent to Catalogue browse/search, cart, order information/fulfilment, pricing/total and confirmation. Exact layout is technical but must remain readable in French and zh-CN at supported sizes.

### 12.2 Product browse

Support category filter, code/name search, double-click add, explicit Add and active Products only. UI language changes must not translate Catalogue business data.

### 12.3 Option editor

Show groups/options in configured order; visually express SINGLE/MULTI and required/optional/min/max; block invalid completion; optional groups may remain empty. Editing an existing cart line prepopulates selections/custom adjustments. Cancel leaves original line unchanged.

### 12.4 Cart

Display enough information to distinguish configured lines: Product code/name, quantity, selected option/custom labels and calculated line TTC. Provide direct quantity controls. Quantity changes preserve configuration and recalculate through shared pricing.

### 12.5 Discount control

Retrait discount defaults OFF and requires explicit operator action. No force apply. Threshold rejection must be clear. Livraison never receives ordinary Retrait discount.

### 12.6 Authoritative total

Display one editable authoritative total. Direct edit activates manual override. A later price-affecting action automatically returns to normal calculated value. Do not require a permanent second calculated-total field.

### 12.7 Confirmation

Confirmation must be blocked for invalid draft, protected from accidental duplicate execution while already in progress, show stable committed ID after success and not make post-commit output failure look like Order loss.

After success, transition to a clearly committed state in which repeated clicking cannot accidentally create a duplicate of the same intended confirmation. Starting another order must be explicit.

### 12.8 Reload

Expose only minimum practical exact-ID committed-order reload. Do not implement general live search or M05 same-ID edit/save.

## 13. Control-state preservation

Every interactive action mutates only intended state. Automated presentation/WPF tests must cover important adjacent-state invariants, including:

- quantity edit preserves options;
- option edit preserves quantity;
- custom adjustment does not reset fulfilment;
- telephone/comment edit does not reset discount request;
- non-price text edit does not clear manual override;
- language switch does not change cart/prices/fulfilment/discount/total;
- option-editor Cancel does not mutate original line;
- Catalogue filtering does not mutate cart;
- adding Product does not modify BusinessSettings.

## 14. Explicit exclusions

M04 must not expose or implement production business features for:

- CB/Espèce entry or cumulative payment composition;
- PaymentAdjustment workflow/effective dates;
- Close/reopen/Cancel lifecycle;
- saved same-ID Order modification or abandon-edit workflow;
- new order from prior customer reuse;
- telephone/comment historical search;
- operational turnover/received-payment/future/due-today/overdue dashboards;
- final Windows printer adapter/settings/layout/reprints;
- Hiboutik paste importer;
- Catalogue `.xlsx` import/export;
- Gestion export;
- M06 recovery enforcement integration;
- M07 handoff/DR UI;
- archive;
- installer.

A minimal technical seam for a later milestone is allowed only when necessary for correct M04 architecture and must not expose later business behavior.

## 15. Failure-path/data-safety strategy

M04 must explicitly test failures.

### 15.1 Validation/option failure

Invalid draft/required/min/max/inactive selection → no Order commit, no dispatch, clear localized validation.

### 15.2 Stale current Catalogue

If a selected current Product/Option becomes invalid before confirmation, do not silently substitute another record or persist a stale inactive choice as a new sale. Fail safely and preserve draft for correction where practical.

### 15.3 Livraison minimum

Below minimum blocks confirmation before fee. Manual total override is not an ordinary bypass of the merchandise minimum. No DB changes and no dispatch.

### 15.4 Transaction failure

Inject representative failures at Order/item/adjustment/tax insert and commit. Every failure must roll back the complete aggregate.

### 15.5 Dispatch failure

Inject failure only after successful commit. Order survives and exact-ID reload succeeds. Persistence success and dispatch failure remain distinguishable.

### 15.6 Migration failure

Starting from populated synthetic schema v2, prove failed v3 migration does not reset/reseed and successful retry remains safe.

### 15.7 Restart

After successful commit (including simulated dispatch failure), new process/store instance must reload identical stable-ID snapshots.

## 16. Automated test matrix

Use synthetic data only. Never use real customer/order/payment data.

### 16.1 Domain/pricing

Cover at least:

- A1 Product €10 + option €1 × qty 2 = €22 before other rules;
- negative/custom adjustment quantity multiplication;
- SINGLE required/optional boundaries;
- MULTI min/max/optional boundaries;
- inactive Option invalid for new selection;
- custom positive/negative/zero and non-empty label;
- Catalogue base price unchanged by custom adjustment;
- B1 default discount OFF;
- eligible/non-eligible Retrait lines;
- positive option undiscounted;
- negative option reduces discountable component;
- threshold just below/exact/above;
- changed Settings rate/minimum consumed without recompilation;
- C1 half-cent and multi-line one-cent-difference regression;
- Livraison €29.99 fail / €30.00 pass;
- fee disabled/zero/enabled;
- fee cannot qualify under-minimum order;
- line adjustments contribute to commercial amount;
- Product/5.5/10/mixed VAT and included-VAT half-up boundaries;
- manual total above/below calculated;
- exactly one 10% manual tax row;
- line calculated snapshots remain;
- each price-affecting change clears override;
- non-price text/date edit preserves override.

### 16.2 Application

Cover active Product listing; inactive Product exclusion; code/name search; category filter; option workflow load; inactive option exclusion; explicit fulfilment requirement; optional telephone/address; phone normalization; source type POS; status Open; advance marker; stable ID; complete snapshot creation; current BusinessSettings consumption; dispatch only after commit; no dispatch on validation failure; duplicate-confirm guard.

### 16.3 Infrastructure integration

Cover migration 1→2→3; populated 2→3; current-version startup; M03 data preservation; complete aggregate commit; deterministic line/adjustment order; exact reload; rollback; current Product deletion after Order creation; historical snapshot independence; tax reload; manual single bucket; normal mixed buckets.

### 16.4 Dispatch-boundary integration

Cover:

1. not committed → sink not called;
2. committed → sink receives stable committed Order;
3. sink fails → Order reloadable;
4. simulated restart after sink failure → Order present;
5. dispatched snapshot equals persisted snapshot, not mutable draft.

### 16.5 STA/WPF lifecycle

Because of M03 defect history, source/XAML assertions alone are insufficient. Exercise actual loaded/shown controls for first Product add; double-click/explicit add; structured option dialog; SINGLE/MULTI; custom add/edit/remove; quantity; reopen line editor; Cancel; remove line; Retrait/Livraison; discount OFF→ON; threshold feedback; manual total; override clearing; confirmation busy/disabled; committed state; FR→zh-CN→FR; default/resized/maximized layout; scrolling with sufficient content.

Verify operator-visible/action state, not only internal booleans.

## 17. Windows/WPF manual acceptance checklist

The final M04 artifact requires operator acceptance before M04 can be declared Passed. Use synthetic data.

### A. Regression baseline

1. Launch app.
2. Catalogue still works.
3. Settings still works.
4. Existing M03 data persists.
5. FR ↔ zh-CN leaves prior M03 destinations usable.

### B. Basic order entry

6. Open Caisse/order entry.
7. Fulfilment is not silently selected.
8. Browse category.
9. Search by code.
10. Search by name.
11. Add simple Product by explicit Add.
12. Add by double-click.
13. Directly change quantity.
14. Remove line.

### C. Options

15. Required SINGLE blocks missing choice.
16. Select one and add.
17. Edit line and change choice.
18. Quantity preserved.
19. Optional SINGLE permits empty.
20. Required MULTI min/max.
21. Above max blocked.
22. Inactive Option unavailable.
23. Cancel option edit leaves line unchanged.

### D. Custom/A1

24. Add positive adjustment with label.
25. Add negative adjustment with label.
26. Add zero adjustment with label.
27. Blank label rejected.
28. Quantity 2 multiplies option/custom amounts per A1.
29. Catalogue Product base price unchanged.

### E. Retrait

30. Select Retrait.
31. Discount defaults OFF.
32. Enable explicitly.
33. Eligible/non-eligible behavior.
34. Positive option not discounted.
35. Negative adjustment reduces discountable component.
36. Below-€15 candidate rejects discount.
37. Exact threshold allowed.
38. Change Settings and verify new pricing parameter.

### F. Livraison

39. Select Livraison.
40. Ordinary Retrait discount not active.
41. Telephone may be blank.
42. Address may be blank.
43. €29.99 fails against €30.
44. €30 passes.
45. Enable synthetic fee and verify added after minimum.
46. Fee does not qualify under-minimum order.

### G. Telephone

47. Enter `0612345678`.
48. Confirm/reload.
49. Verify `06 12 34 56 78`.
50. Blank telephone allowed.

### H. Manual total

51. Build mixed-VAT order.
52. Directly edit authoritative total.
53. Verify override state.
54. Verify saved/reloaded final tax snapshot is exactly one 10% bucket.
55. Change quantity and verify override clears and mixed VAT returns.
56. Set override again; edit telephone/comment; verify override remains.

### I. Persistence/stable ID

57. Confirm valid order.
58. Record stable ID.
59. Status is Open.
60. Commit precedes fake dispatch.
61. Reload exact ID.
62. Restart and reload same ID/content.

### J. Historical independence

63. Record confirmed snapshots.
64. Modify underlying current Product/category/options.
65. Deactivate and permanently delete Product where M03 permits.
66. Reload historical Order.
67. Verify historical code/name/category/price/VAT/options/adjustments/total/tax unchanged.

### K. Failure behavior

68. Exercise deterministic injected dispatch failure evidence path.
69. Verify Order remains committed/reloadable.
70. Verify failure is not presented as Order-save failure.

### L. Localization/layout

71. With non-empty cart/options, switch FR→zh-CN→FR.
72. No business data/state changes.
73. Labels readable at normal size.
74. Resize/maximize.
75. Controls remain practical/unclipped.

Do not extend manual acceptance into M05+ workflows.

## 18. Acceptance-status mapping

Subject to evidence, M04 is intended to close:

- `AC-CAT-006`
- `AC-CAT-007`
- `AC-CAT-012`
- historical-independence remainder of `AC-CAT-003`
- `AC-ORD-001`
- M04 ordinary-new-order portion of `AC-ORD-002`
- initial-confirmation portion of `AC-ORD-003`
- `AC-ORD-004` through `AC-ORD-010`
- pricing-consumer remainder of `AC-ORD-011`
- `AC-LIFE-001`
- `AC-LIFE-002`

Honesty boundaries:

- AC-ORD-002 later reusable-information regression belongs to M05 when that path exists.
- AC-ORD-003 remains Partial after M04 because same-ID later Livraison address modification belongs to M05.
- AC-ORD-004 receives final printer cross-check in M08.
- AC-CAT-012 historical snapshot invariant may be Passed in M04; M08/M11 later add consumer regressions using persisted history.
- AC-LIFE-001 may be Passed for durable commit/dispatch/restart invariant; M08 later cross-checks final Windows adapter.
- `AC-LIFE-008` and `AC-NFR-003` are partial foundation only; do not mark Passed.
- Do not mark any M05+ primary criterion Passed merely because a field/enum exists.

## 19. Parallel execution plan

This milestone inherits `agent-execution-contract.md`.

The Codex main agent first performs shared-contract work serially: re-read specs, Domain boundaries, pricing sequence, Application interfaces, schema contract, dispatch interface and shared test fixture contracts.

After seams are stable, likely independent workstreams are:

- Worker A: Domain/pricing implementation/tests;
- Worker B: SQLite migration/store/integration tests;
- Worker C: WPF/order-entry presentation/resources/STA tests;
- Worker D: independent read-only integration/quality audit of pricing, snapshot authority, commit-before-dispatch, WPF lifecycle and scope leakage.

Do not race shared contracts, project files, migration registry or composition root. Main agent reviews/integrates all output and performs final integration.

When explicitly controllable, subagents use GPT-5.6 Luna at highest available reasoning effort (`max` preferred). If runtime cannot guarantee/report model/effort, follow `agent-execution-contract.md` and report truthfully.

## 20. Verification required before CODEX_DONE

Before completion run repository-required verification plus M04-specific evidence, at minimum:

- restore;
- Release build;
- complete solution tests;
- zero failed tests;
- no unjustified new warnings;
- required self-contained Windows publish path;
- GitHub CI for final pushed head.

Record exact SDK/runtime, test totals/project counts, warning/error count, publish result, final SHA and CI URL/check.

Never fabricate manual operator evidence. If runtime cannot interactively run Windows WPF, say so and provide strongest STA/WPF lifecycle coverage plus exact remaining manual checklist.

## 21. CODEX_DONE evidence requirements

Completion report must include:

- exact handoff ID;
- branch/PR/final head;
- delivered M04 scope;
- explicit exclusions respected;
- migration version/schema summary;
- pricing summary and A1/B1/C1 evidence;
- transaction/rollback evidence;
- historical independence evidence;
- dispatch ordering/restart evidence;
- automated test totals;
- Release build/publish/CI;
- acceptance mapping;
- remaining manual checks;
- execution topology required by `agent-execution-contract.md`;
- operator-journey evidence required by `interactive-quality-gate.md`;
- M05 not started;
- PR remains open/unmerged;
- `POST_TASK_POWER_ACTION: NONE`.

`CODEX_DONE` never authorizes merge.

## 22. Definition of M04 complete

M04 may be presented for final merge approval only when:

- M03 Catalogue/Settings regressions remain green;
- M04 order entry works end-to-end;
- option rules, A1 quantity semantics and custom adjustments work;
- cart editing works;
- Retrait B1/C1 behavior and Livraison minimum/fee work;
- VAT snapshots and manual authoritative total work;
- new Order commit is atomic with stable ID;
- historical snapshots are independent from current Catalogue;
- exact committed reload works;
- commit occurs before fake dispatch;
- dispatch failure and restart cannot lose the Order;
- FR/zh-CN workflow is usable;
- automated verification is green;
- required Windows/WPF operator checklist is passed;
- implementation status is updated honestly;
- no M05+ production business feature has been pulled forward;
- PR remains open/unmerged pending explicit owner approval.

**Central M04 invariant:**

> A valid new order becomes a complete, durable, historically self-contained Sushi81 POS business record before any output dispatch is attempted, and later current Catalogue changes cannot alter what was sold.
