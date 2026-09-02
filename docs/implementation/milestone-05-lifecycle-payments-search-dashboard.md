# M05 — Lifecycle, payments, search and operational dashboard

**Status:** Approved implementation contract  
**Approved by:** project owner  
**Approval date:** 2026-09-02  
**Phase:** 6 — Implementation  
**Milestone:** M05  
**Approved starting baseline:** `main` after M04 merge commit `ab218263bd4eee9c1be203d36acc552988cef43a`, plus M05 preparation documentation committed before implementation begins  
**Codex execution authorization:** only through one matching durable `CODEX_HANDOFF_READY` record on the active M05 PR while issue #4 is OPEN  
**POST_TASK_POWER_ACTION:** `NONE`

## 1. Mission

M05 extends the merged M04 order-entry vertical slice into the first complete day-to-day order-management workflow:

**retrieve/search live orders → inspect structured committed state → modify the same order safely → edit cumulative CB/Espèce with dated signed deltas → explicitly Close/reopen/cancel → abandon unsaved edits → create a new order from reusable customer text → surface operational turnover / received-payment / future / due-today / overdue views**

M05 must extend the same M04 Order business record, persistence model and four-layer architecture. It must not create a parallel lifecycle/order model.

M05 does **not** implement final Windows printing, authority/recovery enforcement, pairing/handoff UI, Hiboutik parsing, catalogue Excel, management export, annual archive or installer behavior.

GitHub current Approved sources are authoritative. Legacy Excel/VBA behavior and chat memory are not specification sources.

## 2. Mandatory source review before coding

Before production code changes, the Codex main agent must re-read current branch authority including at minimum:

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
- `docs/data-model.md`
- `docs/architecture.md`
- `docs/storage-strategy.md`
- `docs/printing.md`
- `docs/decisions/payment-effective-date.md`
- `docs/decisions/advance-order-marker.md`
- `docs/decisions/order-modification-printing.md`
- `docs/decisions/m05-lifecycle-payment-modification-clarifications.md`
- all other Approved decision records
- `docs/implementation/agent-execution-contract.md`
- `docs/implementation/interactive-quality-gate.md`
- `docs/implementation/control-state-preservation.md`
- `docs/implementation/post-task-power-policy.md`
- `docs/implementation/milestone-04-order-entry.md`
- `docs/implementation/milestone-04-worklog.md`
- `docs/implementation/milestone-04-final-manual-acceptance.md`
- `src/README.md`
- `tests/README.md`

If any Approved source materially conflicts with another Approved source on lifecycle, payment attribution, snapshot meaning, search/report semantics or user workflow, stop only the affected path and report the conflict. Do not guess.

## 3. Frozen M05 decisions

`docs/decisions/m05-lifecycle-payment-modification-clarifications.md` is authoritative.

### D1 — Human order reference

Introduce immutable unique operator-facing reference `YYYYMMDD-NNN` where:

- date = BusinessDate of first durable creation;
- sequence = atomic per-date sequence;
- display = at least three zero-padded digits but naturally grows above 999;
- reference never changes after creation;
- GUID remains technical primary identity;
- migrated M04 orders receive deterministic permanent backfilled references by business creation date, `created_at` order and GUID tie-breaker.

Normal UI and live search should use/show the human reference instead of forcing staff to work with GUIDs.

### D2 — Snapshot-preserving existing-order modification

Existing orders open from persisted snapshots. Current Catalogue must not silently rewrite historical values.

- non-price-affecting edits preserve line snapshots/manual total/tax snapshot;
- quantity change uses existing snapshotted price/VAT/eligibility/adjustments;
- removing an existing line removes it;
- newly added lines use current active Catalogue;
- explicit option reconfiguration intentionally uses current Catalogue and replaces that line's Catalogue-dependent configuration;
- unavailable/inactive source Product does not invalidate historical readability or quantity correction/removal, but blocks current option reconfiguration until replaced explicitly;
- price-affecting saves use current BusinessSettings and clear manual override under the existing rules.

### D3 — Non-negative cumulative payments

Current cumulative CB and Espèce are each constrained to >= 0 cents. Negative signed deltas remain valid when reducing a previous cumulative amount.

## 4. Explicit M05 scope

### 4.1 Human-readable order reference

