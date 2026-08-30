# M03 extension — filtered catalogue bulk activation/deactivation

**Status:** Approved implementation contract — authorized extension to active M03  
**Prepared:** 2026-08-30  
**Product:** Sushi81 POS  
**Milestone:** M03 — Catalogue and business settings  
**Active implementation PR:** #5 — `M03: catalogue and business settings`  
**Active branch:** `codex/m03-catalogue-settings`

## 1. Purpose and authority

This contract implements the approved 2026-08-30 V1 specification amendment for filtered bulk Product activation/deactivation discovered during M03 operator acceptance.

Read and obey, in priority order:

- current `AGENTS.md`;
- `docs/v1-specification-freeze.md`;
- `docs/catalogue-management.md`;
- `docs/decisions/filtered-catalogue-bulk-activation.md`;
- `docs/acceptance-criteria.md` plus `docs/acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md`;
- the original M03 contract `docs/implementation/milestone-03-catalogue-settings.md`;
- inherited implementation governance under `docs/implementation/README.md`, especially `agent-execution-contract.md`, `interactive-quality-gate.md`, `control-state-preservation.md`, and `post-task-power-policy.md`.

GitHub current `main` is authoritative. Chat memory and the legacy VBA workbook are not specification sources.

This extension does **not** authorize M04 or any later milestone.

## 2. Preconditions and branch handling

Before production edits:

1. verify Issue #4 is OPEN and still points to PR #5 / `codex/m03-catalogue-settings` / M03;
2. verify PR #5 is OPEN and unmerged;
3. verify the matching authorization file `docs/implementation/milestone-03-filtered-bulk-activation-authorization.md` exists on `main`;
4. reconcile the current `main` into the active M03 branch without force-pushing or rewriting previously reviewed history;
5. ensure the approved 2026-08-30 specification amendment files are present on the branch.

The new amendment documents were intentionally added on `main` after FIX-12. Preserve their approved wording. Do not reinterpret or weaken them.

### Documentation-first consolidation

Before changing production code, fold `AC-CAT-013 — Filtered bulk activation/deactivation` from `docs/acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` into the Catalogue section of `docs/acceptance-criteria.md` and update that file's amendment metadata to include 2026-08-30. The standalone amendment file remains as the historical approval record and must not be deleted.

This documentation consolidation is mechanical only; do not change the approved semantics.

## 3. Exact feature behavior

The existing catalogue filter controls are the authoritative bulk-selection boundary:

- code/name keyword search;
- category filter;
- status filter: All / Active / Inactive;
- all conditions compose.

The existing catalogue list is not paginated. Therefore the `Products` result collection after the latest successful filter query represents the complete current filtered result for M03.

### 3.1 Bulk Activate

Provide an explicit operator action meaning **Activate the filtered results**.

At action start:

- first ensure the latest live-filter/search refresh has completed; never capture a stale pre-debounce result;
- capture an immutable operation snapshot containing every filtered Product ID and that Product's current active state;
- target state is `true`;
- matched count = captured Product count;
- effective-change count = captured products whose active state is currently false.

If effective-change count is zero, perform no business write. Prefer disabling the action whenever the current visible result proves zero effective changes; still guard the application use case against a zero-change request.

### 3.2 Bulk Deactivate

Same contract as 3.1, with target state `false` and effective-change count equal to captured currently-active products.

### 3.3 Confirmation

Before any write, show a modal explicit confirmation localized in French / Simplified Chinese.

The confirmation must state at least:

- target action (Activate or Deactivate);
- total matched count;
- effective-change count.

The action must not be ambiguous about whether it affects one selected row or the filtered set.

Cancel/No closes the confirmation and performs no mutation.

Because the confirmation is modal, the captured target set must remain the target even if future implementation changes permit background refresh; do not recompute the set after confirmation and silently affect different Products.

### 3.4 Commit behavior

After confirmation, call **one Application-owned bulk use case** that performs **one atomic business transaction**.

Required semantics:

