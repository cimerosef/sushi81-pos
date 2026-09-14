# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-14  
**Current state:** M01 through M08 are Passed and merged. M09 — Hiboutik paste-order fallback — is **Authorized** by explicit project-owner approval. Dedicated implementation branch `codex/m09-hiboutik-paste-fallback` and PR #17 exist. Codex execution remains gated by Issue #4 and may begin only after the complete queued M09 handoff exists and the owner reopens the gate. M10 and later milestones remain unauthorized.

> Historical implementation/evidence through M06 remains preserved byte-for-byte at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). M07/M08 evidence remains authoritative in milestone-specific worklogs/manual-acceptance/PR records. This living document states current control state only.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring or complete, but implementation is not authorized and Codex must not execute.
- `Authorized` — project-owner implementation approval is durable, but Codex still requires the complete branch/PR/mailbox/open-gate execution prerequisites.
- `In progress` — an authorized executable handoff is active under the OPEN Issue #4 gate.
- `Partial` — some evidence exists, but complete acceptance is not yet satisfied.
- `Passed` — required automated/manual evidence is recorded and passes on the applicable accepted build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — an approved protocol remains safe, but a required technical capability still needs an architecture decision/proof.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable/replaces it.

Only `Passed` and properly approved `Not applicable — amended` satisfy final V1 acceptance.

## 2. Milestone status

| Milestone | Status | Current authoritative result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged through PR #1. |
| M02 — Remote handoff feasibility/revalidation | Passed | Approved target-directed GitHub Release Asset transport revalidation Passed. |
| M03 — Catalogue and settings | Passed | Merged through PR #5; final Windows/WPF acceptance Passed. |
| M04 — Order-entry vertical slice | Passed | Merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`; final Windows/WPF acceptance Passed. |
| M05 — Lifecycle/payments/search/dashboard | Passed | Merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`; final Windows/WPF acceptance Passed. |
| M06 — Local recovery/read-only enforcement | Passed | Merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`; final Windows/WPF acceptance Passed. |
| M07 — Pairing, target-directed handoff and disaster recovery | Passed | Accepted production head `e971580ef43d3b50366d51733ca9431ca0997e8d`; closure docs/evidence head `d586c847f2dd541815b8c00565c58b3685a3e4be`; PR #13 merged to `main` at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; exact-head CI #631 succeeded with 541/541 tests and 0 warnings/errors. |
| M08 — Printing and reprinting | Passed | Accepted production candidate `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`; production implementation commit `7ab74655977faaf70f95c91188d5a69486a378e6`; final pass-record head `5f8c92c29116e17a3365ecd8801f7e13f107269c`; exact-head CI #675 succeeded; project-owner physical/manual acceptance Passed; PR #14 merged to `main` at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`. |
| M09 — Hiboutik paste fallback | Authorized | Preparation/specification package complete. Project owner explicitly authorized implementation on 2026-09-14. Durable authorization: `implementation/milestone-09-authorization.md`. Active branch `codex/m09-hiboutik-paste-fallback`; active PR/mailbox #17. Codex execution remains blocked while Issue #4 is CLOSED. |
| M10 — Catalogue `.xlsx` | Not started | Unauthorized; pending M09. |
| M11 — Gestion export | Not started | Unauthorized; pending M10. |
| M12 — Annual archive/historical access | Not started | Unauthorized; pending M11. |
| M13 — Installer and final acceptance | Not started | Unauthorized; pending M12. |

## 3. M08 closure baseline

M08 is no longer open work.

Authoritative closure facts:

- PR #14 — `M08: printing and reprinting` — CLOSED / MERGED;
- merge commit: `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`;
- accepted production candidate: `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`;
- final pass-record head: `5f8c92c29116e17a3365ecd8801f7e13f107269c`;
- final exact-head CI #675: SUCCESS;
- B-PC Customer R10, B-PC Kitchen R11 and A-PC automatic-print evidence: accepted;
- controller final-pass disposition: accepted before owner merge approval.

Any older living-status/Issue wording stating “M08 merge-ready / PR #14 not yet merged” is superseded by GitHub merge metadata and this reconciled current-state record. Historical M08 evidence is not rewritten.

## 4. M09 controlling scope and approved amendment

M09 primary acceptance ownership remains AC-HIB-001 through AC-HIB-009, as amended by:

- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`.

The consolidated operational specification is `paste-order-import.md` (amended 2026-09-14).

The approved and authorized M09 control package is:

- `implementation/milestone-09-preparation-readiness.md`;
- `implementation/milestone-09-hiboutik-paste-fallback.md`;
- `implementation/milestone-09-final-manual-acceptance.md`;
- `implementation/milestone-09-authorization.md`.

Key frozen M09 semantics:

- operator pastes the Hiboutik product-detail block directly;
- per-item source `Total` lines/final `TOTAL` are tolerated without manual cleanup;
- exact-code automatic product resolution only;
- unknown/material lines become explicit unresolved state;
- every unresolved line requires operator product selection or explicit ignore before confirmation;
- ordinary option workflow is reused;
- ordinary order-level fields are entered manually;
- ordinary POS pricing remains authoritative;
- nullable read-only `source_total_ttc` may retain a reliably determined Hiboutik source amount for reconciliation only;
- passive read-only `Hiboutik` source identification is allowed in ordinary order list/detail;
- anti-double-counting exclusions remain unchanged;
- no old emergency-order/discrepancy/duplicate/payment subsystem is reintroduced.

A synthetic structural source sample is recorded at:

`samples/pasted-orders/hiboutik-product-block-synthetic.txt`

No real customer/order screenshots or production details are committed.

## 5. M09 readiness/authorization/gate state

Preparation audit conclusion:

- M08 dependency: satisfied/merged;
- M09 business specification: frozen;
- real Hiboutik product-block source structure: verified by owner examples and translated to synthetic fixture;
- implementation seams: available in ordinary order entry/catalogue/options/pricing/authority/recovery/M08 printing;
- material M09 questions: none open;
- readiness: PASS;
- implementation authorization: **AUTHORIZED by project owner on 2026-09-14**;
- branch: `codex/m09-hiboutik-paste-fallback`;
- PR/mailbox: #17.

Authorization is not execution permission by itself. While Issue #4 remains CLOSED, Codex makes no M09 production-code changes.

Before Codex may execute M09:

- Issue #4 must point to branch `codex/m09-hiboutik-paste-fallback` and PR #17;
- exactly one complete unprocessed top-level `CODEX_HANDOFF_READY` must be queued on PR #17;
- Issue #4 must then be reopened by the owner/operator.

M10+ remain unauthorized.

## 6. M09 execution topology

Authorized topology:

- branch: `codex/m09-hiboutik-paste-fallback`;
- PR: `M09: Hiboutik paste-order fallback` (#17);
- PR top-level Conversation comments: durable mailbox;
- first work package: WP1 — domain/data migration and exact-code seam;
- prepare/publish the first complete executable handoff while Issue #4 remains CLOSED;
- then ask the owner to reopen Issue #4 so Codex can immediately consume the queued work;
- Codex processes only authorized M09 work packages serially;
- completion of a work package never authorizes the next milestone or merge;
- merge requires separate explicit owner approval.

## 7. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and accepted exact-head evidence remain authoritative in place. Current-state cleanup must not rewrite historical failures/remediation as though they never occurred.