Add the D1 reference to Domain/Application/persistence/presentation.

Requirements:

- globally unique within the live lineage;
- generated only at durable new-order creation;
- allocation occurs inside the same business transaction as order creation so failure cannot consume/commit an order inconsistently;
- restart-safe and handoff-safe because the durable sequence state is in SQLite business data, not process memory;
- deterministic v5 migration/backfill of every existing M04 order;
- exact live lookup by reference;
- normal order list/detail prominently displays reference;
- GUID remains available internally and may remain diagnostic-only in UI.

Do not introduce a formal invoice-number sequence. This is an operational order reference, not a B2B invoice identifier.

### 4.2 Existing-order retrieval and Commandes destination

Create a dedicated `Commandes` destination separate from the normal Caisse workflow.

Reuse the persisted M04 date-browser/exact-ID seams where appropriate, but replace the M04 diagnostic-style committed-order display with a structured operator-facing master/detail workflow.

The application must support:

- date-based browse by persisted `planned_fulfilment_date` for past/current/future dates;
- live search independent from the current date filter;
- exact operator reference search;
- telephone search;
- comment-text search;
- deterministic list ordering;
- selection of the exact persisted order and structured details;
- Cancelled orders remaining findable;
- current/live database only for normal M05 search.

Do not automatically open or search annual archives; that closes in M12.

### 4.3 Structured order detail

The normal selected-order detail must no longer require reading one monospaced diagnostic text blob.

Present at least:

- operator reference and status;
- fulfilment mode;
- planned date/time;
- advance-order marker where operationally useful;
- telephone/address/comment;
- ordered lines in saved order;
- line quantities/options/custom adjustments;
- authoritative Total TTC/manual-override indication;
- VAT snapshot summary;
- cumulative CB/Espèce/payment total/difference;
- available lifecycle/modification actions.

All new operator-facing strings must support French and Simplified Chinese.

### 4.4 Same-ID modification

Every non-cancelled order remains modifiable regardless of Open/Closed state.

Entering modification:

- loads a draft from the latest successfully committed snapshot;
- retains stable GUID and human reference;
- retains current lifecycle/payment facts unless explicitly changed through their dedicated controls;
- follows D2 snapshot-preservation semantics.

Before save the operator can modify approved ordinary order fields including:

- line quantity;
- line removal;
- adding current Catalogue products;
- explicit current-Catalogue option reconfiguration where allowed;
- custom line adjustments;
- fulfilment mode;
- planned fulfilment date/time;
- telephone;
- delivery address;
- comment;
- Retrait discount request / pricing inputs where the ordinary rules permit;
- authoritative manual total through the existing rule.

Saving a modification:

- updates the same Order ID/reference;
- is atomic across order snapshot rows and applicable lifecycle state changes;
- never automatically prints either document;
- refreshes the current snapshot state used by later viewing/printing/reporting/export;
- preserves `advance_order_marker = true` once it has ever been true;
- if a non-cancelled order is saved with a future planned date while the marker was false, set it true;
- does not create operator-visible revision history.

### 4.5 Abandon edits

Provide an explicit abandon/cancel-edit action.

It must:

- discard only uncommitted in-memory edits;
- reload/restore the latest persisted state;
- not create PaymentAdjustment rows;
- not change status/timestamps/business data;
- not mutate Caisse or unrelated view state.

### 4.6 Payment model

M05 implements the approved V1 cumulative-payment workflow.

Persist current cumulative amounts for the Order:

- CB/Card;
- Espèce/Cash.

Derived values:

- paid total = CB + Espèce;
- difference = authoritative Order Total TTC - paid total;
- composition is derived automatically; no separate CB/Espèce/Mixte selector.

Cumulative values are integer cents and each must remain >= 0 under D3.

### 4.7 Signed dated PaymentAdjustment ledger

Internally retain signed dated changes sufficient for cross-day received-payment reporting.

Each durable adjustment must include at least:

- stable adjustment ID;
- Order ID;
- payment channel (CB or Espèce);
- signed delta cents;
- effective payment date/time used for business attribution;
- application-generated `recorded_at` technical timestamp.

Normal UI edits cumulative amounts; it does not expose the technical ledger as a routine operator feature.

When one save changes both CB and Espèce, persist the two channel deltas atomically with the resulting cumulative order payment state. Do not write zero deltas.