- validate the complete captured target set before update;
- duplicate IDs in the request are invalid or normalized deterministically before persistence; no Product may be updated twice;
- every captured Product ID must still exist as a current Product;
- the implementation should carry the captured expected active state for each Product and fail the complete operation if a Product's current active state no longer matches that expected snapshot, so the confirmed effective-change count cannot silently become stale;
- products whose captured state already equals target state are skipped and must not receive a fabricated `updated_at` change;
- every Product that actually changes receives the same operation-level `IBusinessClock.UtcNow` timestamp (or current equivalent) for deterministic batch semantics;
- all effective changes commit or none commit;
- no WPF loop may call the existing single-product `SetProductActive` use case repeatedly with independent commits.

A missing Product, stale expected state, SQLite failure, write-guard failure or other conflict must leave all targeted Product active states unchanged.

Normal invalid/conflict results must use stable Application-owned result/validation semantics rather than raw SQL/stack traces.

### 3.5 Fields that must remain unchanged

Bulk activation/deactivation may change only:

- Product active/inactive state;
- the normal Product `updated_at` timestamp for Products that actually changed.

It must preserve exactly:

- Product opaque ID;
- code;
- name;
- category ID;
- TTC price;
- VAT;
- Retrait-discount eligibility;
- `options_enabled`;
- Product `created_at`;
- every OptionGroup and its ID/name/mode/required/min/max/order/timestamps;
- every Option and its ID/name/adjustment/active/order/timestamps.

Historical orders are outside M03 schema and must not be introduced or changed.

### 3.6 Post-success UI

After successful commit:

- refresh the catalogue automatically;
- preserve search text, category filter and status filter semantic values;
- status filtering may naturally make changed rows disappear (for example Deactivate while filter=Active);
- clear any selected Product that no longer belongs to the refreshed result;
- do not reset language or unrelated UI state.

After failed commit:

- show localized safe operator feedback;
- refresh the catalogue so the operator sees current truth;
- preserve filter values;
- do not report partial success.

## 4. UI contract

Add exactly two bulk state actions to the Catalogue maintenance surface:

- bulk Activate filtered results;
- bulk Deactivate filtered results.

Exact visual placement is implementation-level, but both actions must be clearly associated with the filtered-list scope and visually distinguishable from the existing single-selected-product Activate/Deactivate action.

Recommended French concepts (exact polished resource wording may vary without changing meaning):

- `Activer les résultats filtrés`;
- `Désactiver les résultats filtrés`.

Recommended Simplified Chinese concepts:

- `批量启用筛选结果`;
- `批量停用筛选结果`.

Do **not** add:

- bulk permanent Delete;
- checkbox row-selection/multi-selection framework unless genuinely required by the simplest reliable implementation;
- a generic bulk-edit subsystem;
- bulk price/category/VAT/options editing;
- Excel import/export UI;
- order-entry UI;
- a third-party WPF toolkit.

Prefer the simplest clear action model based on the existing filter result.

## 5. Live-filter synchronization requirement

FIX-12 made search/category/status filtering automatic and race-safe. This extension must build on that behavior rather than bypass it.

A bulk action must never capture a result while a newer search debounce/filter query is still pending. Implement a narrow reliable mechanism, for example a `WaitForLatestFilterRefreshAsync`/idle seam that waits until the currently latest automatic filter task is the task that completed, or an equivalent explicit pending-state gate.

Required operator journey:

1. type/change filters;
2. latest filter result settles;
3. bulk action captures exactly that result;
4. confirmation shows counts for that captured result;
5. confirm;
6. one atomic mutation;
7. automatic refresh preserving filters.

Rapid filter changes immediately before clicking the bulk action must not cause products from an older result to be changed.

Do not regress FIX-12 latest-wins, cancellation ownership, localization semantic-key preservation or force-refresh behavior.

## 6. Application contract

Add a narrow Application-owned bulk activation operation. Do not introduce a generic bulk command framework.

A recommended request shape is equivalent to:

```text
BulkProductActiveStateRequest
- targetActive: bool
- items: [ { productId, expectedIsActive } ... ]
```

A recommended result may include:

```text
BulkProductActiveStateResult
- matchedCount
- changedCount
```

