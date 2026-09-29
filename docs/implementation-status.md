# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-29
**Current state:** M01 through M13 and the independent post-M09 dashboard enhancement are Passed/merged. M14 — PreProd Foundation and immutable release promotion — is **Accepted under waiver / merged** through PR #30 at `71024888a9cfd08109496ad076002be96e708c1a`. The real two-PC PREPROD pairing/handoff/acquisition remains DEFERRED UNDER OWNER WAIVER — NOT PASSED; PREPROD remains computer-A-only and computer B remains Production-only. M12 real populated-archive verification remains separately deferred. Post-M14 Production Maintenance Batch 01 is OWNER-AUTHORIZED on Draft PR #31; WP1 duplicate-add repair is the first executable package and no Production deployment or merge is authorized.

> Historical implementation/evidence through M06 remains preserved at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). Later milestone worklogs/manual-acceptance records and PR comments preserve their own history. This file is the living current-state summary and does not rewrite historical failures.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring or complete, but implementation is not authorized and Codex must not execute.
- `Authorized` — project-owner implementation approval is durable, but execution still requires the matching implementation branch/PR/handoff/open gate.
- `In progress` — an authorized executable handoff is active under OPEN Issue #4.
- `Partial` — some evidence exists, but complete acceptance is not yet satisfied.
- `Closure-ready` — owner acceptance (including any explicit documented waiver) is complete; final controller closure remains. Any merge authorization or prerequisite is governed by its durable owner record.
- `Accepted under waiver` — owner acceptance proceeds under an explicit Approved waiver while the waived/deferred verification remains named and must not be represented as Passed. Controller closure and merge state are recorded separately; `Accepted under waiver / closure-ready` means the owner evidence is complete and final controller closure remains pending.
- `Passed` — required automated/manual evidence is recorded and passes.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — a required technical capability still needs an approved architecture decision/proof.
- `Not applicable — amended` — only when an approved specification amendment explicitly replaces a criterion.

`Accepted under waiver` may satisfy milestone closure only to the exact extent explicitly approved by the owner; the deferred item remains an open operational verification obligation and must stay visible in final V1 records.

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
| M12 — Annual archive/historical access | Accepted under waiver / merged | PR #25 CLOSED/MERGED at `f59663c6b47ab21114c24360544e4e25094f4722`. Final pre-merge head `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`; exact-head CI #862 / run `36021889765` succeeded with 881/881, 0 failed/skipped and Release build 0 warnings/errors. Post-merge CI #863 / run `36023054757` build-and-test succeeded. Real populated-archive operational verification remains explicitly deferred under the owner waiver and is not claimed Passed. |
| M13 — Installer and final acceptance | Passed / merged | PR #26 CLOSED/MERGED at `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`. Accepted production application remains source `469c8761061b0ecf488e06386a97b9e10652a916`; CI #912 / `36253271385`, 932/932 tests; installer artifact `10909188508`. |
| M14 — PreProd Foundation and immutable release promotion | Accepted under waiver / merged | PR #30 CLOSED/MERGED at `71024888a9cfd08109496ad076002be96e708c1a`. Immutable C01 Release `398945249` and WP6 exact-head `1c73999c323dba593e3279f47c54a6174638b23d`, CI #963 / run `36546336521`, promotion artifact `11022832475` remain accepted evidence. Two-PC PREPROD pairing/handoff/acquisition remains deferred under owner waiver, not Passed. |
| Post-M14 Production Maintenance Batch 01 | Authorized / PR open | Draft PR #31 on `codex/post-m14-production-maintenance-batch-01`; owner-approved scope is duplicate-add repair, cart auto-reveal, natural code order, customer-ticket Retrait discount line, then integrated PREPROD candidate. WP1 first; Issue #4 controls execution. |

## 3. Current merged baseline

Current authoritative `main`:

`71024888a9cfd08109496ad076002be96e708c1a`

This is PR #30's M14 merge commit.

M12 closure facts:

