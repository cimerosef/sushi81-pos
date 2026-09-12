# M08 worklog — printing and reprinting

**Status:** Preparation only — **IMPLEMENTATION NOT AUTHORIZED**  
**Prepared:** 2026-09-12  
**Preparation entry baseline:** `main@9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`  
**Contract:** `docs/implementation/milestone-08-printing-reprinting.md`  
**Authorization:** `docs/implementation/milestone-08-authorization.md` — NOT AUTHORIZED  
**Execution gate:** CLOSED

This worklog records M08 preparation and, only after later explicit authorization, implementation/evidence. Preparation entries do not authorize Codex.

## 1. Entry state

- M07 — Passed by project-owner final acceptance.
- PR #13 — merged.
- M07 merge commit: `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.
- Accepted M07 production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- Final M07 closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`.
- Final accepted M07 CI: #631 / run `34698627867`, job `103566341254`, success; 541/541 tests Passed; build 0 warnings / 0 errors.
- Issue #4: CLOSED.
- Active handoff: none.
- M08 implementation branch: none.
- M08 implementation PR: none.
- M09+: not authorized.

## 2. Preparation audit — 2026-09-12

### Specifications read

Current `main` was used to re-read the frozen/amended baseline relevant to M08, including printing, lifecycle, data, architecture, storage, current implementation control and accepted M07 evidence.

### Criterion ownership

M08 owns `AC-PRINT-001`–`008`, `010`, `011`, `AC-ARCH-006`, and the production-printing cross-check of `AC-LIFE-001`.

`AC-PRINT-009` archive printing remains M12.

### Production foundation findings

- Existing M04 `IOrderPrintDispatcher` seam found.
- `OrderEntryService` already commits + reloads before dispatcher invocation.
- Dispatcher exception already preserves order persistence success.
- Production still uses `NoOpOrderPrintDispatcher`.
- No production Windows print-queue/spooler adapter exists.
- No current kitchen/customer reprint WPF commands exist.
- No printer fields exist in `LocalConfiguration`.
- M05 modification/payment/lifecycle writes do not auto-print.
- Current committed `OrderSnapshot` is sufficient for order/item/adjustment/tax/payment print content without current Catalogue dependency.
- M07 non-authoritative/read-only warning and authority guard provide the required boundary; printing must remain outside the business-write permission gate.

### Material preparation blocker

`M08-D1`: customer ticket business/statutory identity and VAT-identification block is required by Approved spec, but exact authoritative values/storage source are not frozen in GitHub.

Recommended resolution: owner freezes the exact identity block and, if editable/changeable, stores it as authoritative business settings rather than per-device printer config.

### Technical proposal prepared

Prepared contract freezes the proposed Windows/WPF boundary, independent kitchen/customer outcomes, local queue configuration, explicit reprint semantics, Cancelled/future marks, read-only printing behavior, failure injection, automated test matrix, real Windows acceptance and parallelization boundaries.

## 3. Governance state after preparation commits

Preparation documentation may advance `main` beyond the M07 merge commit. Those documentation-only commits do not authorize implementation.

Codex must continue to report:

`M08_NOT_AUTHORIZED: execution gate closed / no active handoff.`

until a later explicit owner authorization is durably translated into the normal branch/PR/mailbox/gate sequence.

## 4. Implementation entries

**None.**

Do not add production implementation evidence here until:

- M08-D1 is resolved;
- project owner explicitly approves M08 implementation;
- the authorization record is updated to Authorized;
- a dedicated M08 branch/PR exists;
- a valid top-level M08 `CODEX_HANDOFF_READY` exists;
- Issue #4 is OPEN.

## 5. Current next action

1. Obtain project-owner decision for M08-D1.
2. Record that decision in GitHub and reconcile current-state control docs.
3. Present the final preparation package for explicit M08 implementation approval.

`POST_TASK_POWER_ACTION: NONE`
