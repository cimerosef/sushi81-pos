# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-17
**Current state:** M01 through M09 are Passed and merged. The independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement is **In progress** on PR #19 under the OPEN Issue #4 gate. M10 and later milestones remain unauthorized.

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
| M09 — Hiboutik paste fallback | Passed | Accepted production candidate `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d` (FIX-14); exact-head CI #712 / `35019616149` succeeded with 651/651 Release tests passed and a 0-warning / 0-error Release build. Owner Windows/WPF manual acceptance is PASSED with A–N evidence reconciled. PR #17 merged to `main` at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`; the later docs-only closure head is historical closure evidence, not a replacement executable. |
| Post-M09 — Hiboutik daily CB/Espèce dashboard | In progress | Explicitly authorized independent enhancement on branch `codex/post-m09-hiboutik-daily-payment-dashboard` / PR #19. Current handoff: `POST-M09-HIBOUTIK-DAILY-PAYMENT-DASHBOARD-01`; docs-first reconciliation is required before production edits. |
| M10 — Catalogue `.xlsx` | Not started | Unauthorized; no M10 handoff exists. |
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

Authorization is not merge approval. The final M09 execution state is now:

- PR #17 is **CLOSED / MERGED** at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`;
- branch `codex/m09-hiboutik-paste-fallback` remains the historical M09 implementation branch;
- WP1 through WP5, WP6 preparation and the narrow FIX-12/FIX-13/FIX-14 remediations are preserved as historical implementation evidence;
- owner Windows/WPF manual acceptance is **PASSED** on accepted candidate `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`, with A–N evidence reconciled in `implementation/milestone-09-final-manual-acceptance.md`;
- the accepted production candidate remains `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`; the later closure head is historical documentation evidence and was not a replacement executable;
- M09 is `Passed` and merged; M10+ remain unauthorized.

## 6. M09 execution topology

Authorized topology:

- branch: `codex/m09-hiboutik-paste-fallback`;
- PR: `M09: Hiboutik paste-order fallback` (#17);
- PR top-level Conversation comments: durable mailbox;
- Codex processes only authorized M09 work packages serially;
- the completed WP6 handoff is `M09-WP6-EVIDENCE-MANUAL-ACCEPTANCE-BUILD-11`; the completed reset remediation is `M09-MANUAL-A-HIBOUTIK-RESET-ENABLEMENT-FIX-12`; the completed payment-only remediation is `M09-MANUAL-F-MANUAL-TOTAL-PAYMENT-PRESERVATION-FIX-13`; the completed quantity-revert remediation is `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14`; the current handoff is `M09-FINAL-CLOSURE-PASS-READY-FOR-MERGE-15`;
- the owner-tested candidate is `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`; the final documentation-only closure head is intentionally later and must not be treated as a replacement owner-tested executable;
- completion of a work package never authorizes the next milestone or merge;
- merge requires separate explicit owner approval.

## 7. M09 evidence and acceptance boundary

The durable WP6 traceability matrix and final A–N reconciliation are recorded in [`implementation/milestone-09-worklog.md`](implementation/milestone-09-worklog.md) and [`implementation/milestone-09-final-manual-acceptance.md`](implementation/milestone-09-final-manual-acceptance.md). The historical remediation chain is preserved, including the earlier candidate evidence and the accepted FIX-14 correction. The owner final disposition is durable in PR #17 comment `5704531105`; no new physical observation is created by the closure docs. The M11 `Gestion SUSHI 81` export-exclusion cross-check remains outside M09 scope.

## 8. Post-M09 Hiboutik daily payment dashboard

The independent post-M09 enhancement is authorized by the project owner and controlled by the following committed records:

- `decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-authorization.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-manual-acceptance.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-worklog.md`.

Current execution state:

- branch: `codex/post-m09-hiboutik-daily-payment-dashboard`;
- PR/mailbox: `Post-M09: Hiboutik daily CB/Espèce dashboard` (#19), OPEN / unmerged;
- active handoff: `POST-M09-HIBOUTIK-DAILY-PAYMENT-DASHBOARD-01`;
- Issue #4: OPEN and points only to that handoff;
- required scope: exactly two passive read-only values derived from non-Cancelled `HIBOUTIK_PASTE` payment adjustments by effective business date;
- no schema/migration, new dependency, new write path, M10/M11 work or additional Hiboutik metric/workflow is authorized;
- the owner manual checklist remains pending and Codex must not declare it Passed.

## 9. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and accepted exact-head evidence remain authoritative in place. Current-state cleanup must not rewrite historical failures/remediation as though they never occurred.
