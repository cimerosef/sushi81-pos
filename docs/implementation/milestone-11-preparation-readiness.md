# M11 — Gestion intermediate export — preparation/readiness

**Status:** Preparation complete / ready for separate project-owner implementation authorization  
**Date:** 2026-09-20  
**Preparation baseline:** `main` = `299df8b44a1959497ad46f861e44db32913b4d11`  
**Preparation branch:** `prep/m11-gestion-export`  
**Codex execution:** NOT AUTHORIZED by this document

## 1. Verified entry state

GitHub current facts were re-established before this audit:

- M10 PR #22 is CLOSED / MERGED;
- M10 merge / current main baseline is `299df8b44a1959497ad46f861e44db32913b4d11`;
- M10 controller final closure is PR #22 comment `5750090951`;
- accepted M10 runtime candidate remains `34e61c67785aa6c8c0ca84a545e31de30b17ac39`;
- Issue #4 is CLOSED;
- active Codex handoff is none;
- M11 production implementation has not started;
- M12/M13 remain unauthorized.

Some living M10 text on main is stale relative to the already-completed merge. Preparation must reconcile living summaries without rewriting historical evidence.

## 2. Authoritative M11 specification reviewed

The audit returned to the formal sources, including:

- `README.md`, `AGENTS.md`, `docs/README.md`;
- `v1-specification-freeze.md`, `acceptance-criteria.md`;
- `product-requirements.md`, `order-lifecycle.md`, `business-rules.md`, `data-model.md`;
- `architecture.md`, `storage-strategy.md`, `export.md`;
- `implementation-plan.md`, `implementation-status.md`;
- export decisions for date range, eligibility, intermediate file and post-export correction;
- M09 Hiboutik amendment and post-M09 payment-dashboard amendment;
- current SQLite order/payment/snapshot schema and M10 ClosedXML implementation seams.

Primary acceptance ownership is AC-EXP-001 through AC-EXP-011, AC-HIB-008's final export exclusion cross-check, and the export portion of AC-ARCH-005.

## 3. Frozen result after owner clarification

No material M11 decision remains open.

The 2026-09-20 owner decision freezes:

- Closed is the positive-sale lifecycle export gate; Open never exports CREATE/UPDATE;
- existing lifecycle exact-payment reconciliation before Close remains unchanged;
- `SettlementDate` uses actual payment effective business date rather than Close/recording date;
- UPDATE waits until the current order is Closed;
- CANCEL supersedes an un-emitted pending UPDATE;
- CANCEL does not require the cancelled order to be Closed/settled;
- workbook fields remain the fixed versioned four-sheet contract, not per-run selectable columns.

Controlling amendment records:

- `decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `acceptance-criteria-amendment-m11-gestion-export.md`;
- consolidated amended `export.md`.

## 4. Existing seams that can be reused

### Application

Existing `OrderSnapshot` / lifecycle contracts already expose stable order identity, source, lifecycle, fulfilment data, authoritative total, current payment composition, historical line/option snapshots and tax snapshots.

`OrderLifecycleService` centralizes modification, Close, Cancel, payment adjustments, write-authority guarding and post-commit durable-change notification.

### Infrastructure

`SqliteOrderStore` already persists/reads:

- `orders`;
- `order_items`;
- `order_item_adjustments`;
- `order_tax_breakdown`;
- `payment_adjustments`.

M10 already introduced/tested ClosedXML through an application-owned workbook gateway boundary. M11 should use a separate export gateway, not couple to Catalogue workbook DTOs.

### Authority/recovery

Existing `IWriteAuthorityGuard`, SQLite transaction runner, `IDurableChangeNotifier`, local recovery and target-directed handoff/DR architecture are reusable. Export-ledger writes must participate in the authoritative live SQLite database so recovery cannot split order facts from duplicate-protection facts.

## 5. Required new boundaries

### Application

Add an `Export` area containing:

- export selection/query contracts;
- immutable batch/payload DTOs;
- export action enum CREATE/UPDATE/CANCEL;
- preparation/generation/finalization/regeneration orchestration;
- workbook gateway interface;
- validation/result contracts.

### Infrastructure

Add:

- `SqliteGestionExportStore` (or equivalent) for export ledger/payload/selection persistence;
- M11 migration;
- `ClosedXmlGestionExportWorkbookGateway` for V1 four-sheet workbook generation/validation.

### Desktop

Add a narrow Gestion export workflow:

- default all applicable pending actions;
- optional inclusive start/end date;
- visible selected range before execution;
- summary/preview sufficient to understand the batch scope;
- execute export;
- successful-batch history sufficient for exact regeneration;
- FR/zh-CN labels;
- clear read-only/non-authoritative behavior.

No Gestion importer is part of M11.

## 6. SQLite migration is required

Current schema version is 7 and contains no export batch/ledger/payload history. M11 therefore requires a versioned migration (expected version 8).

The migration must retain enough durable technical metadata for:

- unique batch identity;
- PREPARED/SUCCESS/failed-retryable technical state as needed;
- immutable successful emitted payload;
- per-order emitted CREATE/correction identity/state;
- exact regeneration after the live order later changes;
- duplicate protection.

Physical names are implementation choices. Business semantics are not.

## 7. Workbook contract

V1 is fixed:

- `Meta`;
- `Orders`;
- `OrderLines`;
- `TaxBreakdown`;
- `SchemaVersion = 1.0`.

Fields/data types remain those in `export.md`. Operators select order scope, not columns.

CREATE/UPDATE carry complete committed order/line/tax snapshot rows. CANCEL is keyed by stable OrderId and does not require positive line/tax rows.

Historical values must come from persisted sale snapshots, never current Catalogue state.

## 8. Batch identity, immutable payload and exact regeneration

Each prepared business batch receives one opaque stable `BatchId`.

Once its canonical payload is prepared it must not be rebuilt from a later live order version for regeneration.

Exact regeneration:

- uses the same BatchId;
- reproduces the same Meta business metadata and emitted rows;
- does not create a new business export action;
- does not consume/clear later pending corrections;
- does not substitute current Catalogue or later order state.

An internal canonical payload hash/checksum is recommended as a technical integrity measure.

## 9. Correction state model

Required business transitions:

```text
never exported
  -> pre-export edits: still no correction
  -> successful eligible Closed export: CREATE emitted / clean