- PR #25: CLOSED / MERGED;
- accepted runtime source candidate before final documentation: `e62db0003f837297b848448a28a94e30c5db64a4`;
- final documentation head before merge: `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`;
- exact-head pre-merge CI #862 / run `36021889765`: SUCCESS; 881 passed, 0 failed, 0 skipped; Release build 0 warnings / 0 errors;
- post-merge CI #863 / run `36023054757`: build-and-test SUCCESS;
- owner waiver/deferred real populated-archive verification remains in force and is not rewritten as Passed;
- first safe real verification point remains an authoritative startup on or after 2027-02-01 with real 2026 rows;
- archive failure must continue to fail safe without silent eligible-live-row deletion.

## 3.1 M14 final merged state

- owner approval/freeze: 2026-09-27;
- start baseline: `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`;
- implementation branch: `codex/m14-preprod-foundation-authorized`;
- scope: permanent Prod/PreProd isolation, safe initial seed, independent runtime transport/DR configuration, dual installers, immutable GitHub candidate payload and no-rebuild production promotion;
- explicitly excluded: post-launch business bug fixes, repeated automatic Prod→PreProd refresh, automatic production deployment, M12 deferred populated-archive verification;
- PR #30 is CLOSED/MERGED at `71024888a9cfd08109496ad076002be96e708c1a`; Issue #4 is no longer an M14 execution pointer.

Current M14 closure line: M14 is **Accepted under waiver / merged**. WP7 owner checklist A, B, C, D and F passed on PREPROD computer A; G was controller-accepted with WP6 evidence. Checklist E, the real second-PREPROD-device pairing and A→B target-directed handoff/acquisition, is **DEFERRED UNDER OWNER WAIVER — NOT PASSED**. PREPROD currently exists only on A; B remains Production-only. The deferred verification is mandatory before a second PREPROD device is introduced or before accepting material multi-device pairing/authority-handoff/target-acquisition/DR/OneDrive/GitHub coordination changes. The waiver changes acceptance practice, not M07 runtime semantics. M12 populated real annual-archive verification remains a separate deferred obligation.

Controlling records:

- `decisions/m14-preprod-environment-isolation-and-promotion.md`;
- `acceptance-criteria-amendment-m14-preprod-foundation.md`;
- `implementation/milestone-14-preparation-readiness.md`;
- `implementation/milestone-14-preprod-foundation.md`;
- `implementation/milestone-14-final-manual-acceptance.md`;
- `implementation/milestone-14-worklog.md`;
- `implementation/milestone-14-authorization.md`.

## 3.2 Post-M14 Production Maintenance Batch 01

- owner authorization: granted 2026-09-29;
- baseline: `71024888a9cfd08109496ad076002be96e708c1a`;
- branch: `codex/post-m14-production-maintenance-batch-01`;
- Draft PR/mailbox: #31;
- WP order: duplicate-add repair -> cart auto-reveal -> natural product-code ordering -> customer-ticket Retrait discount -> integration/candidate;
- owner acceptance target: computer A PREPROD only;
- computer B remains Production-only;
- M14 deferred two-PC PREPROD verification and M12 populated real archive verification remain unchanged;
- no Production deployment or PR merge is authorized.

Controlling records:

- `decisions/post-m14-production-maintenance-batch-01.md`;
- `acceptance-criteria-amendment-post-m14-production-maintenance-batch-01.md`;
- `implementation/post-m14-production-maintenance-batch-01.md`;
- `implementation/post-m14-production-maintenance-batch-01-worklog.md`;
- `implementation/post-m14-production-maintenance-batch-01-final-manual-acceptance.md`;
- `implementation/post-m14-production-maintenance-batch-01-authorization.md`.

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

## 5. M12 closure and current M13 control state

M12 is controller-accepted and merged under the explicit owner waiver. The historical M12 control package and PR #25 remain the authoritative evidence for what passed, what was owner-observed, and what is still deferred. Do not restart synthetic archive forensics in M13 unless a new installer/final-regression fact directly blocks release.

M13 current control line:

