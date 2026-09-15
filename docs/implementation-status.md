# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-15
**Current state:** M01 through M08 are Passed and merged. M09 — Hiboutik paste-order fallback — is **Partial**: WP1 through WP5 implementation/evidence and WP6 candidate preparation were accepted; owner checklists A through E passed on replacement candidate `a18cca2ad51d3676bc9fc2a99d416ad82a1f1a7a`, but acceptance is paused at checklist F after the payment-only manual-total defect and a controller-found quantity-revert edge case. The active narrow remediation is `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`; a replacement exact candidate is required before owner acceptance can resume. Separate merge approval remains pending. M10 and later milestones remain unauthorized.

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
| M09 — Hiboutik paste fallback | Partial | WP1–WP5 implementation/evidence and WP6 preparation were accepted; checklist A’s reset defect was remediated and A–E passed on `a18cca2ad51d3676bc9fc2a99d416ad82a1f1a7a`. Checklist F’s payment-only manual-total defect was fixed at `3269a27f6a5ff41be9085abc36cee39a2258eb32`, but controller review found a quantity `4 -> 5 -> 4` final-state edge case. The active remediation is `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`; owner acceptance remains paused pending its replacement candidate. M09 is not Passed, and merge still requires separate explicit project-owner approval. |
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

Authorization is not merge approval. The active execution state is now:

- Issue #4 was observed **OPEN** solely for `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`;
- branch `codex/m09-hiboutik-paste-fallback` and PR #17 are the durable M09 mailbox;
- WP1 through WP5 and WP6 preparation were accepted; checklist A’s reset defect was remediated and A–E passed on candidate `a18cca2ad51d3676bc9fc2a99d416ad82a1f1a7a`, but that candidate is now paused at checklist F after the owner-observed payment-only manual-total defect;
- the prior payment-only remediation completed at `3269a27f6a5ff41be9085abc36cee39a2258eb32`, and the active remediation is limited to final-state quantity-revert detection, focused regression evidence, and a replacement exact self-contained `win-x64` owner candidate;
- owner Windows/WPF manual acceptance is **PAUSED / NOT PASSED** at checklist F and no owner checklist box is checked by Codex;
- M09 remains `Partial`, not `Passed`; M10+ remain unauthorized.

## 6. M09 execution topology

Authorized topology:

- branch: `codex/m09-hiboutik-paste-fallback`;
- PR: `M09: Hiboutik paste-order fallback` (#17);
- PR top-level Conversation comments: durable mailbox;
- Codex processes only authorized M09 work packages serially;
- the completed WP6 handoff is `M09-WP6-EVIDENCE-MANUAL-ACCEPTANCE-BUILD-11`; the completed reset remediation is `M09-MANUAL-A-HIBOUTIK-RESET-ENABLEMENT-FIX-12`; the completed payment-only remediation is `M09-MANUAL-F-MANUAL-TOTAL-PAYMENT-PRESERVATION-FIX-13`; the active remediation handoff is `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`;
- the current owner candidate is paused at checklist F by the quantity-revert final-state defect; the active remediation replacement candidate must be built and hashed from its final exact head;
- completion of a work package never authorizes the next milestone or merge;
- merge requires separate explicit owner approval.

## 7. M09 evidence and acceptance boundary

The durable WP6 traceability matrix is recorded in [`implementation/milestone-09-worklog.md`](implementation/milestone-09-worklog.md). It maps AC-HIB-001 through AC-HIB-009 to accepted WP1–WP5 automated evidence and the remaining owner-manual sections. WP6 preparation was accepted, but the owner found a checklist-A reset-availability defect on the exact candidate `18ec621d077c1da61994e1cb8657ddb67ab752eb`; its replacement remediation candidate is now the only candidate eligible for resumed owner acceptance.

The automated implementation/evidence baseline remains accepted, and owner checklists A–E are recorded in the PR as passed on the current candidate. Checklist F remains paused: the payment-only manual-total defect was remediated, but controller review found a quantity-revert final-state edge case and authorized `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`. M09 has not received a `Passed` disposition, Codex must not check or rewrite owner checklist boxes, the replacement candidate must be used for resumed acceptance, and the M11 `Gestion SUSHI 81` export-exclusion cross-check remains outside M09 scope.

## 8. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and accepted exact-head evidence remain authoritative in place. Current-state cleanup must not rewrite historical failures/remediation as though they never occurred.
