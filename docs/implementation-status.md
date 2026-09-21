# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-21
**Current state:** M01 through M11 and the independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement are Passed and merged. Current authoritative `main` is M11 merge commit `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`. M12 Annual archive and historical access is owner-AUTHORIZED on `codex/m12-annual-archive-authorized` / Draft PR #25. The owner-approved 2026-09-21 amendment makes canonical annual archives permanent application-managed local databases and makes archive export an explicit operator-selected copy action. WP1 remains the first package. M13 remains unauthorized.

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
| M11 — Gestion intermediate export | Passed | PR #24 CLOSED/MERGED at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`. Accepted runtime candidate `77ccecf9d947462e96e74b8aa1d99ced30e3788e`; controller closure comment `5762784303`; post-merge CI #821 / run `35617254203` succeeded with 831/831, 0 failed, 0 skipped and Release build 0 warnings/errors. |
| M12 — Annual archive/historical access | Authorized | Owner-authorized on `codex/m12-annual-archive-authorized` / Draft PR #25. Local-archive/user-selected-export amendment Approved 2026-09-21; WP1 is the first package. |
| M13 — Installer and final acceptance | Not started | Unauthorized; pending M12. |

## 3. Current merged baseline

Current authoritative `main`:

`1a94f3400e0aa9fe9f878bbe98a8285112206ba9`

This is PR #24's M11 merge commit.

M11 closure facts:

- PR #24: CLOSED / MERGED;
- accepted runtime candidate: `77ccecf9d947462e96e74b8aa1d99ced30e3788e`;
- final documentation head before merge: `4eddf0a93c5a141326d76c6e67a9a9c5840e5068`;
- controller final closure: PR #24 comment `5762784303`;
- merge completion: PR #24 comment `5762846107`;
- post-merge CI #821 / run `35617254203`: SUCCESS;
- total tests: 831/831 passed, 0 failed, 0 skipped;
- Release build: 0 warnings / 0 errors.

Historical M11 failures/repairs, owner A–E evidence and pre-merge closure records remain in the M11 worklog/manual-acceptance record and PR #24 and are not rewritten here.

## 4. M11 historical implementation/acceptance record

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
- WP3: ACCEPTED at `3c9e110f7d6c5a93c773b2289f5b43f5c59fb14f`; controller acceptance is recorded in PR #24.
- WP4: the prior controller-accepted owner candidate was delivered through GitHub Actions run `35584565162`, artifact `M11-WP4-owner-candidate-win-x64-dc8091f` / ID `10631573948`, built from source head `dc8091fccb31923bacc16cbff7b49ada771f55fb`; owner scenario A failed and B–E were not continued. That failure remains historical evidence. Repairs 09–11 were subsequently accepted, and owner A–E are now PASS on the accepted candidate at `77ccecf9d947462e96e74b8aa1d99ced30e3788e`.
- Repair 09 controller finding: export finalization, PREPARED retry visibility and visible `销售数据导出` naming were accepted, but the DatePicker watermark remained insufficiently proven because only `DatePicker.Language` was asserted; the old candidate is not approved for retest.
- The accepted repair chain preserves WPF `CurrentUICulture`/`DatePicker` behavior, normalizes native Excel/OLE DateTime validation through its exact millisecond-representable round-trip, and leaves export/business semantics unchanged. The accepted runtime candidate is `77ccecf9d947462e96e74b8aa1d99ced30e3788e`; owner A–E PASS evidence is recorded in PR #24 comments `5762194270`, `5762229722`, `5762338169`, `5762376235` and `5762475899`.

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

## 5. Current M12 readiness and execution gate

M12 control line:

- owner authorization: GRANTED;
- dedicated implementation branch: `codex/m12-annual-archive-authorized`;
- Draft implementation PR/mailbox: #25 — `M12: Annual archive and historical access`;
- preparation/readiness: `implementation/milestone-12-preparation-readiness.md`;
- implementation contract: `implementation/milestone-12-annual-archive-historical-access.md`;
- authorization record: `implementation/milestone-12-authorization.md`;
- M13: unauthorized.

Implementation-readiness result:

- current SQLite order aggregate and historical snapshot tables are reusable;
- `IWriteAuthorityGuard`, application Temp path, read-only SQLite connection factory and M08 snapshot-based print model are reusable seams; M12 adds a permanent local Archive application-data path;
- normal `SearchLiveAsync` remains live-only and must not be widened implicitly;
- M11 immutable successful export emissions survive order-row deletion physically, but pending/un-emitted corrections are derived from the live order and must be durably preserved before removal as required by the implementation plan;
- no production archive implementation currently exists;
- owner-approved decision `decisions/m12-local-archive-and-user-selected-export.md` removes OneDrive from annual archive publication/access;
- WP1 can be implemented without final local canonical promotion or live deletion and is READY;
- WP2 is no longer blocked by OneDrive acknowledgement: it must use staged validation -> durable local canonical promotion -> reopen-validation -> exact live removal, while preserving pending export payloads first.

Issue #4 remains the sole Codex execution switch/status pointer. The previous WP1 READY was revoked while this Approved amendment was aligned. A new unique exact-head WP1 READY must be published before Codex resumes. One WP1 DONE does not authorize WP2.

## 6. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and exact-head evidence remain authoritative in place. Current-state reconciliation changes only this living summary.