Exact type names are implementation-level.

Application responsibilities:

- reject invalid empty/duplicate/structurally unsafe requests as appropriate;
- return success with zero changed only if invoked defensively with an all-already-target request, while ensuring persistence performs no write; UI should normally prevent this call;
- delegate one atomic persistence mutation;
- expose stable conflict/validation failures;
- never expose SQL details to WPF.

## 7. Persistence contract

Extend the existing narrow catalogue persistence boundary; do not add `IRepository<T>`.

The store method must support one atomic operation over the captured Product IDs/states.

Inside the existing transaction infrastructure:

1. read/validate every requested Product ID and current active state;
2. fail before writes if any requested Product is missing or has a stale unexpected state;
3. determine effective changes;
4. update only those Products whose current state differs from target;
5. use one operation timestamp for all changed rows;
6. commit once;
7. rollback everything on failure.

The implementation may use prepared per-ID statements inside one transaction or a safe set-based SQL strategy. Catalogue size is small; reliability/readability wins over micro-optimization.

Reads used for validation remain read-only where appropriate before the write transaction, but the authoritative compare-and-update validation must occur inside the write transaction so a stale gap cannot create partial semantics.

No migration/schema change is expected or authorized for this feature. If implementation believes a schema change is necessary, stop and report the reason before changing migrations.

## 8. Localization and layout

Every new visible label/message must exist in both `fr-FR` and `zh-CN` resources and pass existing resource parity checks.

Follow `interactive-quality-gate.md`:

- verify natural text fit in French and Chinese;
- normal window, minimum supported width/height and resized/maximized layouts must not clip the new actions/confirmation information;
- bulk buttons should use content-sized/WrapPanel/Grid layout rather than brittle fixed widths that clip French;
- keyboard focus and modal confirmation must remain usable;
- no visual-only indication may be the sole meaning of disabled/active state.

## 9. Automated tests — mandatory

Add deterministic tests at the strongest applicable layer.

### 9.1 Application tests

At minimum prove:

- request with mixed active/inactive items returns correct matched/effective semantics;
- Activate skips already-active items;
- Deactivate skips already-inactive items;
- zero-effective-change path does not invoke a persistence write when the UI/service path can determine it;
- duplicate/invalid request behavior is deterministic;
- stable conflict results are propagated without raw infrastructure details.

### 9.2 Infrastructure integration tests

Use synthetic Products with at least two categories and at least one Product containing OptionGroups/Options.

Prove:

- multiple Products activate in one successful transaction;
- multiple Products deactivate in one successful transaction;
- already-target-state Products preserve `updated_at`;
- changed Products receive the expected target state and one deterministic operation timestamp;
- Product code/name/category/price/VAT/discount/options-enabled/created timestamp remain unchanged;
- OptionGroups and Options remain byte/business-value equivalent, including IDs/order/adjustment/active states;
- missing requested Product causes full rollback;
- stale expected active state causes full rollback;
- injected failure after at least one update attempt rolls back all changes;
- no schema/migration version changes.

### 9.3 WPF / presentation tests

Add realistic STA/WPF or presentation-seam tests proving:

- both bulk actions exist and are localized FR/zh-CN;
- with All filter and mixed states, both actions expose correct enablement/count plan;
- with Active filter, bulk Activate has zero effective changes and cannot write; bulk Deactivate can act;
- with Inactive filter, inverse behavior;
- code/name search + category + status combination yields the exact captured target IDs;
- bulk capture waits for the latest debounced/live-filter result rather than stale pre-debounce Products;
- confirmation model/message carries target action, matched count and effective count;
- cancel performs no mutation;
- post-success refresh preserves search/category/status filters;
- selection clears if its Product leaves the filtered result;
- FR → zh-CN → FR preserves business data/filter semantics;
- no bulk Delete control exists;
- new buttons/text fit normal/min/resized layouts in both languages.

If directly automating the native MessageBox is brittle, extract the smallest testable confirmation model/seam while preserving a real modal confirmation in production. Source-string checks alone are not sufficient evidence for the operator journey.