Examples:

- CB 0 -> 20 => +20 CB delta;
- next day CB 20 -> 50 => +30 CB delta only;
- CB 50 -> 30 => -20 CB delta;
- no change => no adjustment row.

### 4.8 Effective date attribution

Normal payment entry defaults effective payment date to current `IBusinessClock.BusinessDate`.

The operator may select another effective date when entering/correcting a payment after the fact.

Rules:

- effective attribution affects reporting only;
- `recorded_at` remains actual persistence timestamp and is never operator-editable;
- changing effective date does not create a second Order or change Order reference;
- selected effective date applies to the payment deltas created by that save;
- if no cumulative amount changed, merely changing the date must not create a zero business adjustment.

Use business timezone/clock consistently; do not use UI-machine ad hoc `DateTime.Now` calculations.

### 4.9 Lifecycle actions

Status remains exactly:

- Open;
- Closed;
- Cancelled.

#### Close

Close is explicit; becoming exactly paid does not automatically close the Order.

Close is allowed only when:

`CB + Espèce == authoritative Total TTC`

exactly to the cent.

Successful Close:

- retains same ID/reference;
- status = Closed;
- sets `closed_at` from application clock;
- clears/keeps cancellation timestamp null as appropriate;
- is an atomic durable business mutation.

#### Automatic reopen after incompatible saved change

If a currently Closed non-cancelled Order is successfully saved after a modification/payment correction and the resulting cumulative payment no longer equals the authoritative total, the same transaction must set status back to Open and clear `closed_at`.

A saved change that keeps exact equality may leave the Order Closed.

Do not require the operator to manually reopen merely because the saved change itself made the Closed invariant false.

#### Cancellation

Cancellation:

- is explicit and requires clear operator confirmation;
- retains the Order, lines, tax snapshots and PaymentAdjustment/payment facts;
- status = Cancelled;
- sets `cancelled_at`;
- does not delete the row;
- does not clear the advance-order marker;
- excludes the order from active financial/statistical treatment under the frozen rules;
- remains terminal in ordinary V1 workflow; no Uncancel action is authorized.

### 4.10 Reuse customer/order text into a new order

From an existing order provide an action equivalent to `Nouvelle commande avec ces coordonnées`.

Only these fields may be reused:

- telephone;
- delivery address;
- comment.

The resulting Caisse draft must have:

- empty cart;
- no inherited fulfilment mode;
- new-order planned date behavior from current Caisse;
- no inherited planned time;
- no inherited payment values;
- no inherited total/status/discount request;
- no inherited GUID/reference;
- ordinary confirmation allocates a new GUID/reference.

If replacing a non-empty uncommitted Caisse draft would lose work, require explicit operator confirmation; never silently overwrite it.

### 4.11 Operational dashboard / Caisse compact indicators

Keep the Caisse focused on fast new-order entry. Add only a compact operational summary/entry strip rather than moving the whole Commandes browser into Caisse.

The main interface must expose at least:

- operational turnover for today;
- actual received total for today;
- received CB today;
- received Espèce today;
- future-order count/entry point;
- due-today advance-order count/entry point;
- overdue-unsettled count/entry point.

Clicking an order count/entry point should navigate to `Commandes` with the corresponding view/filter applied.

No dedicated Hiboutik emergency dashboard/count is allowed.

### 4.12 Operational turnover semantics

For ordinary POS-originated, non-cancelled activity, today's operational turnover uses current authoritative order totals attributed by planned fulfilment/business operational date as frozen in lifecycle/acceptance documents.

Do not substitute payment-received date for turnover date.

A later same-ID modification changes the current authoritative business result for the applicable operational reporting semantics; M05 does not invent immutable turnover revision history.

HiboutikPaste source remains excluded from ordinary POS-originated turnover even though M09 UI/parser is not implemented yet.

### 4.13 Received-payment summaries

Today's received total/CB/Espèce are sums of signed PaymentAdjustment deltas whose effective date is today, excluding Cancelled and excluded-source business activity under the frozen anti-double-counting rules.

The summary is not based on:

- Order creation date;
- planned fulfilment date;
- final cumulative payment value copied wholesale each day;
- technical `recorded_at` when an explicit back-entry effective date differs.

