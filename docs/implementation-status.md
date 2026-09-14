# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-14
**Current state:** M01 through M07 are Passed and merged. M08 — Printing and reprinting — is **Partial**: the R11 production implementation and exact-head CI passed; B-PC Customer R10, B-PC Kitchen R11 and the A-PC automatic-print evidence are reconciled in the closure docs. Final controller/project-owner disposition and separate merge approval remain pending. M09 and later milestones remain unauthorized.

> Historical implementation/evidence through M06 remains preserved byte-for-byte at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). M07 evidence remains authoritative in its milestone-specific worklog/manual-acceptance/PR records. This living document states current control state only.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring, but implementation is not authorized and Codex must not execute.
- `Authorized` — project-owner implementation approval is durable, but Codex still requires the complete branch/PR/mailbox/open-gate execution prerequisites.
- `In progress` — an authorized executable handoff is active under the OPEN Issue #4 gate.
- `Partial` — some evidence exists, but complete acceptance is not yet satisfied.
- `Passed` — required automated/manual evidence is recorded and passes on the applicable accepted build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — an approved protocol remains safe, but a required technical capability still needs an architecture decision/proof.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable.

Only `Passed` and properly approved `Not applicable — amended` satisfy final V1 acceptance.

## 2. Milestone status

| Milestone | Status | Current authoritative result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged through PR #1. |
| M02 — remote handoff feasibility/revalidation | Passed | Approved target-directed GitHub Release Asset transport revalidation Passed. |
| M03 — Catalogue and settings | Passed | Merged through PR #5; final Windows/WPF acceptance Passed. |
| M04 — Order-entry vertical slice | Passed | Merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`; final Windows/WPF acceptance Passed. |
| M05 — Lifecycle/payments/search/dashboard | Passed | Merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`; final Windows/WPF acceptance Passed. |
| M06 — Local recovery/read-only enforcement | Passed | Merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`; final Windows/WPF acceptance Passed. |
| M07 — Pairing, target-directed handoff and disaster recovery | Passed | Project-owner final acceptance recorded on PR #13; accepted production head `e971580ef43d3b50366d51733ca9431ca0997e8d`; final closure docs/evidence head `d586c847f2dd541815b8c00565c58b3685a3e4be`; PR #13 merged to `main` at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; exact-head CI #631 succeeded with 541/541 tests and 0 warnings/errors. |
| M08 — Printing and reprinting | Partial | R11 candidate head `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`; implementation commit `7ab74655977faaf70f95c91188d5a69486a378e6`; exact CI #673 / run `34784638478` / job `103797769200` succeeded with 575/575 tests and 0 warnings/errors; B-PC Customer R10 and Kitchen R11 physical evidence is recorded in PR comment `5656535282`; A-PC automatic-print PASS is recorded in comment `5661259453`; closure evidence is reconciled, while final controller/project-owner disposition and merge approval remain pending. |
| M09 — Hiboutik paste fallback | Not started | Unauthorized; pending M08. |
| M10 — Catalogue `.xlsx` | Not started | Unauthorized; pending M09. |
| M11 — Gestion export | Not started | Unauthorized; pending M10. |
| M12 — Annual archive/historical access | Not started | Unauthorized; pending M11. |
| M13 — Installer and final acceptance | Not started | Unauthorized; pending M12. |

## 3. M08 controlling scope and criteria

M08 controls:

- `AC-PRINT-001` through `AC-PRINT-008`;
- `AC-PRINT-010`;
- `AC-PRINT-011`;
- `AC-ARCH-006`;
- the production printing cross-check of `AC-LIFE-001` commit-before-print;
- the final real-printer/offline cross-check relevant to the local-first product boundary.

`AC-PRINT-009` archived-order printing remains M12.

The controlling M08 preparation/authorization package is:

- `implementation/milestone-08-printing-reprinting.md`;
- `implementation/milestone-08-contract-addendum-print-layout-identity.md`;
- `decisions/m08-print-layout-and-receipt-identity.md`;
- `implementation/milestone-08-preparation-readiness.md`;
- `implementation/milestone-08-authorization.md`;
- `implementation/milestone-08-worklog.md`;
- `implementation/milestone-08-final-manual-acceptance.md`.

The supplied `modèle impression.pdf` is an owner visual source translated durably into the M08 decision/addendum: page 1 semantics control the kitchen-ticket target and page 2 Hiboutik-style semantics control the customer-ticket target. Repository text remains the executable specification; Codex is not required to access the external project-file attachment directly.

## 4. M08 owner-approved receipt/layout decision

The customer receipt identity is frozen as authoritative business configuration:

- `Sushi 81`;
- `12 Rue Gaston Darley`;
- `77140 Nemours - FRA`;
- SIRET `90805211100014`;
- TVA `FR03908052111`;
- APE/NAF `5610C`.

These values belong to authoritative SQLite `BusinessSettings` and follow normal authority/handoff/DR data semantics. Kitchen/customer Windows printer queue selections remain local per-device technical configuration.

The customer visual target is a narrow monospaced thermal-receipt appearance comparable to the owner-supplied Hiboutik sample. The exact Hiboutik printer-resident font is not a frozen portable font-family requirement; physical printed similarity is part of project-owner manual acceptance.

## 5. M08 governance gate

Project-owner implementation authorization is durable in `implementation/milestone-08-authorization.md` and records exact authorized preparation head `b983efa7ef4e2591575fa662f9d433652b97e4aa`.

Codex must still fail closed unless all of the following are simultaneously true:

- Issue #4 is OPEN;
- Issue #4 points to the exact active M08 PR and branch;
- the M08 authorization record exists and is AUTHORIZED;
- the active PR contains exactly one valid unprocessed top-level `CODEX_HANDOFF_READY` for the current task;
- the handoff ID, contract/addendum and branch/PR identity are unambiguous.

Authorization does not authorize merge. M08 evidence is now reconciled and the milestone remains Partial pending final controller/project-owner disposition and separate explicit merge approval. The owner checklist remains governed by the acceptance record; Codex does not exercise owner authority.

M09 must not start during M08.

## 6. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and accepted exact-head evidence remain authoritative in place. Current-state cleanup must not rewrite historical failures/remediation as though they never occurred.
