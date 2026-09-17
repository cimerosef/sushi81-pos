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

## Correction of erroneous authorization state

An implementation branch/PR and authorization wording were created prematurely after the project owner approved only the **Category `short_code` M10 workbook semantics**.

The owner did **not** state `批准 M10 implementation` in the controlling conversation at this stage. Approval of the Category short-code workbook semantics is a specification decision only and must not be interpreted as milestone implementation authorization.

Therefore any earlier M10 record/comment on this branch claiming explicit owner implementation authorization is invalid and superseded by this correction.

## Current control state

M10 specification/readiness preparation is complete, but implementation remains explicitly unauthorized.

Preparation baseline:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

Current governance facts:

- PR #19 is CLOSED / MERGED;
- Issue #18 is CLOSED / completed;
- Issue #4 is CLOSED;
- no valid executable Codex handoff is active;
- M10 production implementation has not started;
- M11, M12 and M13 are not authorized.

## Category short-code decision — completed

On 2026-09-17 the project owner explicitly approved the proposed Category `short_code` workbook semantics.

The controlling decision is:

`docs/decisions/m10-category-short-code-workbook-semantics.md`

The decision closes the last material M10 readiness gap. It does not grant implementation authority.

## Readiness disposition

`docs/implementation/milestone-10-preparation-readiness.md` concludes:

**M10 is READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION.**

That conclusion is not implementation authorization.

## Separate implementation approval still required

Only a new explicit project-owner statement such as:

> 批准 M10 implementation

may change this record to AUTHORIZED.

Until then:

- no production implementation may start;
- ClosedXML must not be added to production projects;
- no executable `CODEX_HANDOFF_READY` is valid;
- Issue #4 remains CLOSED;
- M11/M12/M13 remain unauthorized.

## Required gate transition after a future valid authorization

If and only if the project owner explicitly authorizes M10 implementation, the controller must:

1. record the exact final preparation head in the authorization record;
2. change Status to AUTHORIZED with the owner's explicit approval date/reference;
3. establish/confirm the dedicated M10 implementation branch and PR/mailbox;
4. publish exactly one complete executable handoff for the first M10 work package;
5. update Issue #4 to that unique pointer;
6. only then open Issue #4.

Merge and every later milestone remain separately controlled.