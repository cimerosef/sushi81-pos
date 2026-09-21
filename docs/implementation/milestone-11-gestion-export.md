# M11 — Gestion intermediate export — implementation contract

**Status:** Prepared / NOT YET AUTHORIZED  
**Milestone:** M11  
**Preparation baseline:** `299df8b44a1959497ad46f861e44db32913b4d11`

## 1. Objective

Implement the frozen Sushi81 POS -> controlled intermediate `.xlsx` -> later manual Gestion import boundary.

M11 owns selection, immutable export history, duplicate protection, exact regeneration, CREATE/UPDATE/CANCEL corrections, safe workbook generation/finalization and the narrow Desktop workflow.

It does not implement the downstream Gestion importer, annual archive/historical access (M12), or installer/final packaging (M13).

## 2. Authoritative behavior

Use:

- `docs/export.md`;
- `docs/decisions/export-date-range.md`;
- `docs/decisions/export-eligibility.md`, as clarified by the later M11 decision;
- `docs/decisions/export-intermediate-file.md`;
- `docs/decisions/export-post-export-correction.md`;
- `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `docs/acceptance-criteria.md`;
- `docs/acceptance-criteria-amendment-m11-gestion-export.md`;
- M09/post-M09 amendments for Hiboutik exclusion.

Where older wording differs narrowly, the later approved M11 decision/amendment controls.

## 3. Work packages

### WP1 — Export state model, migration, selection and application contracts

Scope:

- add M11 versioned SQLite migration;
- add Application export DTO/contracts/services without ClosedXML dependency leakage;
- add durable batch/immutable payload/per-order emission/correction metadata;
- implement selection for CREATE/UPDATE/CANCEL;
- implement Closed gate, optional inclusive fulfilment-date filtering and explicit POS source inclusion;
- implement SettlementDate from effective-dated payment facts, including zero-total Closed handling;
- implement UPDATE pending/applicability and CANCEL precedence, including last-emitted-snapshot anchoring;
- preserve authority/recovery semantics;
- no Desktop UI and no workbook generation beyond test doubles.

Required evidence:

- migration-upgrade and migration-failure tests;
- selection matrix for source/status/date/export-history;
- settlement-date tests including back-dated recording and zero-net bucket reclassification;
- CREATE duplicate protection;
- pre-export edit remains CREATE;
- post-export modification UPDATE state;
- Open UPDATE waits;
- CANCEL supersedes un-emitted UPDATE and uses last successful positive emitted snapshot/date;
- Hiboutik never creates export events;
- write-authority blocked-path test;
- recovery/business-revision compatibility.

### WP1 technical execution detail

WP1 should prefer a simple derived-correction ledger rather than coupling every order mutation to a second lifecycle subsystem.

Recommended invariant:

- successful export emissions are durable facts;
- current pending UPDATE/CANCEL is derived from the current committed order plus the last successful export emission;
- a current positive-snapshot hash equal to the last successful positive-snapshot hash means no UPDATE even if the order was edited and restored;
- no successful CREATE/UPDATE/CANCEL fact is written until the later workbook/finalization stage reports success.

Expected migration version: **8**.

A simple reliable physical model may use:

1. an export-batch table containing BatchId, schema version, generated metadata, filter metadata, status, immutable canonical payload, payload hash and counts;
2. an export-emission table keyed to successful batch/order/action, retaining the per-order canonical positive snapshot/hash for CREATE/UPDATE so later duplicate/update/cancel decisions do not depend on current Catalogue state.

Physical names may differ, but these invariants must remain.

Selection algorithm:

- no prior successful emission + current POS Closed/non-Cancelled -> CREATE;
- no prior successful emission + current Open/Cancelled/Hiboutik -> no action;
- last successful action CANCEL -> no later action;
- last successful positive action + current Cancelled -> CANCEL anchored to that last positive emitted snapshot;
- last successful positive action + current Open -> no emitted action yet;
- last successful positive action + current Closed + canonical positive snapshot differs -> UPDATE;
- last successful positive action + current Closed + canonical positive snapshot equal -> no action;
- HIBOUTIK_PASTE -> never any Gestion action.

Date filter:

- CREATE/UPDATE: current candidate fulfilment date;
- CANCEL: last successful positive emitted fulfilment date.

SettlementDate:

- positive total: effective business date at which cumulative signed CB+Espèce reaches the committed total;
- zero total: business-local ClosedAt date;
- positive-total Closed invariant failure: blocking export diagnostic, never guessed.

WP1 must keep ClosedXML out of Application and must not add Desktop UI.

### WP2 — ClosedXML workbook, validation, safe finalization and exact regeneration

Scope:

- implement the four-sheet schema version 1.0;
- native Excel types and fixed unlocalized schema identifiers;
- CREATE/UPDATE full snapshots and CANCEL shape;
- temporary/staging generation;
- validation before SUCCESS marking;
- safe finalization;
- exact successful-batch regeneration from immutable payload;
- failure injection/retry.

Required evidence:

- sheet/header/schema tests;
- cell-type tests;
- parent/child/count validation;
- snapshot fidelity after current Catalogue changes;
- immutable old-batch regeneration after order changes;
- same BatchId on regeneration;
- generation/finalization failures do not mark actions successful;
- no partial business-state mutation;
- existing good output not destroyed by later failed generation.

### WP3 — Desktop export workflow

Scope:

- Gestion export entry point;
- default all applicable pending actions;
- optional inclusive start/end dates visibly shown before execution;
- useful selection summary/preview;
- export destination/file workflow;
- successful batch history and exact regeneration command;
- authority/read-only state;
- FR/zh-CN localization;
- responsive, non-blocking WPF behavior.

No per-column selection UI.

Required evidence:

- application/presentation tests;
- STA/WPF rendering/interaction tests where valuable;
- localization parity;
- authority-disabled action behavior;
- no broad unrelated shell redesign.

### WP4 — Integration hardening and owner candidate

Scope:

- end-to-end CREATE/UPDATE/CANCEL;
- M09 Hiboutik exclusion cross-check;
- recovery/authority regression;
- exact regeneration;
- failure/retry;
- full Release regression;
- self-contained win-x64 owner candidate;
- candidate hashes and exact-head CI.

No M12/M13 code.

## 4. Technical direction delegated to implementation

Provided frozen behavior is preserved, implementation may choose:

- physical table/index names;
- internal DTO/class names;
- canonical payload serialization shape inside SQLite;
- checksum/hash algorithm;
- temp filename mechanics;
- low-level atomic file move/replace mechanics;
- query/index optimization;
- exact WPF layout details.

Priority remains reliability > simplicity > maintainability > operational clarity > novelty.

## 5. Hard stop conditions

Stop the affected path and report rather than guess if:

- a workbook field cannot be derived from approved persisted facts;
- successful export/finalization atomicity cannot be made retry-safe under the approved semantics;
- a required change would weaken authority/recovery or create split-brain export history;
- a new business decision is required about downstream Gestion importer behavior;
- implementing M11 would require M12 archive semantics;
- an incompatible workbook contract change appears necessary.

## 6. Draft first execution handoff — NON-EXECUTABLE UNTIL AUTHORIZED

The first executable task, once the project owner separately authorizes M11 implementation and the dedicated implementation branch/PR/Issue #4 controls are established, will be **WP1 only**.

Expected handoff ID pattern:

`M11-WP1-EXPORT-STATE-SELECTION-01`

Required start head will be the exact finalized preparation head carried into the dedicated implementation branch.

The handoff must require migration/application/SQLite selection/correction evidence and must explicitly forbid ClosedXML production generation, Desktop UI, M12/M13 and merge.

No `CODEX_HANDOFF_READY` marker may be published from this preparation document/PR.
