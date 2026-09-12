# M08 implementation authorization — printing and reprinting

**Status:** **AUTHORIZED**  
**Prepared:** 2026-09-12  
**Authorized:** 2026-09-12 by explicit project-owner approval  
**Milestone:** M08 — Printing and reprinting  
**Preparation entry baseline:** `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`  
**Exact authorized preparation head:** `b983efa7ef4e2591575fa662f9d433652b97e4aa`  
**Execution gate at authorization record time:** CLOSED — must remain closed until branch/PR/mailbox/handoff pointers are cross-verified  
**Active M08 implementation PR at authorization record time:** none  
**Active handoff at authorization record time:** none

## Project-owner authorization

On 2026-09-12 the project owner explicitly stated:

> 批准 M08 正式实施。

This statement authorizes **M08 implementation only** under the controlling scope below. It does not authorize merge, M09 or any later milestone, and it does not permit Codex execution until the normal branch/PR/mailbox/gate prerequisites are completed.

## Authorized controlling package

Implementation is authorized only within the behavior and boundaries frozen in:

- `docs/implementation/milestone-08-printing-reprinting.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`;
- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-preparation-readiness.md`;
- `docs/implementation/milestone-08-final-manual-acceptance.md`;
- current Approved V1 baseline/acceptance criteria;
- inherited execution/interactive-quality/control-state/power governance under `docs/implementation/`.

The project-owner visual source is the supplied `modèle impression.pdf` as durably translated into the M08 decision/addendum. Kitchen layout follows page 1 semantics; customer layout follows page 2 Hiboutik-style semantics. The exact portable font family is not frozen; the implementation must target the approved narrow monospaced thermal appearance and final physical similarity is owner-accepted.

## Authorized scope summary

M08 owns:

- production deterministic kitchen/customer print models;
- real Windows print queue/spooler integration;
- local kitchen/customer printer queue configuration;
- automatic one-kitchen + one-customer output only after successful new-order durable commit;
- independent output failure/retry behavior;
- explicit kitchen/customer reprint from latest committed live state;
- future-order prominence;
- `RÉIMPRESSION`, `DUPLICATA`, `ANNULÉ` semantics;
- non-authoritative/read-only printing without authority/freshness claims;
- FR/zh-CN operator UI/status/error behavior;
- deterministic failure injection, automated tests and real Windows/manual acceptance;
- authoritative SQLite receipt-identity business settings frozen by the M08 owner decision.

M08 does **not** authorize:

- archive printing/hydration (`AC-PRINT-009`, M12);
- B2B invoice subsystem;
- M09 Hiboutik paste implementation;
- M10 Catalogue `.xlsx`;
- M11 Gestion export;
- M12 annual archive/historical access implementation;
- M13 installer/final acceptance;
- any weakening/change of M07 authority, generation, handoff, DR or write-guard semantics.

## Execution setup required after this authorization record

Before Issue #4 may be opened, the governance controller must complete and cross-check all of the following:

1. create a dedicated M08 implementation branch from the latest governance-only `main` baseline descending from the exact authorized preparation head;
2. create the dedicated M08 PR/mailbox targeting `main`;
3. update Issue #4 to name exactly that branch/PR/milestone while keeping the issue CLOSED;
4. publish exactly one valid unprocessed top-level `CODEX_HANDOFF_READY: <id>` on that PR;
5. include `POST_TASK_POWER_ACTION: NONE` unless the owner explicitly requests another one-shot action;
6. ensure the handoff points to the controlling M08 contract/addendum and forbids M09+;
7. verify no conflicting active implementation PR/handoff exists;
8. only then reopen Issue #4.

Once Issue #4 is OPEN with all pointers consistent, Codex may implement the single active M08 handoff. A `CODEX_DONE` never authorizes merge.
