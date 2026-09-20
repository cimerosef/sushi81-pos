# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-20  
**Current state:** M01 through M10 and the independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement are Passed and merged. Current merged `main` is M10 merge commit `299df8b44a1959497ad46f861e44db32913b4d11`. M11 Gestion intermediate export is owner-AUTHORIZED on `codex/m11-gestion-export-authorized` / PR #24. WP1 and WP2 are controller-accepted; WP3 is the next executable package once its exact handoff is published. M12/M13 remain unauthorized.

> Historical implementation/evidence through M06 remains preserved at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). Later milestone worklogs/manual-acceptance records and PR comments preserve their own history. This file is the living current-state summary and does not rewrite historical failures.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring or complete, but implementation is not authorized and Codex must not execute.
- `Authorized` — project-owner implementation approval is durable, but execution still requires the matching implementation branch/PR/handoff/open gate.
- `In progress` — an authorized executable handoff is active under OPEN Issue #4.
- `Partial` — some evidence exists, but complete acceptance is not yet satisfied.
- `Closure-ready` — owner acceptance is complete; final controller closure and separate explicit merge approval remain.
- `Passed` — required automated/manual evidence is recorded and passes.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — a required technical capability still needs an approved architecture decision/proof.
- `Not applicable — amended` — only when an approved specification amendment explicitly replaces a criterion.

Only `Passed` and properly approved `Not applicable — amended` satisfy final V1 acceptance.

## 2. Milestone status

| Milestone | Status | Current authoritative result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged through PR #1. |
| M02 — Remote handoff feasibility/revalidation | Passed | Approved target-directed GitHub Release Asset transport revalidation Passed. |
| M03 — Catalogue and settings | Passed | Merged through PR #5; final Windows/WPF acceptance Passed. |
| M04 — Order-entry vertical slice | Passed | Merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`. |
| M05 — Lifecycle/payments/search/dashboard | Passed | Merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`. |
| M06 — Local recovery/read-only enforcement | Passed | Merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`. |
| M07 — Pairing, target-directed handoff and disaster recovery | Passed | PR #13 merged at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; final multi-device owner acceptance Passed. |
| M08 — Printing and reprinting | Passed | PR #14 merged at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`; physical/manual acceptance Passed. |
| M09 — Hiboutik paste fallback | Passed | PR #17 merged at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`; owner Windows/WPF acceptance Passed. |
| Post-M09 — Hiboutik daily CB/Espèce dashboard | Passed | PR #19 merged at `861cfba1dfacbb3289395c0370f6d42765b6c223`; controller/owner acceptance complete. |
| M10 — Catalogue `.xlsx` | Passed | Controller final closure PR #22 comment `5750090951`; PR #22 CLOSED/MERGED at `299df8b44a1959497ad46f861e44db32913b4d11`. Accepted runtime candidate remains `34e61c67785aa6c8c0ca84a545e31de30b17ac39`. |
| M11 — Gestion intermediate export | In progress | WP1 and WP2 controller-accepted. WP3 Desktop export workflow is next under an exact PR #24 handoff; WP4 follows later. |
| M12 — Annual archive/historical access | Not started | Unauthorized; pending M11. |
| M13 — Installer and final acceptance | Not started | Unauthorized; pending M12. |

## 3. Current merged baseline

Current authoritative `main`:

`299df8b44a1959497ad46f861e44db32913b4d11`

This is PR #22's M10 merge commit.

M10 closure facts:

- PR #22: CLOSED / MERGED;
- final documentation head before merge: `e4df426ac58243fbc29b2eb64b2d9adf7b7b9620`;
- accepted runtime candidate: `34e61c67785aa6c8c0ca84a545e31de30b17ac39`;
- owner A–L + final usability acceptance: PASS;
- controller final closure: comment `5750090951`;
- runtime exact-head CI run `35512075546`: success, 787/787, 0 warnings/errors;
- documentation closure CI run `35513279968`: success, 787/787, 0 warnings/errors.

Historical M10 failures/repairs remain in the M10 worklog and PR #22 and are not rewritten here.

## 4. M11 preparation/readiness

Preparation line:

- branch: `prep/m11-gestion-export`;
- draft PR #23 — documentation/readiness history, superseded for execution by PR #24;
- finalized preparation head: `a8df4a6c671de7e0050a539e156e15caa9c791f8`.

Implementation line:

- branch: `codex/m11-gestion-export-authorized`;
- PR #24 — `M11: Gestion intermediate export`;
- owner implementation authorization: granted 2026-09-20;
- authorization record: `implementation/milestone-11-authorization.md` — AUTHORIZED;
- WP1: ACCEPTED at `678bf476c259f628582c7ae5e66c7ed641f32952`;
- WP2: ACCEPTED at `a9774e2a6795adafee75b18f061289e47811caf5`, controller comment `5752144083`;
- WP3: next package, executable only under its own exact PR #24 handoff.

Primary acceptance ownership:

- AC-EXP-001 through AC-EXP-011;
- AC-HIB-008 final Gestion-export exclusion cross-check;
- export portion of AC-ARCH-005.

Readiness result:

- existing OrderSnapshot/lifecycle/payment/snapshot seams: reusable;
- existing centralized authority/recovery seams: reusable;
- M10 ClosedXML boundary: reusable as architecture/dependency precedent;
- M11 requires a new versioned SQLite migration because no export batch/ledger/immutable payload history exists;
- workbook contract remains fixed four-sheet schema version 1.0;
- exact regeneration requires immutable emitted payload retained in SQLite;
- M09 Hiboutik orders are explicitly excluded from every Gestion export action;
- M12 archive and M13 packaging remain out of scope.

Owner-approved 2026-09-20 clarification:

- Closed is the positive-sale lifecycle export gate; Open never emits CREATE/UPDATE;
- existing exact-payment Close invariant remains unchanged;
- SettlementDate uses effective payment business date, not merely Close/recording date;
- UPDATE waits until current committed state is Closed;
- CANCEL supersedes an un-emitted pending UPDATE;
- CANCEL does not require current Closed/settled state;
- workbook fields are fixed contract fields; operator selects order scope, not columns.

Controlling records:

- `decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `acceptance-criteria-amendment-m11-gestion-export.md`;
- amended `export.md`;
- `implementation/milestone-11-preparation-readiness.md`;
- `implementation/milestone-11-gestion-export.md`;
- `implementation/milestone-11-final-manual-acceptance.md`;
- `implementation/milestone-11-worklog.md`;
- `implementation/milestone-11-authorization.md` — **AUTHORIZED**.

Work-package plan:

1. WP1 — export state model, migration, selection and Application contracts;
2. WP2 — ClosedXML workbook, validation, safe finalization and exact regeneration;
3. WP3 — Desktop export workflow;
4. WP4 — integration hardening and owner candidate.

No material M11 business/spec decision remains open.

## 5. Current execution gate

Issue #4 remains the sole Codex execution gate.

Current authorized control line:

- dedicated implementation branch: `codex/m11-gestion-export-authorized`;
- dedicated implementation PR/mailbox: #24;
- M11 implementation authorization: GRANTED;
- WP1: ACCEPTED;
- WP2: ACCEPTED;
- WP3: next executable package under its own unique PR #24 `CODEX_HANDOFF_READY`;
- WP4: not yet executable;
- Issue #4 must be OPEN before Codex executes;
- M12/M13 remain unauthorized.

The live Issue #4 body is the sole execution switch. Closing Issue #4 immediately revokes execution permission without changing the durable owner authorization.

## 6. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and exact-head evidence remain authoritative in place. Current-state reconciliation changes only this living summary.