- owner authorization: GRANTED under the implementation-plan preauthorization that became effective after M12 merge;
- dedicated branch: `codex/m13-installer-final-acceptance-authorized`;
- Draft implementation PR/mailbox: #26 — `M13: Installer, localization completion and final V1 acceptance`;
- M13 preparation/readiness: `implementation/milestone-13-preparation-readiness.md`;
- implementation contract: `implementation/milestone-13-installer-localization-final-acceptance.md`;
- authorization record: `implementation/milestone-13-authorization.md`;
- final owner checklist: `implementation/milestone-13-final-manual-acceptance.md`;
- worklog: `implementation/milestone-13-worklog.md`;
- Approved retention decision: `decisions/m13-gestion-export-ledger-retention-compaction.md`;
- matching acceptance amendment: `acceptance-criteria-amendment-m13-gestion-export-retention.md`;
- Issue #4 was OPEN for the final documentation reconciliation handoff `M13-FINAL-STATUS-RECONCILIATION-20`; normal post-package expectation is no active implementation gate until a new authorized task;
- accepted production application head `469c8761061b0ecf488e06386a97b9e10652a916`, CI #912 / run `36253271385`, artifact ID `10909188508`;
- PR #26 remains unmerged.

### M13 work-package sequence

1. WP1 — Gestion export retention/compaction core;
2. WP2 — retention runtime integration and successful-history behavior;
3. WP3 — full FR/zh-CN localization completion;
4. WP4 — self-contained win-x64 publish + per-user Inno Setup installer + preservation evidence;
5. WP5 — diagnostics/performance/repository-security/production hardening;
6. WP6 — final exact-head production candidate, operating guide and owner V1 acceptance.

### Current execution state

The owner explicitly confirmed on 2026-09-24 that the source repository's public visibility is intentional and does not block M13. Repository visibility therefore requires no M13 action. The M13 export retention/compaction behavior is formalized as Approved criteria AC-EXP-012 through AC-EXP-016.

### M13 accepted work-package evidence

| Package | Controller-accepted head | Exact-head CI |
|---|---|---|
| WP1 — Gestion export retention/compaction | `a3d3110b59f436bc0a9107777ea97f73df85119d` | #872 / `36057354475` — success |
| WP2 — retention runtime/history | `97d0a6e048aedca66b1f93e9768285f49f04ac63` | #873 / `36063106606` — success |
| WP3 — localization completion | `e40a1f882d4c0557fc0fe4e30cb88395be290c89` | #874 / `36106655435` — success |
| WP4 — production publish/installer | `0f73cee0311a1c76e8b4cafd7413720c252be27c` | #880 / `36114601424` — success; artifact `10854348679` |
| WP5 — diagnostics/performance/security hardening | `aa734cc94bb6500be42cbaa00407e8fc8ec7a1fd` | #882 / `36124050769` — success; artifact `10859146322` |

### M13 owner acceptance and remaining control

- A/B installer identity, launch and profile preservation: PASS on owner evidence and controlled B installation;
- C authoritative business smoke: PASS;
- D FR/zh-CN runtime switching and final navigation/layout: PASS after the earlier candidate failure was repaired;
- E retained Gestion export behavior: PASS for available real-profile evidence; PREPARED/30-day edges remain deterministic automated evidence, not invented owner observations;
- F operating guide: PASS after the owner followed the revised Chinese guide through B installation/configuration, Credential Manager GitHub test, OneDrive local availability, printers and normal exact-target A→B handoff; the earlier guide failure remains historical;
- G protected in-app full business-data reset: PASS. Owner evidence covers preview, cancel/no mutation, typed `RESET` and final confirmation, private `MaintenanceBackups`, cleared business views and preserved settings, authority and technical configuration. The subsequent A→B transfer proved pairing/target reuse. Do not repeat the reset;
- A→B production cutover succeeded: B is authoritative/writable, A read-only; real production catalogue transferred to B;
- catalogue VAT compatibility: genuine Excel-percent formats normalize, and only plain numbers `0.055→5.5`, `0.1→10`, `0.2→20` receive legacy normalization. Other plain fractions remain literal. B bound-record export/reimport committed canonical 5.5/10/20, owner visually verified representative rows, and a second preview showed zero changes. Handoff 19 owner acceptance: PASS.

The first safe M12 real populated-archive verification remains an authoritative startup on or after 2027-02-01 with real 2026 rows: **DEFERRED-NOT-M13**. PR #26 remains OPEN/Draft/unmerged pending final controller review and separate owner merge approval. This docs-only reconciliation does not change the accepted production application head; a generated installer from its CI run is evidence-only.

## 6. Evidence preservation

Historical milestone worklogs, acceptance records, decision records, PR discussions and exact-head evidence remain authoritative in place. Current-state reconciliation changes only this living summary.
