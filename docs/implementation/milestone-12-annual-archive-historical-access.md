# M12 — Annual archive and historical access — implementation contract

**Status:** OWNER-AUTHORIZED; WP1 executable under Issue #4 gate
**Milestone:** M12
**Implementation branch:** `codex/m12-annual-archive-authorized`
**Start baseline:** `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`

## 1. Mission

Implement M12 in small reviewable packages while preserving the frozen archive, authority, recovery, export and printing contracts.

This contract does not amend Approved product behavior. `docs/storage-strategy.md`, `docs/data-model.md`, `docs/printing.md`, `docs/acceptance-criteria.md`, `docs/implementation-plan.md` and later Approved decisions remain controlling.

## 2. Non-negotiable invariants

1. Never remove an eligible order from `live.db` before a complete archive has been staged, validated, durably promoted to the application-managed local Archive area, reopened and validated there as the canonical completed archive.
2. Any archive creation/validation/local-promotion/reopen-validation failure leaves eligible live orders intact and safely retryable.
3. Open orders never archive because of age.
4. Closed and Cancelled archive-year assignment uses their end timestamps in the business timezone.
5. `POS` and `HIBOUTIK_PASTE` use identical archive-year semantics.
6. Normal live search remains live-only; historical access is explicit by archive year.
7. Archive databases are permanent application-managed local ordinary-use read-only historical SQLite files and are never part of normal handoff lineage.
8. Archive historical interpretation/reprint uses persisted snapshots, never current Catalogue/current VAT substitution.
9. Archive read/reprint never mutates the archive database.
10. Archive execution is authoritative-only; read-only archive access must not promote or weaken authority.
11. Before live removal, preserve every valid pending/not-yet-emitted Gestion CREATE/UPDATE/CANCEL action as a durable immutable technical payload or an equivalent demonstrably safe representation.
12. Explicit archive export is a separate copy action whose destination is selected by the operator; it never moves/deletes the canonical local archive.
13. OneDrive is not part of annual archive publication/access after the 2026-09-21 amendment.
14. M13 is out of scope and unauthorized.

## 3. WP1 — archive core contract, eligibility and staging

### Scope

WP1 must:

- add an application-owned archive contract/service boundary with no WPF dependency;
- implement deterministic target-year calculation needed by M12: on or after February 1 the candidate year is exactly the previous calendar year; a January startup does not target the just-ended year yet;
- derive eligibility from the current committed live database:
  - `CLOSED` -> business-local year of `closed_at_utc`;
  - `CANCELLED` -> business-local year of `cancelled_at_utc`;
  - `OPEN` -> never eligible;
  - both source types treated identically;
- require `IWriteAuthorityGuard` for the archive-staging execution path;
- construct one **independent staged SQLite historical database** under an application-managed temporary/staging location, never directly over the canonical local Archive file;
- store only the historical order aggregate needed for lossless archive access: order rows plus item, item-adjustment, payment-adjustment and tax-breakdown snapshot rows;
- preserve all current persisted order snapshot columns required to reconstruct the existing `OrderSnapshot`, including reference, source type/source total, status/end timestamps, fulfilment/customer text, authoritative totals, payment totals and pricing flags;
- include small archive-format metadata sufficient to validate at least format/schema version, archive year, build timestamp and expected order count;
- validate the staged archive with read-only reopen, `PRAGMA integrity_check`, `PRAGMA foreign_key_check`, required-schema checks, target-year/order-count/order-ID checks and child-row ownership/count checks;
- make staging retry-safe: an interrupted/failed attempt must not mutate `live.db` and must not make an incomplete staged file look completed;
- expose enough application/infrastructure seams for later WP2 local canonical promotion and WP4 read-only local archive access without coupling archive code to current Catalogue repositories.

### Preferred technical shape

A simple dedicated archive reader/builder is preferred over copying all of `live.db`. The archive file should contain the historical order aggregate and technical archive metadata, not current Catalogue/settings, handoff/authority state, recovery state or unrelated live operational tables.

Physical table/index names and internal helper types are delegated technical details provided the logical order data remains compatible and lossless.

Use business timezone conversion through `IBusinessClock.BusinessTimeZone` for end-timestamp year assignment. Do not compare raw UTC year at New-Year boundaries.

### Required automated evidence

Use real SQLite/persistence boundaries, not mock-only tests. At minimum prove:

- January 31 does not produce a February archive target; February 1 and any later date target exactly previous calendar year;
- a delayed start in March/December still targets only previous year, never current January;
- Closed end-of-year and business-timezone boundary assignment;
- Cancelled end-of-year and business-timezone boundary assignment;
- Open orders remain live/non-eligible regardless of age;
- an order created in one year and Closed in the next follows `closed_at`, not creation/planned date;
- POS and `HIBOUTIK_PASTE` are identical for eligibility;
- staged archive contains exact intended order IDs and all item/option/payment/tax child snapshots;
- archive content can reconstruct equivalent historical `OrderSnapshot` values without current Catalogue;
- integrity/schema/metadata/count validation rejects incomplete/corrupt/wrong-year staging;
- authority-blocked staging performs no archive execution;
- injected staging/write/validation failures leave `live.db` byte/content-equivalent for the eligible aggregates and leave no completed archive publication;
- successful WP1 staging itself performs **zero live-row deletion** and zero Gestion export emission/history mutation;
- repeated WP1 staging attempts are safe and do not corrupt live data.

Run the complete existing test suite and Release build after the package; require 0 failed, 0 unexpected skipped, 0 build warnings/errors and `git diff --check` clean.

## 4. Explicit WP1 exclusions

WP1 must **not**:

- publish/promote an archive into the final local canonical `Archive\` area;
- export/copy an archive to an operator-selected destination;
- delete or update eligible order aggregates in `live.db`;
- create or consume Gestion export actions as an archive side effect;
- add an M12 live-database migration unless a concrete WP1 need is demonstrated and reviewed;
- wire startup/February automatic execution;
- add archive-selection/search/hydration WPF UI;
- change normal live search;
- implement archived printing/reprinting;
- alter M07 authority/handoff/DR semantics;
- add Microsoft Graph/OAuth or another remote service;
- use OneDrive for annual archive storage/publication;
- start M13;
- merge the PR.

## 5. Later package boundary

WP2 is now governed by the Approved local-archive amendment: preserve pending export actions, durably promote/reopen-validate the canonical local archive, then perform exact live removal and post-archive recovery. The prior OneDrive acknowledgement blocker is removed. A `CODEX_DONE` for WP1 still does not authorize WP2.
