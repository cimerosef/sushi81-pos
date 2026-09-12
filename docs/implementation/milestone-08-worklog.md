# M08 worklog — printing and reprinting

**Status:** **AUTHORIZED / implementation mailbox setup**  
**Prepared:** 2026-09-12  
**Authorized:** 2026-09-12  
**Exact authorized preparation head:** `b983efa7ef4e2591575fa662f9d433652b97e4aa`  
**Authorized governance/execution baseline:** `e46d2a3c0988076a19ea7431bdb65d3d719a54c0`  
**Implementation branch:** `codex/m08-printing-reprinting`  
**Implementation PR:** #14 — `M08: printing and reprinting`  
**Contract:** `docs/implementation/milestone-08-printing-reprinting.md` + `milestone-08-contract-addendum-print-layout-identity.md`  
**Authorization:** `docs/implementation/milestone-08-authorization.md` — AUTHORIZED  
**Execution gate:** CLOSED until mailbox/handoff cross-check completes

This worklog records M08 preparation, authorization, implementation and evidence. Project-owner implementation authorization does not authorize merge or M09.

## 1. Entry state

- M07 — Passed by project-owner final acceptance.
- PR #13 — merged.
- M07 merge commit: `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.
- Accepted M07 production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- Final M07 closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`.
- Final accepted M07 CI: #631 / run `34698627867`, success; 541/541 tests Passed; build 0 warnings / 0 errors.
- M08 exact authorized preparation head: `b983efa7ef4e2591575fa662f9d433652b97e4aa`.
- M08 governance/execution baseline after authorization/current-state bookkeeping: `e46d2a3c0988076a19ea7431bdb65d3d719a54c0`.
- Dedicated branch: `codex/m08-printing-reprinting`.
- Dedicated implementation PR: #14.
- Issue #4 remains CLOSED until the single active handoff is posted and all pointers are verified.
- M09+: not authorized.

## 2. Preparation audit — complete

The current Approved printing/lifecycle/data/architecture/storage/control documents and accepted M04–M07 implementation were audited before authorization.

Key foundation facts:

- M04 `IOrderPrintDispatcher` exists as the post-commit seam;
- `OrderEntryService` already persists + reloads committed state before dispatch;
- dispatcher failure already preserves committed order persistence;
- production still uses `NoOpOrderPrintDispatcher`;
- no production Windows print-queue/spooler adapter exists;
- no kitchen/customer reprint WPF actions exist;
- no printer queue fields exist in local technical configuration;
- M05 modification/payment/lifecycle writes do not auto-print;
- committed `OrderSnapshot` already contains the order/item/adjustment/tax/payment facts needed for deterministic output;
- M07 read-only/authority semantics permit printing as a non-business-write local side effect.

M08 criterion ownership: `AC-PRINT-001`–`008`, `010`, `011`, `AC-ARCH-006`, plus the production printing cross-check of `AC-LIFE-001`. `AC-PRINT-009` remains M12.

## 3. Owner print-reference decision — M08-D1 resolved

On 2026-09-12 the project owner supplied `modèle impression.pdf` in the ChatGPT Sushi81 POS project resources and selected:

- page 1 as the kitchen-ticket visual target, with customer/fulfilment/telephone/address/comment information between the two upper dashed separators;
- page 2 as the customer-ticket visual target, matching the current Hiboutik-style thermal structure/appearance as closely as practical.

Durable controlling records:

- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`.

Frozen customer identity:

- `Sushi 81`;
- `12 Rue Gaston Darley`;
- `77140 Nemours - FRA`;
- SIRET `90805211100014`;
- TVA `FR03908052111`;
- APE/NAF `5610C`.

Receipt identity is authoritative SQLite `BusinessSettings`; printer queues remain local technical configuration. The exact Hiboutik font family is not portable/frozen; M08 targets the same narrow monospaced thermal appearance through the approved Windows printing boundary, with physical similarity judged by the owner.

## 4. Project-owner implementation authorization — 2026-09-12

The project owner explicitly stated:

> 批准 M08 正式实施。

Authorization scope is exactly the M08 contract/addendum/decision/readiness package at exact preparation head `b983efa7ef4e2591575fa662f9d433652b97e4aa`.

This authorization:

- authorizes M08 implementation only;
- does not authorize merge;
- does not authorize M09+;
- does not itself open Issue #4;
- requires the normal dedicated branch/PR/mailbox/handoff gate sequence before Codex execution.

## 5. Technical implementation boundaries

Implementation must preserve:

- durable commit before automatic print;
- independent kitchen/customer model/submission outcomes;
- real Windows print queue/spooler boundary;
- no order rollback on print failure;
- no automatic reprint after existing-order saves;
- latest committed state only for explicit reprint;
- required `RÉIMPRESSION`, `DUPLICATA`, `ANNULÉ` markings;
- future-order prominence;
- non-authoritative local committed-copy printing without authority/freshness implication;
- FR/zh-CN WPF quality/localization requirements;
- M07 authority/generation/handoff/DR/write-guard semantics;
- no M12 archive printing implementation and no M09+ work.

## 6. Execution setup

Controller sequence after authorization:

1. [x] advance governance-only `main` with authorization/current-state records;
2. [x] create dedicated branch `codex/m08-printing-reprinting` from `e46d2a3c0988076a19ea7431bdb65d3d719a54c0`;
3. [x] create dedicated M08 implementation PR #14 targeting `main`;
4. [ ] update Issue #4 pointer while keeping it CLOSED;
5. [ ] publish exactly one top-level executable handoff `CODEX_HANDOFF_READY: M08-IMPLEMENTATION-01` with `POST_TASK_POWER_ACTION: NONE`;
6. [ ] cross-check branch/PR/comment/authorization pointers;
7. [ ] only then reopen Issue #4.

## 7. Implementation evidence

**None yet.**

Codex must not claim production implementation evidence until Issue #4 is OPEN and the matching handoff is active.

`POST_TASK_POWER_ACTION: NONE`
