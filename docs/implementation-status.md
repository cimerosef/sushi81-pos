# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-12  
**Current state:** M01 through M07 are Passed and merged. M08 — Printing and reprinting — is in **Preparation** only; implementation is **NOT AUTHORIZED**. GitHub Issue #4 is CLOSED, no M08 branch/PR/handoff exists, and Codex must not execute M08 until separate project-owner implementation approval is durably translated into the normal gate sequence.

> Historical implementation/evidence detail through M06 is preserved byte-for-byte at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). M07 historical implementation/evidence remains in its milestone contract/worklog/manual-acceptance records and PR #13. This living document records current control state rather than rewriting historical evidence.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring, but implementation is not authorized and Codex must not execute.
- `In progress` — the explicitly authorized milestone is being implemented/revalidated or still has an open gate.
- `Partial` — some evidence exists, but the complete acceptance criterion/gate is not yet satisfied.
- `Passed` — required automated/manual evidence is recorded and passes on the applicable build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — an approved protocol remains safe, but a required technical capability still needs an architecture decision/proof.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable.

Only `Passed` and properly approved `Not applicable — amended` satisfy final V1 acceptance.

## 2. Milestone status

| Milestone | Status | Current authoritative result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged through PR #1; automated and Windows/WPF evidence Passed. |
| M02 — remote handoff feasibility gate | Passed | Original competitive OneDrive model remains historically Blocked; approved target-directed GitHub Release Asset transport revalidation Passed. |
| M03 — Catalogue and settings | Passed | Merged through PR #5; final Windows/WPF acceptance Passed. |
| M04 — Order-entry vertical slice | Passed | Merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`; final Windows/WPF acceptance Passed. |
| M05 — Lifecycle/payments/search/dashboard | Passed | Merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`; accepted production head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`; final Windows/WPF acceptance Passed. |
| M06 — Local recovery/read-only enforcement | Passed | PR #11 merged at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`; accepted production repair head `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`; Release tests 364/364 and project-owner acceptance Passed. |
| M07 — Pairing, target-directed formal handoff and disaster recovery | Passed | PR #13 merged to `main` at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; accepted production implementation head `e971580ef43d3b50366d51733ca9431ca0997e8d`; final closure docs/evidence head `d586c847f2dd541815b8c00565c58b3685a3e4be`; exact-head CI #631 / run `34698627867` succeeded; 541/541 tests Passed; owner Windows/WPF multi-device acceptance Passed. |
| M08 — Printing and reprinting | Preparation | Contract/readiness/worklog/manual checklist prepared. Owner visual/layout and receipt-identity decision is frozen in `decisions/m08-print-layout-and-receipt-identity.md`; implementation remains not authorized; Issue #4 CLOSED; no branch/PR/handoff. |
| M09 — Hiboutik paste fallback | Not started | Pending M08. |
| M10 — Catalogue `.xlsx` | Not started | Pending M09. |
| M11 — Gestion export | Not started | Pending M10. |
| M12 — Annual archive/historical access | Not started | Pending M11. |
| M13 — Installer and final acceptance | Not started | Pending M12. |

## 3. M08 current acceptance ownership

M08 owns the production implementation/evidence for:

| Criterion | Current state | M08 obligation |
|---|---|---|
| AC-LIFE-001 printing cross-check | Preparation | preserve durable commit before any automatic print attempt |
| AC-PRINT-001 | Preparation | automatic kitchen + customer output after successful new-order commit |
| AC-PRINT-002 | Preparation | required kitchen/customer content from committed state |
| AC-PRINT-003 | Preparation | future-order date/time prominence |
| AC-PRINT-004 | Preparation | print failure never rolls back order; identify failed document; independent retry |
| AC-PRINT-005 | Foundation already present / M08 regression | saved modification does not auto-reprint |
| AC-PRINT-006 | Preparation | reprint latest committed state, never unsaved edits |
| AC-PRINT-007 | Preparation | `RÉIMPRESSION` / `DUPLICATA` |
| AC-PRINT-008 | Preparation | Cancelled remains printable with prominent `ANNULÉ` |
| AC-PRINT-010 | Preparation | non-authoritative/read-only device may print local committed copy without freshness/authority claim |
| AC-PRINT-011 | Preparation | no B2B invoice subsystem |
| AC-ARCH-006 | Preparation | deterministic app-owned print model + Windows spooler/queue adapter |

`AC-PRINT-009` archive printing is explicitly deferred to M12.

## 4. M08 preparation decisions

The current controlling M08 package is:

- `implementation/milestone-08-printing-reprinting.md`;
- `implementation/milestone-08-contract-addendum-print-layout-identity.md`;
- `implementation/milestone-08-preparation-readiness.md`;
- `implementation/milestone-08-authorization.md` — NOT AUTHORIZED;
- `implementation/milestone-08-worklog.md`;
- `implementation/milestone-08-final-manual-acceptance.md`;
- `decisions/m08-print-layout-and-receipt-identity.md`.

The owner-selected visual reference resolves the former M08-D1 gap:

- kitchen target = page 1 of the supplied print reference;
- customer target = page 2 Hiboutik-style receipt;
- customer identity = Sushi 81 / 12 Rue Gaston Darley / 77140 Nemours - FRA / SIRET 90805211100014 / TVA FR03908052111 / APE 5610C;
- receipt identity is authoritative SQLite business configuration;
- printer queue selection remains local technical configuration;
- exact Hiboutik typeface is treated as printer-resident visual target and verified on real hardware rather than guessed as a portable font family.

## 5. Current governance gate

Codex must fail closed unless all M08 execution prerequisites are simultaneously true.

Current state is intentionally:

- Issue #4 **CLOSED**;
- M08 implementation authorization: **NOT AUTHORIZED**;
- M08 branch: none;
- M08 PR: none;
- active handoff: none.

A later explicit project-owner approval of **M08 implementation** is required. Only after that approval may the controller:

1. update the authorization record to `AUTHORIZED` with the exact preparation head;
2. create the dedicated M08 branch and PR;
3. update Issue #4 to point to that exact mailbox;
4. publish one valid top-level `CODEX_HANDOFF_READY` record;
5. verify no conflicting/older handoff exists;
6. open Issue #4.

Authorization never authorizes merge or M09.

## 6. Evidence preservation

Historical M01–M07 evidence remains authoritative in its original milestone/PR records. Current-state cleanup must not rewrite historical results merely to make them read as if they had always described later milestones.