Corrections may make a day's signed received summary lower than before and may contribute negative amounts if a prior receipt is corrected downward on that effective date.

### 4.14 Future orders

Future-order view/count includes non-cancelled orders whose planned fulfilment date is after current BusinessDate.

Provide practical navigation to inspect actual future orders/dates.

### 4.15 Due-today advance orders

A due-today advance-order reminder is derived when:

- planned fulfilment date = current BusinessDate;
- `advance_order_marker = true`;
- status != Cancelled.

Closed/payment state does not remove this reminder. No separate processed/collected state is introduced merely to dismiss it.

### 4.16 Overdue unsettled orders

Surface orders that:

- planned fulfilment date < current BusinessDate;
- are not Cancelled;
- are not fully Closed/reconciled under the approved lifecycle/payment semantics.

Use the exact frozen acceptance/lifecycle interpretation; do not invent age cutoffs or hidden dismiss states.

## 5. Application architecture

Keep the existing four layers and M04 seams.

### 5.1 Do not turn new-order service into a universal god service

`OrderEntryService` remains the ordinary new-order creation/pricing confirmation boundary.

Add a focused Application service/use-case boundary for existing-order lifecycle/payment/search operations (name is technical choice, e.g. `OrderLifecycleService`). It must operate on the same Order data/store, not a second model.

### 5.2 Existing-order drafts

Use explicit Application/Domain representations for editable existing-order state rather than binding WPF directly to SQLite rows.

The conversion from persisted snapshot to editable draft must encode D2 deliberately.

### 5.3 Shared pricing

Reuse the M04 deterministic pricing service/rules. WPF must not duplicate formulas and persistence must not independently recalculate alternative formulas.

When pricing historical snapshot-based lines whose current Catalogue is not authoritative, create the minimum explicit domain input needed to price those snapshotted values safely; do not fake ProductAggregate current-catalogue authority merely to reuse a method signature.

If this requires a small refactor of pricing inputs while preserving M04 behavior, it is authorized as a technical refactor with regression coverage.

### 5.4 Search/query seams

Provide narrow deterministic Application queries for:

- planned-date browse;
- human reference exact lookup/search;
- telephone/comment live search;
- future/due-today/overdue views/counts;
- operational turnover;
- effective-date payment summaries.

Do not create generic repository/query abstractions merely for M05.

## 6. SQLite migration 5

M05 must introduce the next additive production migration: **version 5**.

Migrations 1–4 remain unchanged.

Migration 5 must preserve all existing catalogue/settings/order rows and fail safely without reset/recreate.

At minimum persist equivalent semantics for:

- immutable human order reference;
- current cumulative CB cents;
- current cumulative Espèce cents;
- PaymentAdjustment signed-delta rows;
- sequence/allocation state needed for atomic daily references, unless an equally safe transactionally derived approach is proven;
- indexes for reference, live search and lifecycle/dashboard queries where justified.

Do not use SQLite `REAL` for money/percentage.

### 6.1 Backfill safety

Migration tests must start from populated v4 data with multiple orders including:

- same creation business date;
- different creation dates;
- future planned date different from creation date;
- equal/near-equal timestamps where deterministic tie-breaking matters;
- existing snapshots/tax data.

After migration verify:

- all prior business fields unchanged;
- references deterministic/unique/stable;
- date portion uses business creation date rather than planned date;
- cumulative payments initialize to zero for pre-payment M04 orders;
- retry/failure behavior does not leave partial references/payment schema;
- old orders reload correctly.

## 7. Transactionality and failure behavior

Every multi-row business mutation must be one SQLite transaction through the existing transaction seam.

At minimum failure injection/integration coverage is required for:

- new-order reference allocation + order creation rollback;
- same-ID order modification snapshot replacement/update rollback;
- payment cumulative state + one/two PaymentAdjustment rows rollback;
- automatic Closed -> Open transition rollback with the triggering save;
- explicit Close rollback;
- cancellation rollback;
- migration v4 -> v5 failure preservation/retry.

A failure must never leave:

- cumulative CB/Espèce changed without matching signed adjustment rows;
- adjustment rows without the matching cumulative state;
- Closed status inconsistent with the committed total/payment result because half the save failed;
- a partially rewritten order snapshot;
- duplicate operator references.

## 8. WPF interaction contract

