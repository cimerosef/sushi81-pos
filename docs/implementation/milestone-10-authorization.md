# M10 implementation authorization — Catalogue `.xlsx` import/export

**Status:** **NOT AUTHORIZED**  
**Prepared:** 2026-09-17  
**Readiness finalized:** 2026-09-17  
**Authorized:** NOT GRANTED  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Preparation branch:** `prep/m10-catalogue-xlsx`  
**Exact authorized preparation head:** N/A — no implementation authorization exists  
**Execution gate:** Issue #4 must remain **CLOSED**  
**Active executable handoff:** none

## Current control state

M10 specification/readiness preparation is complete, but implementation remains explicitly unauthorized.

Preparation was started from current `main` at:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

That commit is the merge commit of PR #19 — `Post-M09: Hiboutik daily CB/Espèce dashboard`.

Current governance facts:

- PR #19 is CLOSED / MERGED;
- Issue #18 is CLOSED / completed;
- Issue #4 is CLOSED;
- no executable Codex handoff is active;
- M10 production implementation has not started;
- M11, M12 and M13 are not authorized.

## Category short-code decision — completed

The preparation audit originally found one material operator-visible workbook gap: the later Approved Category `short_code` business field was not defined in the older three-sheet M10 workbook baseline.

On 2026-09-17 the project owner explicitly approved the proposed semantics.

The controlling record is:

`docs/decisions/m10-category-short-code-workbook-semantics.md`

The decision is aligned into:

- `docs/catalogue-management.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/implementation/milestone-10-preparation-readiness.md`;
- `docs/implementation/milestone-10-catalogue-xlsx.md`;
- the prepared owner manual acceptance checklist.

No material M10 owner-decision blocker remains.

## Readiness disposition

`docs/implementation/milestone-10-preparation-readiness.md` now concludes:

**M10 is READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION.**

That conclusion does not grant implementation authority.

## Prepared but non-executable package

The preparation branch contains:

- `milestone-10-preparation-readiness.md`;
- `milestone-10-catalogue-xlsx.md` — prepared implementation contract;
- `milestone-10-final-manual-acceptance.md` — PREPARED / NOT YET EXECUTED;
- `milestone-10-worklog.md`;
- this NOT AUTHORIZED record;
- approved M10 Category-short-code decision/acceptance amendment.

No executable `CODEX_HANDOFF_READY` exists.

## Separate implementation approval still required

Only a new explicit project-owner statement such as:

> 批准 M10 implementation

may change this record to AUTHORIZED.

Approval of the Category short-code workbook semantics was a specification decision only and must not be interpreted as implementation authorization.

## Required gate transition after a future implementation authorization

If and only if the project owner explicitly authorizes M10 implementation, the controller must:

1. record the exact final preparation head in this authorization record;
2. change Status to AUTHORIZED with the owner's explicit approval date/reference;
3. establish/confirm the dedicated M10 implementation branch and PR/mailbox under current governance;
4. publish exactly one complete executable `CODEX_HANDOFF_READY` for the first M10 work package;
5. ensure no conflicting active handoff exists;
6. update Issue #4 to point only to that exact branch/PR/handoff;
7. only then open Issue #4;
8. leave later work packages, merge and M11+ subject to their separate controls.

Until those conditions are met, Issue #4 remains CLOSED and Codex makes no M10 project changes.

## Explicit non-authorization

This record does **not** authorize:

- production implementation;
- adding ClosedXML to production projects yet;
- SQLite/schema changes;
- WPF import/export implementation;
- an executable Codex handoff;
- opening Issue #4;
- merging an M10 implementation PR;
- M11, M12 or M13 work.