CREATE or UPDATE emitted / clean
  -> later modification while current state Closed: UPDATE pending/applicable
  -> later modification that leaves/reopens Open: UPDATE pending/not applicable
  -> later Close: UPDATE applicable
  -> successful UPDATE: clean

any previously exported order
  -> Cancelled: CANCEL pending/applicable
  -> pending un-emitted UPDATE is superseded
  -> successful CANCEL: cancellation emitted
```

A successfully emitted historical UPDATE is never erased; a later cancellation is a later CANCEL action.

## 10. Eligibility/date filtering

For first positive export, explicitly include only ordinary `POS` source, current `Closed`, non-Cancelled orders that have no successful CREATE.

Do not implement source eligibility as merely “not Hiboutik”; explicit POS inclusion is safer.

Optional date filtering is inclusive and uses fulfilment/business date. It narrows CREATE and applicable corrections but never changes the underlying pending/export state.

## 11. Hiboutik boundary

M11 must fail closed against anti-double-counting:

- `HIBOUTIK_PASTE` never emits CREATE/UPDATE/CANCEL into Gestion export;
- `source_total_ttc` is not an export financial source;
- post-M09 Hiboutik dashboard values remain separate reporting only;
- no Hiboutik-specific export ledger/workflow is added.

## 12. Failure safety and atomicity

Recommended reliable flow:

1. authority check/write scope where durable export state is created;
2. prepare immutable batch payload in SQLite without marking business actions successful;
3. generate workbook to a temporary/staging path;
4. reopen/validate schema, row relationships, counts and types;
5. finalize to the requested output path without overwriting the only known-good file on failure;
6. in one SQLite transaction mark the batch SUCCESS and contained business actions emitted / correction consumed;
7. notify durable change after commit.

If workbook generation/finalization fails, order business state is unchanged and the action remains retryable.

A crash/retry design must never silently create a second CREATE for the same order.

## 13. Authority, recovery and M12 boundary

Export-ledger/payload facts belong inside the authoritative live SQLite database.

- non-authoritative/read-only devices may inspect existing committed export history if UI exposes it, but cannot create/finalize a new emitted batch;
- export writes use the central authority guard;
- recovery/handoff carries order and export ledger together;
- M11 does not implement annual archive, archive rotation or archived-order browsing/export UI;
- M12 will own archive/historical access.

## 14. Manual acceptance — minimum valuable Windows/Excel checks

Owner acceptance should be short and high-value:

A. Mixed selection / CREATE contract — eligible Closed POS orders export; Open, Cancelled and Hiboutik orders do not; Excel shows four sheets and correct native numeric/date/text cell types.

B. Optional inclusive date range + duplicate protection — boundary dates include correctly; second ordinary export does not repeat successful unchanged CREATE.

C. Exact regeneration — change an order after successful export, regenerate the old batch, confirm same BatchId and original values remain.

D. Correction — exported order modified and re-Closed -> full UPDATE same OrderId; then Cancel -> CANCEL same OrderId, with no unnecessary un-emitted intermediate UPDATE.

E. Failure/retry — force a destination/finalization failure; verify no false-success duplicate-protection state, then retry successfully.

The remaining contract/relationship/failure-injection cases should be automated.

## 15. Readiness disposition

**PASS — M11 is ready for separate project-owner implementation authorization.**

This preparation approval does not authorize Codex production changes, does not open Issue #4, does not authorize merge, and does not authorize M12/M13.
