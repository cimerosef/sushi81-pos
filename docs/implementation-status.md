# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-20
**Current state:** M01 through M09 and the independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement are Passed and merged. M10 Catalogue `.xlsx` is on PR #22 / branch `codex/m10-catalogue-xlsx-authorized`; WP1–WP5 are controller-accepted and owner Scenarios A/B passed. Scenario C remains owner-retest required, and G1 failed on the repair-23 candidate because one malformed Product identity caused unrelated valid OptionGroup/Option `unknown-entity-id` diagnostics in production Preview. Repair-24 now preserves the real read-only baseline during blocking workbook diagnostics and requires owner G1 retest on its replacement candidate; D–L remain not run, PR #22 remains unmerged, and M10 is not marked Passed. Live Issue #4 controls later Codex execution. M11–M13 remain unauthorized.

> Historical implementation/evidence through M06 remains preserved byte-for-byte at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). M07–M09 and post-M09 evidence remains authoritative in milestone-specific worklogs/manual-acceptance/PR records. This file is the living current-state summary; historical evidence is not rewritten to match later state.

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
| M07 — Pairing, target-directed handoff and disaster recovery | Passed | Accepted production head `e971580ef43d3b50366d51733ca9431ca0997e8d`; PR #13 merged at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; final multi-device owner acceptance Passed. |
| M08 — Printing and reprinting | Passed | Accepted production candidate `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`; PR #14 merged at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`; physical/manual acceptance Passed. |
| M09 — Hiboutik paste fallback | Passed | Accepted production candidate `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`; PR #17 merged at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`; owner Windows/WPF acceptance Passed. |
| Post-M09 — Hiboutik daily CB/Espèce dashboard | Passed | Automated evidence accepted in PR #19 comment `5705723428`; owner A–F acceptance Passed in comment `5715414940`; final controller closure comment `5715705712`; PR #19 CLOSED / MERGED at `861cfba1dfacbb3289395c0370f6d42765b6c223`. Accepted executable candidate remains from checkout `dbab706a3f535b521a4a0fc67c318bf0c14b60bb`. |
| M10 — Catalogue `.xlsx` | Partial | WP1–WP5 are controller-accepted and owner Scenarios A/B passed. Scenario C remains owner-retest required. G1 failed on repair-23 because blocking malformed identity diagnostics replaced the live baseline and caused unrelated `unknown-entity-id` cascades; repair-24 fixes Preview baseline handling and requires owner G1 retest. D–L remain not run, merge/M11 remain separate gates, and M10 is not Passed. Live Issue #4 controls future Codex execution. |
| M11 — Gestion export | Not started | Unauthorized; pending M10. |
| M12 — Annual archive/historical access | Not started | Unauthorized; pending M11. |
| M13 — Installer and final acceptance | Not started | Unauthorized; pending M12. |

## 3. Current merged baseline

Current authoritative `main` baseline before M10 implementation:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

This is PR #19's merge commit.

Post-M09 closure facts:

- PR #19: CLOSED / MERGED;
- final closure head before merge: `d55e36a244327c55b81f6fb1040c0ef46dbe154a`;
- owner final acceptance: PR #19 comment `5715414940`;
- final controller closure acceptance: PR #19 comment `5715705712`;
- Issue #18: CLOSED / completed.

Any older living wording saying PR #19 is OPEN/unmerged is stale state-accounting text and is superseded by GitHub merge metadata/current `main`. Historical PR/evidence text remains historical and is not rewritten.

## 4. M10 preparation/readiness and implementation state

M10 primary ownership:

- AC-CAT-008;
- AC-CAT-009;
- AC-CAT-010;
- AC-CAT-011;
- Catalogue portion of AC-ARCH-005.

Preparation audit result:

- existing Domain/Application/SQLite/WPF Catalogue seams: PASS;
- ClosedXML architecture selection: already Approved; one pinned/tested dependency may be added within authorized M10 implementation;
- expected SQLite migration: none;
- whole-import transaction boundary: dedicated one-transaction batch commit required;
- missing workbook rows: mechanically no-delete through overlay planning;
- authority/recovery: export/parse/preview are reads; commit must use centralized authority guard and one post-commit durable-change notification;
- historical orders: current Catalogue import must not rewrite snapshots;
- M11/M12/M13 boundaries: preserved.

WP1–WP5 implementation packages are controller-accepted. Owner Scenarios A/B passed. Scenario C's negative existing-Category short-code subcase correctly blocks without mutation but still requires owner retest for the frozen actionable guidance. G1 then failed on repair-23 because `PreviewAsync` substituted an empty baseline for a workbook with blocking identity diagnostics, causing unrelated valid-row `unknown-entity-id` cascades. Repair-24 keeps the real read-only baseline for diagnostic planning while preserving root Errors and the null Plan/Confirm gate; its exact self-contained `win-x64` candidate must be owner-retested with G1 after controller review. D–L remain not run and are not performed by Codex. This does not mark M10 Passed or authorize merge/M11.

## 5. M10 Category `short_code` owner decision

The audit found one material gap because Category `short_code` became V1 business data after the original three-sheet workbook baseline.

The project owner approved the proposed semantics on 2026-09-17.

Controlling records:

- `decisions/m10-category-short-code-workbook-semantics.md`;
- `acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- aligned `catalogue-management.md`.

Frozen behavior:

- visible Product-row Category name + Category short code;
- no operator-facing Categories worksheet;
- no operator-managed `category_id`;
- new Category may be created with one optional consistent short code;
- existing Category short code is preserve/consistency data only;
- blank preserves; same normalized value is valid; different non-blank value blocks;
- existing Category short code cannot be cleared/replaced by workbook import;
- Category manager remains the global edit workflow;
- conflicts are blocking Errors and therefore prevent the entire atomic import.

No material M10 business/spec decision remains open.

## 6. M10 implementation control package

Finalized preparation head approved by owner:

`6fda83115ccde97e8d0538205eff2b769b353f71`

Implementation line:

- branch: `codex/m10-catalogue-xlsx-authorized`;
- PR #22 — `M10: Catalogue .xlsx import/export`;
- owner implementation authorization: explicit `批准 M10 implementation` on 2026-09-17;
- authorization record: `implementation/milestone-10-authorization.md` — **AUTHORIZED**;
- preparation/readiness: `implementation/milestone-10-preparation-readiness.md` — complete;
- implementation contract: `implementation/milestone-10-catalogue-xlsx.md`;
- final manual acceptance checklist: `implementation/milestone-10-final-manual-acceptance.md` — CANDIDATE PREPARED / OWNER NOT YET EXECUTED;
- worklog: `implementation/milestone-10-worklog.md`.

Historical PR #21 is VOID/CLOSED and must not be used as a mailbox.

## 7. Current gate state

Issue #4 remains the sole Codex execution gate.

M10 milestone implementation is authorized, but actual Codex execution is narrower:

- Codex may execute only the exact handoff named by the live Issue #4 body while Issue #4 is OPEN;
- historical handoff IDs and starting heads remain in their original PR/worklog records and are not rewritten by this living summary;
- branch/PR/handoff pointer must match exactly;
- a `CODEX_DONE` never authorizes another work package, merge or M11;
- controller acceptance never auto-opens the next work package;
- merge always requires separate explicit owner approval;
- M11/M12/M13 remain unauthorized.

## 8. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and exact-head evidence remain authoritative in place. Current-state reconciliation changes only living status summaries; it does not rewrite historical failures, remediation or acceptance chronology.
