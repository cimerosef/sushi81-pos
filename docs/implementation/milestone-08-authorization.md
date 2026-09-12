# M08 implementation authorization — printing and reprinting

**Status:** Prepared / readiness complete — **NOT AUTHORIZED**  
**Prepared:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Preparation entry baseline:** `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`  
**Execution gate:** CLOSED  
**Active M08 implementation PR:** none  
**Active handoff:** none

## Purpose

This record exists so M08 preparation can be reviewed durably without being mistaken for implementation authorization.

M07 is Passed and merged through PR #13. M08 preparation/readiness is now complete, but Codex implementation remains forbidden until the project owner separately and explicitly authorizes **M08 implementation**.

## Current preparation state

- GitHub Issue #4 is CLOSED;
- no M08 implementation branch exists;
- no M08 implementation PR exists;
- no `CODEX_HANDOFF_READY` exists for M08;
- no local/Codex implementation change is authorized;
- M09+ remain unauthorized.

The former material preparation gap `M08-D1` is resolved by:

- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`.

The complete readiness audit is `docs/implementation/milestone-08-preparation-readiness.md`, whose current disposition is **ready for project-owner implementation authorization**.

## Scope a future explicit authorization will approve

A later project-owner statement approving **M08 implementation** authorizes only the implementation scope frozen in:

- `docs/implementation/milestone-08-printing-reprinting.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`;
- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-preparation-readiness.md`;
- current Approved V1 baseline/acceptance criteria.

It never authorizes merge, M09, or scope expansion.

After explicit owner implementation approval, the governance controller must:

1. update this record to `AUTHORIZED`;
2. record the exact authorized preparation `main` head;
3. create the dedicated M08 implementation branch from that head;
4. create the dedicated M08 PR/mailbox;
5. update Issue #4 pointer to that exact branch/PR;
6. publish one valid top-level `CODEX_HANDOFF_READY: <id>` with `POST_TASK_POWER_ACTION: NONE` unless the owner explicitly requests otherwise;
7. verify no conflicting/older unprocessed handoff exists;
8. only then set Issue #4 OPEN.

A `CODEX_DONE` never authorizes merge.
