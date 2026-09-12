# M08 implementation authorization — printing and reprinting

**Status:** Prepared — **NOT AUTHORIZED**  
**Prepared:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Preparation entry baseline:** `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`  
**Execution gate:** CLOSED  
**Active M08 implementation PR:** none  
**Active handoff:** none

## Purpose

This record exists so M08 preparation can be reviewed durably without being mistaken for implementation authorization.

M07 is Passed and merged through PR #13. M08 preparation is therefore allowed, but Codex implementation remains forbidden until the project owner explicitly authorizes M08 implementation after the preparation/readiness review and all material specification gaps are resolved.

## Current non-authorization conditions

At preparation time:

- GitHub Issue #4 is CLOSED;
- no M08 implementation branch exists;
- no M08 implementation PR exists;
- no `CODEX_HANDOFF_READY` exists for M08;
- no local/Codex implementation change is authorized;
- M09+ remain unauthorized.

The current material preparation gap is the customer-ticket receipt identity block: Approved requirements mandate Sushi 81 business/statutory identity and VAT identification on the customer ticket, but GitHub does not yet freeze the exact identity values or authoritative settings/storage model.

## What an explicit future authorization must mean

A later project-owner statement approving **M08 implementation** authorizes only the implementation scope frozen in:

- `docs/implementation/milestone-08-printing-reprinting.md`;
- any later owner-approved M08 decision/amendment resolving the receipt-identity gap;
- current Approved V1 baseline/acceptance criteria.

It never authorizes merge, M09, or scope expansion.

After explicit owner implementation approval, the governance controller must update this file to `AUTHORIZED`, record the exact authorized preparation head, and only then establish the dedicated M08 branch/PR/mailbox/handoff sequence.

## Execution prerequisites after future approval

Before opening Issue #4, verify all of the following:

1. exact authorized preparation `main` head is recorded;
2. dedicated M08 implementation branch exists from that head;
3. dedicated M08 PR exists and remains open/unmerged;
4. Issue #4 pointer names exactly that PR/branch/milestone;
5. one valid top-level `CODEX_HANDOFF_READY: <id>` exists on that PR;
6. no older unprocessed handoff exists;
7. `POST_TASK_POWER_ACTION` is explicit (default `NONE`);
8. only then set Issue #4 OPEN.

A `CODEX_DONE` never authorizes merge.
