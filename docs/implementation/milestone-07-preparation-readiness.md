# M07 implementation-preparation readiness

**Status:** Preparation complete pending explicit project-owner implementation authorization  
**Prepared:** 2026-09-07  
**Execution gate:** CLOSED  
**Implementation started:** No

## 1. Material decision closure

The three material M07 product/safety decisions are resolved by project-owner approval and durably recorded in:

- `../decisions/m07-self-join-disaster-recovery.md`;
- `../acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`.

Resolved semantics:

1. fresh devices may self-join without approval from the old/current authoritative device; membership never grants authority;
2. genuine DR requires explicit old-device quarantine and online single-winner next-generation activation while ordinary authoritative offline operation remains supported;
3. DR uses the freshest validated safe candidate from completed GitHub handoff+grant or OneDrive recovery checkpoint; snapshot without grant is ineligible.

No material product/business/authority/recovery/data-loss decision remains open at preparation closure.

## 2. M06 → M07 governance cleanup

Completed:

- M06 is recorded Passed/merged in the living current-status surfaces;
- `docs/implementation-status.md` is now a current control page rather than a stale mixed current/history page;
- its exact former blob through the M06 transition is preserved byte-for-byte at `archive/implementation-status-through-m06-2026-09-07.md`;
- AC-STO-006 is recorded Passed under M06;
- AC-STO-010 M06 centralized/fail-closed/read-only foundation is recorded Passed with M07 regression ownership;
- M07 is recorded Preparation / not implementation-authorized.

Historical M02/M06 evidence remains unchanged in its historical records.

## 3. Specification consistency closure for M07

The Approved M07 decision explicitly controls stale earlier wording:

- normal authority handoff uses GitHub Release Assets in the dedicated private handoff repository;
- OneDrive `Handoff` wording in the old storage baseline has no normal-authority semantics and must not guide implementation;
- OneDrive active M07 areas are `System` for non-authority lineage/device metadata and `DisasterRecovery` for recovery-only checkpoints; `Archive` remains later archive storage;
- product NFR-004 is interpreted as local recovery + GitHub normal handoff + OneDrive disaster recovery + annual archive;
- AC-STO-009 is amended to safe-candidate recovery + quarantine + online single-winner next-generation activation.

These are Approved amendments, not Codex implementation choices.

## 4. Prepared implementation artifacts

- `milestone-07-pairing-handoff-disaster-recovery.md` — detailed mechanical implementation contract; NOT AUTHORIZED.
- `milestone-07-parallel-execution-plan.md` — required execution topology/dependency companion; NOT AUTHORIZED.
- `milestone-07-worklog.md` — prepared evidence log; no code work recorded.
- `milestone-07-final-manual-acceptance.md` — prepared owner Windows/WPF multi-device checklist; NOT EXECUTED.
- `milestone-07-preauthorization-design-review.md` — historical analysis only; its old authority-approved-pairing proposal is superseded.

A separate `milestone-07-authorization.md` intentionally does **not** exist yet because implementation has not been explicitly authorized.

## 5. Technical proof selected for DR activation

The prepared contract uses a deterministic lineage+next-generation GitHub Release Asset filename as the candidate create-once activation primitive.

GitHub's official current Release Assets API contract documents:

- successful upload returns HTTP 201;
- uploading an asset with the same filename as an existing uploaded asset returns HTTP 422;
- a 502 may leave a `starter` asset which may be safely deleted;
- successful asset responses expose uploaded state, exact name, size, ID and SHA-256 digest.

M07 does not assume this is sufficient merely from documentation. WP7 requires deterministic concurrent-contender tests plus a disposable real private-repository race/retry drill before broad production DR implementation. Failure to prove at-most-one valid activation blocks M07 for architecture amendment.

## 6. Preparation-quality checks

Before requesting implementation authorization, preparation requires all of the following to be true:

- [x] M06 Passed/merged baseline recorded.
- [x] M06 historical status evidence archived before living-status rewrite.
- [x] Three material M07 decisions owner-approved and recorded.
- [x] Acceptance amendment recorded.
- [x] Self-join replacement-PC dead-source scenario explicitly covered.
- [x] Normal handoff irreversible point/order explicitly specified.
- [x] Target data-first/authority-last acquisition explicitly specified.
- [x] OneDrive changed-only/15-minute/newest-five checkpoint contract specified.
- [x] DR safe-candidate validation specified.
- [x] DR old-device quarantine limitation stated truthfully.
- [x] Online single-winner activation proof gate specified.
- [x] Crash/retry/idempotency matrices required.
- [x] Protected GitHub credential/redaction boundary specified.
- [x] FR/zh-CN WPF/STA quality requirements specified.
- [x] Parallel execution topology/dependency seams prepared.
- [x] Final real Windows multi-device owner checklist prepared.
- [x] M08+ boundaries explicitly preserved.

## 7. Governance state required at preparation closure

Preparation is valid only while these remain true:

- GitHub issue #4 execution gate is CLOSED;
- no active M07 implementation branch;
- no active M07 implementation PR;
- no durable M07 implementation authorization;
- no executable `CODEX_HANDOFF_READY` mailbox pointer;
- Codex has not started M07 production work;
- M08 has not started.

The governance controller must re-verify these facts immediately before asking the owner for implementation authorization and again before later opening issue #4.

## 8. Next action after owner authorization

Only after a new explicit project-owner statement authorizing M07 implementation:

1. create `milestone-07-authorization.md` tied to the then-current approved contract/preparation head;
2. update living status/worklog to Authorized/In progress without altering prior historical evidence;
3. create the dedicated M07 implementation branch and PR;
4. create the single active mailbox pointer and one complete executable Codex handoff;
5. verify branch/PR/base/head/handoff/authorization consistency;
6. open issue #4 execution gate;
7. give Codex the complete handoff;
8. keep M08 closed/not started.

Authorization does not pre-authorize merge. Final M07 merge still requires exact-head review, project-owner Windows/WPF acceptance, green CI and separate explicit merge approval.
