# M10 implementation authorization — Catalogue `.xlsx` import/export

**Status:** **NOT AUTHORIZED**  
**Prepared:** 2026-09-17  
**Authorized:** NOT GRANTED  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Preparation branch:** `prep/m10-catalogue-xlsx`  
**Exact authorized preparation head:** N/A — no implementation authorization exists  
**Execution gate:** Issue #4 must remain **CLOSED**  
**Active executable handoff:** none

## Current control state

This file records that M10 is in specification/readiness preparation only.

Preparation was started from current `main` at:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

That commit is the merge commit of PR #19 — `Post-M09: Hiboutik daily CB/Espèce dashboard`.

Current governance facts at preparation time:

- PR #19 is CLOSED / MERGED;
- Issue #18 is CLOSED / completed;
- Issue #4 is CLOSED;
- no Codex implementation handoff is active;
- M10 is not implementation-authorized;
- M11, M12 and M13 are not authorized.

## Outstanding owner decision before readiness can be finalized

The later Approved Category-short-code amendment requires future Catalogue `.xlsx` support to preserve Category `short_code`, while the older three-sheet workbook specification defines only Category-name representation and intentionally has no `Categories` worksheet.

The preparation audit therefore leaves one material operator-visible workbook decision for the project owner:

- how `Category short code` is represented and whether/how import may change it.

The recommended contract is recorded in:

`docs/implementation/milestone-10-preparation-readiness.md`

No implementation agent may invent or implement that behavior before owner approval.

## Prepared but non-executable package

The preparation branch contains:

- `milestone-10-preparation-readiness.md`;
- `milestone-10-catalogue-xlsx.md` — DRAFT / PREPARATION ONLY;
- `milestone-10-final-manual-acceptance.md` — PREPARED / NOT YET EXECUTED;
- `milestone-10-worklog.md`;
- this NOT AUTHORIZED record.

These documents do not grant Codex execution permission.

## What owner approval is still required

There are two separate approvals and they must not be conflated.

### 1. Category-short-code workbook decision

The owner must first approve or replace the proposed Category `short_code` workbook semantics. That approval permits the controller to record the corresponding specification decision/alignment and finalize M10 readiness.

It does **not** authorize implementation.

### 2. M10 implementation authorization

Only after readiness is complete may the owner separately state an explicit authorization such as:

> 批准 M10 implementation

Only that separate approval may cause this record to become AUTHORIZED.

Even after authorization, Codex still may not execute until the normal dedicated implementation branch/PR/mailbox and Issue #4 active-handoff prerequisites are complete.

## Required gate transition after a future authorization

If and only if the project owner later explicitly authorizes M10 implementation, the controller must:

1. finalize the exact preparation head and record it here;
2. establish/confirm the dedicated M10 implementation branch and PR/mailbox under the then-current governance;
3. publish exactly one complete executable `CODEX_HANDOFF_READY` for the first authorized M10 work package;
4. ensure no conflicting active handoff exists;
5. update Issue #4 to point only to that exact branch/PR/handoff;
6. then open Issue #4;
7. leave merge and every later work package/milestone subject to their own controls.

Until all of those conditions are met, Issue #4 remains CLOSED and Codex makes no M10 project changes.

## Explicit non-authorization

This record does **not** authorize:

- production implementation;
- adding ClosedXML to production projects yet;
- SQLite/schema changes;
- WPF import/export implementation;
- creating an executable Codex handoff;
- opening Issue #4;
- merging any M10 PR;
- M11, M12 or M13 work.