### 9.4 Regression suite

Run every existing M01–M03 test. FIX-12 filter/race regressions must remain green.

## 10. Manual Windows/WPF acceptance — mandatory

Use only synthetic catalogue data. Resume from the existing M03 local test environment; do not reset the database unless ChatGPT explicitly instructs it.

The final operator checklist for this extension must include, one small step at a time:

1. All/All with mixed active states: verify correct bulk button availability;
2. keyword-search result spanning at least two Products: confirm matched/effective counts;
3. cancel confirmation: verify no Product changes;
4. confirm bulk Deactivate: verify all and only captured effective targets change;
5. verify current search/category/status filters remain unchanged after refresh;
6. verify an Active filter naturally becomes empty/changes result after Deactivate;
7. bulk Activate the intended filtered set back to active;
8. category + keyword + status composition targets only the intersection;
9. attempt a zero-effective action and verify no write/no misleading success;
10. FR ↔ zh-CN confirmation/action labels and counts remain clear;
11. verify single-product Activate/Deactivate still works;
12. verify Product editor data/options are unchanged after bulk state changes;
13. verify there is no bulk permanent-delete action;
14. restart and verify persisted active states.

Record concise manual evidence on PR #5. Do not mark M03 Passed until this extension and the original remaining M03 checklist are complete.

## 11. Parallel execution plan

The Codex main agent must assess parallelization before edits under `agent-execution-contract.md`.

Safe candidate work packages **after the Application request/store interface is stabilized by the main agent**:

- one read-only/subagent package reviewing atomic SQLite transaction design and failure cases;
- one independent subagent package designing WPF/operator-journey regression cases/localization/layout checks;
- one documentation/test traceability package if it has a disjoint write set.

Do **not** parallelize competing edits to:

- catalogue persistence interfaces;
- `SqliteCatalogueStore`;
- `M03ShellViewModel`/MainWindow bulk workflow;
- shared localization resources when it creates merge/conflict risk.

Because this is a small feature touching shared M03 seams, production integration is expected to remain mostly serial. If useful subagents are available, request GPT-5.6 Luna at the highest exposed reasoning effort (`max` when controllable) for independent audit/test-design work. Main agent remains sole integrator and owns final verification.

## 12. Required final verification

On the final pushed head run:

- `dotnet restore Sushi81.Pos.sln`;
- `dotnet build Sushi81.Pos.sln -c Release --no-restore` with 0 warnings / 0 errors;
- `dotnet test Sushi81.Pos.sln -c Release --no-build` with all tests passing;
- self-contained win-x64 publish with `PublishSingleFile=false`;
- GitHub Actions Continuous integration success on the exact final pushed head.

Update as applicable:

- `docs/implementation/milestone-03-worklog.md`;
- `docs/implementation-status.md`;
- PR #5 description/evidence so it truthfully includes AC-CAT-013 extension status.

Keep PR #5 OPEN and UNMERGED. Do not start M04.

## 13. CODEX_DONE evidence contract

After CI settles, post a **new top-level PR #5 Conversation comment** whose first non-whitespace line is exactly:

`CODEX_DONE: M03-FILTERED-BULK-ACTIVATION-EXT-13`

Include:

- exact final branch/head SHA;
- exact current `main` SHA incorporated;
- confirmation that the approved amendment docs and consolidated AC-CAT-013 are present;
- schema/migration statement (expected: no migration change);
- Application/persistence/UI implementation summary;
- atomicity/stale-target/rollback evidence;
- live-filter synchronization approach;
- localization/layout evidence;
- exact test totals by project;
- Release build result;
- self-contained publish result;
- exact final-head CI run number/result;
- manual WPF gate state (must remain outstanding until operator performs it);
- execution topology: parallelization assessment, subagents/work packages, integrated/rejected work, requested/actual model/effort if exposed, deliberately serial work and reason, final integration verification;
- PR #5 remains open/unmerged;
- M04 not started/authorized;
- browserNotification outcome.

`POST_TASK_POWER_ACTION: NONE`

This handoff never authorizes merge.