M05 inherits `interactive-quality-gate.md` and `control-state-preservation.md`.

### 8.1 Main destinations

Normal major destinations remain:

- Caisse;
- Commandes;
- Catalogue;
- Paramètres.

Do not remove working M03/M04 destinations.

### 8.2 Caisse state preservation

Navigating between Caisse and Commandes must not silently destroy an uncommitted Caisse draft.

Keep the Caisse ViewModel/draft alive during ordinary navigation or implement an equivalent explicit preservation strategy.

The reuse-customer action must explicitly confirm before replacing a non-empty current Caisse draft.

### 8.3 Commandes master/detail

Provide a practical list/detail layout at supported window sizes in FR and zh-CN.

The list should expose compact distinguishing facts such as:

- human reference;
- planned date/time;
- fulfilment;
- status;
- Total TTC;
- CB;
- Espèce;
- difference/remaining;
- telephone.

The exact column layout is a presentation detail, but staff must not need GUID memory or diagnostic text parsing.

### 8.4 Edit state

Clearly distinguish committed read-only state from active editable draft state.

Provide explicit actions for:

- Modify;
- Save;
- Abandon;
- Close where eligible;
- Cancel Order;
- New Order with these details.

Do not show an Uncancel action.

### 8.5 Payment editor

The operator edits current cumulative CB/Espèce with cent precision and an effective date control defaulted to current BusinessDate.

Show enough feedback to understand:

- current total paid;
- difference from authoritative total;
- whether Close is allowed;
- validation if either cumulative value would be negative.

Do not expose PaymentAdjustment IDs or technical ledger rows in the normal screen.

### 8.6 Localization and event/lifecycle safety

Automated STA/WPF tests must cover dynamic/event paths, not only ViewModel unit state.

At minimum test:

- FR -> zh-CN -> FR with selected order/search/filter/edit/payment state preservation;
- Commandes list/detail selection;
- search text and date/filter transitions without stale async overwrite;
- Modify/Abandon restoring committed state;
- cumulative payment edits/effective-date preservation across unrelated UI actions;
- navigation Caisse <-> Commandes preserving unrelated Caisse draft;
- reuse action confirmation when current Caisse draft is non-empty;
- lifecycle button enablement and re-evaluation after payment/total changes;
- dashboard click-through into correct Commandes view;
- no accidental auto-print action after existing-order save/payment/lifecycle operations.

## 9. Recovery boundary

`storage-strategy.md` already states that important durable saves such as order modification, payment change, Close/Cancel should trigger local recovery protection.

M05 must preserve/invoke the existing M01 recovery-trigger seam where already available and must not make it harder for M06 to centralize/enforce recovery.

However M06 remains the milestone that completes authoritative/read-only enforcement, scheduling/debounce/flush and full recovery integration across all mutations. M05 must not expand into M06 authority UX.

## 10. Printing boundary

M08 owns final Windows printing.

M05 rules:

- saving an existing-order modification does not automatically print;
- changing payment does not automatically print;
- Close/Cancel does not automatically create undocumented print behavior;
- keep existing post-commit print boundary intact for new orders;
- do not implement kitchen/customer reprint buttons unless a tiny non-functional seam is unavoidable for architecture, because M08 owns actual reprint behavior/markings/queues;
- any existing no-op/test dispatcher behavior must remain deterministic.

M05 may update specification terminology so future print models use the human order reference as the normal operator-visible order identity while retaining GUID internally.

## 11. Acceptance ownership

M05 owns completion of:

- `AC-LIFE-003` through `AC-LIFE-014`;
- live/current search portion of `AC-LIFE-015`;
- M05-owned remaining portions of `AC-ORD-002` and `AC-ORD-003` relating to reuse and later same-ID modification.

Do not mark archive access portion of AC-LIFE-015 Passed; M12 owns archive completion.

Where an AC wording groups multiple behaviors, record exact test/manual evidence per behavior rather than using a blanket milestone claim.

## 12. Required automated evidence

At minimum add/extend automated evidence for:

### Domain/Application

