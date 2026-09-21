# M12 — Annual archive and historical access — preparation/readiness

**Status:** OWNER-AUTHORIZED / WP1 READY; OneDrive remote-publication gate requires resolution before live removal  
**Milestone:** M12  
**Reviewed baseline:** `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`  
**Review date:** 2026-09-21

## 1. Authority and scope

GitHub current `main`, Approved specification/decision records, accepted milestone evidence and live execution controls are the only authority.

M11 is Passed/merged through PR #24 at merge commit `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`. The project owner pre-authorized M12 to begin after that merge. M13 remains unauthorized.

Primary M12 acceptance ownership:

- `AC-STO-011` through `AC-STO-014`;
- `AC-PRINT-009`;
- archive portion of `AC-LIFE-015`.

The implementation plan additionally requires every not-yet-emitted or pending Gestion export action to remain durably representable before the corresponding order may leave `live.db`.

## 2. Frozen M12 behavior

Implementation must preserve the Approved specification without reinterpretation:

- only the authoritative device executes annual archive creation/removal;
- on February 1, or the first later safe authoritative startup, process only the previous complete calendar year;
- delayed execution never includes January of the current year;
- Closed uses the business-local year of `closed_at`; Cancelled uses the business-local year of `cancelled_at`; Open remains live regardless of age;
- POS and `HIBOUTIK_PASTE` use the same archive-year rule;
- an archive is an independent historical SQLite database under OneDrive `Archive`, outside the normal live-handoff lineage and read-only in ordinary POS use;
- complete staging/validation/publication confirmation must precede any live removal;
- any failure leaves eligible live data intact and retryable;
- completed archives are permanent unless deliberately managed outside normal POS workflow;
- normal live search does not automatically open archives; the operator explicitly selects an archive year;
- archived viewing/reprint uses retained order/item/option/payment/tax snapshots, never current Catalogue/current VAT as a substitute;
- archive reprint performs no archive write and must preserve M08 `RÉIMPRESSION`, `DUPLICATA` and `ANNULÉ` behavior;
- non-authoritative/read-only devices may inspect/reprint available completed archives without weakening authority.

## 3. Current code seams

The merged code provides reusable foundations:

- `IWriteAuthorityGuard` / `WriteAuthorityGuard` centralize authoritative write admission.
- `IAppPaths` / `WindowsAppPaths` already expose application-managed `Temp` and `Cache` areas required for staging and read-only archive hydration.
- `LocalConfiguration.OneDriveRoot` and M07 composition already establish the configured shared OneDrive root.
- `SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path)` is suitable for later read-only archive hydration/query.
- `SqliteOrderStore` and migrations 3/5/7 persist the complete historical order aggregate: `orders`, `order_items`, `order_item_adjustments`, `order_tax_breakdown` and `payment_adjustments`.
- Child order rows use cascade relationships, making one order aggregate a clear deletion unit once the archive safety gate is satisfied.
- `OrderLifecycleService.SearchLiveAsync` is already a live-only seam; M12 must add a separate archive reader rather than silently widening normal live search.
- M08 printing consumes an `OrderSnapshot`; `OrderPrintDocumentFactory` renders item/tax/payment snapshots and applies explicit-reprint/cancel markings without Catalogue lookup. A later archive reader can therefore feed the existing print boundary.
- M11 `export_batches` persist immutable canonical payloads and `export_emissions` persist successful positive snapshots without foreign keys to `orders`; however pending/un-emitted export eligibility is still derived from the current live order and therefore must be preserved explicitly before archive deletion.
- M07 `OneDriveRecoveryCheckpointPublisher` is useful as a durability/validation pattern only. It writes/validates a local file under the OneDrive root but does not prove remote OneDrive server receipt.

No production archive implementation exists yet.

## 4. Material readiness finding — remote OneDrive publication acknowledgement

M02 real Windows/OneDrive evidence proved that a newly-created regular file can already be visible remotely while Windows Cloud Files still reports `CF_PLACEHOLDER_STATE_NO_STATES`. The accepted M02 report therefore concluded that there is **no documented local-only per-artifact confirmation** that safely proves remote OneDrive upload completion for such a file.

That limitation was solved for normal authority handoff by the Approved GitHub Release Asset transport amendment. The amendment explicitly did **not** redesign annual archive ownership, and the frozen archive contract still requires publication/synchronization success before eligible live rows may be removed.

Therefore:

- WP1 is not blocked: local eligibility and construction/validation of an independent staged archive database require no remote acknowledgement and perform no live deletion.
- WP2's **remote-publication-success -> live-removal** gate is blocked from implementation until a conforming acknowledgement approach is explicitly approved. Codex must not weaken `AC-STO-013`, treat local OneDrive file existence as cloud success, add Graph/OAuth, invent a manual confirmation workflow or delete live rows on an unproven sync state.
- This readiness finding is not a specification amendment. It records the already-proven technical boundary so later implementation cannot silently cross it.

## 5. Work-package plan

### WP1 — archive core contract, eligibility and staged historical SQLite construction

Implement the pure/application archive-year policy, authoritative staging orchestration, real-`live.db` eligibility query and independent archive SQLite construction/validation. No OneDrive publication and no live deletion.

### WP2 — export-preservation + confirmed publication + live-removal transaction boundary

Before any live removal, durably preserve every valid pending/un-emitted Gestion action. Stage/validate/publish the archive, prove the Approved publication-success condition, then remove exactly the archived order aggregates and trigger post-archive local recovery. Failure must leave live data intact and retryable.

**Blocked only at the remote OneDrive acknowledgement choice described in section 4.** Preparation/tests that do not pre-decide that material choice may proceed later, but no deletion path may be enabled first.

### WP3 — February/late-start scheduler, authority integration and retry

Integrate exact February/late-start previous-year triggering into startup after authority resolution. Repeated/restarted execution must be idempotent and fail-safe.

### WP4 — archive discovery, read-only hydration, explicit selection/search

Discover completed yearly archives from the configured OneDrive `Archive` area after reinstall, hydrate only to application `Cache`, open read-only, and expose explicit year selection/search without merging archives into normal live search.

### WP5 — archived reprint, integration hardening and owner candidate

Feed archive snapshots through the existing M08 print model, retain stale/read-only warnings where applicable, prove no archive writes, run full failure/regression evidence and produce the shortest high-value owner acceptance candidate.

## 6. WP1 readiness decision

WP1 is executable without a material product decision because it cannot publish, delete live data, alter export history, schedule startup work, add archive UI or change printing behavior.

The first executable handoff must be WP1 only.
