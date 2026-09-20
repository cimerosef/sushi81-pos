# M11 — Gestion intermediate export — authorization

**Status:** AUTHORIZED  
**Authorized:** 2026-09-20 by project owner  
**Implementation branch:** `codex/m11-gestion-export-authorized`  
**Implementation PR/mailbox:** #24 — `M11: Gestion intermediate export`  
**Finalized preparation head carried forward:** `a8df4a6c671de7e0050a539e156e15caa9c791f8`

## Owner authorization

The project owner explicitly stated:

> 批准 M11 implementation

This authorizes controlled M11 implementation under the existing repository execution governance. It does not authorize unrestricted milestone execution.

## Authorized milestone scope

M11 may implement:

- export selection/eligibility and optional inclusive fulfilment-date filtering;
- versioned four-sheet Gestion intermediate `.xlsx` contract;
- export batch/ledger persistence and immutable payload;
- duplicate protection and exact regeneration;
- SettlementDate semantics from the approved M11 clarification;
- CREATE / UPDATE / CANCEL correction behavior;
- safe temporary generation, validation and finalization;
- narrow Desktop export workflow;
- M11 integration hardening and final owner candidate.

The controlling behavior remains in:

- `docs/export.md`;
- `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `docs/acceptance-criteria-amendment-m11-gestion-export.md`;
- `docs/implementation/milestone-11-preparation-readiness.md`;
- `docs/implementation/milestone-11-gestion-export.md`.

## Execution remains fail-closed

Authorization alone does not permit Codex to modify the project.

Each executable package requires all of:

1. exact dedicated implementation branch;
2. dedicated implementation PR/mailbox;
3. Issue #4 OPEN;
4. matching active `CODEX_HANDOFF_READY` in PR #24 and Issue #4;
5. exact starting head;
6. explicit package scope/tests/evidence/stop conditions.

Codex may execute only the exact current handoff. A `CODEX_DONE` does not auto-authorize the next work package.

## Current authorized sequence

Implementation split:

1. WP1 — export state model, migration, selection and Application contracts;
2. WP2 — ClosedXML workbook, validation, safe finalization and exact regeneration;
3. WP3 — Desktop export workflow;
4. WP4 — integration hardening and owner candidate.

WP1 and WP2 are controller-accepted. WP3 may be made executable only by its own exact handoff. WP4 remains non-executable until later controller review/handoff.

## Hard boundaries

M11 authorization does not authorize:

- PR merge;
- M12 annual archive/historical access;
- M13 installer/final packaging;
- downstream Gestion importer;
- weakening Hiboutik exclusion;
- changing ordinary order-close/payment lifecycle semantics.

Merge remains a separate explicit project-owner decision after final M11 acceptance and controller closure.
