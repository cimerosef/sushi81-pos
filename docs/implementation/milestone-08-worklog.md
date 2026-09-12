# M08 worklog — printing and reprinting

**Status:** **CONTROLLER REMEDIATION COMPLETE — owner manual acceptance pending**
**Prepared:** 2026-09-12  
**Authorized:** 2026-09-12  
**Exact authorized preparation head:** `b983efa7ef4e2591575fa662f9d433652b97e4aa`  
**Authorized governance/execution baseline:** `e46d2a3c0988076a19ea7431bdb65d3d719a54c0`  
**Implementation branch:** `codex/m08-printing-reprinting`  
**Implementation PR:** #14 — `M08: printing and reprinting`  
**Contract:** `docs/implementation/milestone-08-printing-reprinting.md` + `milestone-08-contract-addendum-print-layout-identity.md`  
**Authorization:** `docs/implementation/milestone-08-authorization.md` — AUTHORIZED  
**Execution gate:** OPEN — M08-CONTROLLER-REMEDIATION-02 is the active authorized handoff

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
- Issue #4 is OPEN for the single active handoff M08-CONTROLLER-REMEDIATION-02; the earlier M08-IMPLEMENTATION-01 is complete and is not being reprocessed.
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

## 6. Execution setup and handoff cross-check

The authorized serial-execution prerequisites are complete:

1. [x] governance-only main records and current-state pointers established;
2. [x] dedicated branch codex/m08-printing-reprinting created from the authorized baseline;
3. [x] dedicated M08 implementation PR #14 targets main;
4. [x] Issue #4 names the branch/PR and is OPEN;
5. [x] the historical top-level executable handoff CODEX_HANDOFF_READY: M08-IMPLEMENTATION-01 was consumed exactly once;
6. [x] branch/PR/comment/authorization pointers cross-checked;
7. [x] serial oldest-first execution has consumed only M08-IMPLEMENTATION-01.

## 7. Initial implementation evidence — completed handoff

The initial authorized implementation was completed on codex/m08-printing-reprinting, PR #14. The branch contains only the M08 printing/reprinting implementation and its focused evidence; no M09+ work has started.

Implemented seams/evidence so far:

- deterministic Application kitchen/customer document models with committed historical facts, future-order prominence and RÉIMPRESSION/DUPLICATA/ANNULÉ markings;
- authoritative SQLite receipt identity migration v6 and pre-v6 settings-store compatibility;
- local technical kitchen/customer Windows queue configuration;
- Windows/WPF System.Printing queue enumeration and fixed-document submission boundary;
- post-commit automatic dispatch wiring and independent output outcomes;
- latest-committed explicit kitchen/customer reprint actions;
- FR/zh-CN printer setup, reprint and failure/status localization;
- focused Application and Infrastructure M08 tests.

Final automated evidence for the initial implementation candidate, before the later controller remediation:

- implementation/evidence head: `4e5d9b39d4db4ad55e2f7ee6bc83e0c4d89a4ca8`;
- Release build: **Passed**, 0 warnings / 0 errors;
- Release tests: **Passed**, 546/546;
- focused M08 tests: **Passed**, 4 Application + 1 Infrastructure integration;
- self-contained win-x64 publish: **Passed**, `artifacts/m08-win-x64`;
- published executable SHA-256: `A2FB8591A4FFAD67046FBC0886249E21122A806C4F54387209DE2C4837FD2B97`;
- exact-head CI: **Passed**, run #654 / workflow run `34704258456`, job build-and-test;
- owner Windows/physical-printer acceptance: **Pending / must remain unchecked**.

No merge, no M09+ implementation, and POST_TASK_POWER_ACTION: NONE.

## 8. Controller remediation — M08-CONTROLLER-REMEDIATION-02

The active controller remediation was limited to the scope recorded in Issue #4 and PR review `5187117563`:

- local technical configuration writers now use an atomic latest-snapshot update path, preserving unrelated language, printer and M07 fields;
- known failed Kitchen/Customer initial output can be retried independently with the same initial intent; ambiguous output remains on the explicit `RÉIMPRESSION`/`DUPLICATA` path;
- Windows printing now executes on a dedicated STA thread, reads the selected queue imageable area and paginates content to that bounded surface instead of using a hardcoded page size;
- duplicate customer `ANNULÉ` output was removed;
- FR/zh-CN initial-retry controls and evidence were added.

Remediation implementation commit: `50a6245e0818461a51662fe7a97fdef47d8c433d`.

Remediation evidence:

- Release build: **Passed**, 0 warnings / 0 errors;
- full Release tests: **Passed**, 550/550;
- focused M08 application tests: **Passed**, 6/6;
- focused M08 infrastructure tests: **Passed**, 2/2;
- focused localization/configuration tests: **Passed**, 6/6;
- `git diff --check`: **Passed**;
- owner Windows/physical-printer acceptance: **Pending / must remain unchecked**.

The final pushed head and exact-head CI result are recorded in the matching `CODEX_DONE: M08-CONTROLLER-REMEDIATION-02` PR comment. No merge, no M09+ implementation, and `POST_TASK_POWER_ACTION: NONE`.

`POST_TASK_POWER_ACTION: NONE`