- D1 reference semantics and immutability;
- same-ID modification rules;
- sticky advance marker after future/today changes;
- snapshot-preserving quantity/non-price edits;
- explicit current-Catalogue reconfiguration boundary;
- current BusinessSettings application on price-affecting modification;
- manual override preservation/reset boundaries;
- non-negative cumulative payments;
- signed delta arithmetic;
- effective date vs recorded timestamp;
- exact Close eligibility;
- explicit Close (not auto-close);
- automatic reopen after incompatible Closed modification/payment change;
- Cancelled retention/exclusion semantics;
- reuse-only telephone/address/comment;
- no inherited fulfilment/cart/payment/status/reference.

### Infrastructure integration

- v4 -> v5 populated migration/backfill;
- deterministic sequence/reference across restart;
- transactional new-order reference allocation;
- same-ID update/reload;
- payment delta persistence/reload;
- two-channel atomic payment change;
- negative correction delta with non-negative cumulative result;
- effective-date summary queries;
- reference/telephone/comment search;
- future/due-today/overdue queries;
- turnover queries;
- cancellation exclusions;
- HiboutikPaste exclusions using synthetic persisted data only;
- all failure-injection rollback paths in section 7.

### WPF/STA

Cover the journeys in section 8 using real controls/events where practical, especially dynamic DataContext/localization/navigation behavior.

## 13. Manual Windows/WPF acceptance

M05 cannot be marked fully Passed until the project owner performs a production-like Windows operator pass on the published artifact.

The final checklist must include at least:

1. existing M04 Caisse still creates ordinary orders correctly;
2. new order visibly receives sensible human reference;
3. multiple same-day orders receive increasing references;
4. Commandes finds prior orders without GUID copying;
5. exact reference search;
6. telephone search;
7. comment search;
8. same-ID modification and Save;
9. Abandon restores last persisted state;
10. old line quantity edit does not silently adopt later Catalogue price;
11. adding a new line uses current Catalogue;
12. explicit option reconfiguration behavior is understandable;
13. CB-only payment;
14. Espèce-only payment;
15. mixed payment;
16. downward correction creates correct current result;
17. negative cumulative input is rejected;
18. effective date back-entry changes the intended daily summary;
19. fully paid Open order does not auto-close;
20. explicit Close succeeds at exact equality;
21. Closed order reopens after a saved incompatible total/payment change;
22. Closed order can remain Closed when equality remains valid;
23. cancellation retains searchable order/payment facts but removes it from active dashboard/financial treatment;
24. reuse customer details creates a fresh empty Caisse draft with no inherited fulfilment/payment/status/order identity;
25. non-empty Caisse draft is not silently overwritten by reuse action;
26. future-order counter/list;
27. due-today advance-order counter/list independent of payment/Closed state;
28. overdue-unsettled counter/list;
29. operational turnover and received today/CB/Espèce values are distinguishable and correct in a synthetic/manual scenario;
30. FR/zh-CN switching preserves relevant state and all new labels fit/read correctly.

Manual acceptance must never be fabricated by Codex.

## 14. Parallel execution plan

The Codex main agent must first stabilize shared Domain/Application contracts and migration shape before delegation.

Likely independent work packages after seams are frozen:

1. **Domain/Application lifecycle/payment logic and tests** — existing-order draft semantics, lifecycle, payment arithmetic, reference allocation contract.
2. **SQLite migration/store/query integration** — v5 schema/backfill, transactional update/payment/search/dashboard queries and integration tests.
3. **WPF Commandes/dashboard/localization** — only after Application contracts are stable.
4. **Independent read-only audit/test design** — inspect D2 snapshot-preservation and lifecycle/payment failure paths without editing the same hot production files.

Do not parallelize shared contract design, migration registry edits, CompositionRoot integration or final integration. Avoid multiple writers in the same ViewModel/store files. Follow `agent-execution-contract.md`; use GPT-5.6 Luna at highest available/max when explicitly controllable and report actual execution topology truthfully.

## 15. Verification gate

Before `CODEX_DONE` for the implementation handoff, the main agent must perform and report:

- restore;
- Release build with 0 errors;
- full test suite, 0 failed;
- self-contained win-x64 publish;
- applicable CI result;
- migration/failure-path evidence;
- acceptance mapping;
- execution topology;
- browser notification attempt after durable GitHub completion comment;
- `POST_TASK_POWER_ACTION: NONE` unless explicitly changed by the operator for that specific run.

M05 PR remains open/unmerged until explicit project-owner merge approval. M06 is not authorized by M05 completion.