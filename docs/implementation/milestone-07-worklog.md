# M07 worklog — pairing, target-directed handoff and disaster recovery

**Status:** Authorized / In progress — execution setup underway  
**Prepared:** 2026-09-07  
**Authorized:** 2026-09-07  
**Execution gate:** CLOSED until PR/mailbox/handoff verification completes  
**Contract:** `docs/implementation/milestone-07-pairing-handoff-disaster-recovery.md`  
**Authorization:** `docs/implementation/milestone-07-authorization.md`

This file is the durable execution/evidence log for M07. Project-owner implementation authorization now exists, but Codex may execute only after the active PR/mailbox pointer and one complete handoff are created and Issue #4 is reopened.

## 1. Entry baseline

- M06: Passed / merged.
- M06 merge commit: `2c5eb52740d0c12e3e837579ecceac6d0600b59e`.
- M06 accepted production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`.
- M06 final docs/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`.
- M06 final Release tests: 364/364 Passed.
- M06 exact-head CI: `34091370109` Passed.
- M06 project-owner Windows/WPF acceptance: Passed.
- M07 material owner decision: `docs/decisions/m07-self-join-disaster-recovery.md`.
- M07 acceptance amendment: `docs/acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`.
- M07 implementation authorization: `docs/implementation/milestone-07-authorization.md` — **AUTHORIZED 2026-09-07**.
- Authorized preparation head: `e8a9992ba04a21ac4854492bd3bcb7a8ce6e4b96`.
- Active M07 branch: `codex/m07-pairing-handoff-disaster-recovery`.
- Active M07 PR: pending creation during execution setup.
- `CODEX_HANDOFF_READY`: pending creation during execution setup.
- Issue #4 execution gate: CLOSED until setup verification completes.

### Authorization event

Project owner explicitly stated on 2026-09-07:

> 批准 M07 正式实施。按照当前已批准的规格和实施合同，开始 M07 implementation。

This authorizes implementation only. It does not authorize merge or M08.

## 2. Owner-approved material semantics

Do not reopen these as implementation questions unless code/reality proves the approved model impossible:

- new computers may self-join an existing lineage without old/current authority approval;
- self-join/membership never grants authority;
- healthy-system authority moves normally only through exact source-directed target handoff;
- dead/unrecoverable old authority may be replaced by a self-joined fresh computer using explicit DR;
- DR requires explicit old-device stop/quarantine confirmation and online single-winner next-generation activation;
- ordinary valid authoritative operation remains local-first/offline-capable;
- DR chooses the freshest validated safe candidate: valid completed GitHub handoff+grant or valid OneDrive DR checkpoint; snapshot without grant is ineligible.

## 3. Execution entries

Codex/governance controller appends entries below. Never rewrite earlier execution evidence to make later state appear historical.

### M07-WP0 — baseline and mutation-guard audit

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Findings:**  
**Next:**

### M07-WP1 — canonical authority state and M06 migration

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Failure-injection evidence:**  
**Next:**

### M07-WP2 — self-join/System metadata/read-only initialization

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**N-device evidence:**  
**Next:**

### M07-WP3 — production GitHub configuration/credential/transport

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Secret/redaction evidence:**  
**Next:**

### M07-WP4 — normal source close/handoff

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Irreversible-point/restart evidence:**  
**Next:**

### M07-WP5 — target acquisition

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Wrong-target/crash/idempotency evidence:**  
**Next:**

### M07-WP6 — OneDrive recovery checkpoints

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**15-minute/newest-five/offline evidence:**  
**Next:**

### M07-WP7 — DR activation single-winner proof

**Status:** Not started  
**Commit(s):**  
**Deterministic concurrency evidence:**  
**Disposable real private GitHub race/retry evidence:**  
**Result:** Must be Passed before broad DR implementation. If not provable, record `Blocked — architecture decision required` and stop.

### M07-WP8 — production Disaster Recovery/stale generation/reinit

**Status:** Not started  
**Commit(s):**  
**Tests/evidence:**  
**Replacement-PC evidence:**  
**Generation/fencing evidence:**  
**Next:**

### M07-WP9 — WPF/localization/observability/integration

**Status:** Not started  
**Commit(s):**  
**STA/WPF evidence:**  
**FR/zh-CN evidence:**  
**Responsiveness/redaction evidence:**  
**Next:**

### M07-WP10 — automated/CI closure before owner acceptance

**Status:** Not started  
**Accepted production-code head candidate:**  
**Release tests:**  
**Release build warnings/errors:**  
**Self-contained win-x64 publish:**  
**Exact-head CI run:**  
**Known carry-over (M08+ only):**

## 4. Review/remediation log

Append each ChatGPT review/remediation cycle with exact head SHA, findings, severity, Codex remediation commit(s), tests and disposition.

### Review cycle 1

**Head reviewed:**  
**Findings:**  
**Disposition:**  
**Remediation head:**  
**CI/tests:**

## 5. Project-owner manual acceptance

**Status:** Not executed  
**Checklist:** `docs/implementation/milestone-07-final-manual-acceptance.md`  
**Exact head tested:**  
**Artifact:**  
**Date:**  
**Result:**  
**Findings/remediation:**

M07 must not be marked Passed from automated evidence alone.

## 6. Final closure

Complete only after owner acceptance and exact-head green CI.

- final accepted production head:
- final docs/evidence head:
- final Release test count:
- final build warnings/errors:
- final exact-head CI:
- owner acceptance: Pending
- AC-STO-002: In progress
- AC-STO-003: In progress
- AC-STO-004: In progress
- AC-STO-005: In progress
- AC-STO-007: In progress
- AC-STO-008: In progress
- AC-STO-009: In progress
- AC-PROD-002 M07 evidence: In progress
- M08 started: No
- merge approval: Not requested / Not granted

## 7. Governance warning

M07 is authorized for implementation but this worklog is not itself a Codex execution signal. Codex execution requires the active M07 PR, Issue #4 mailbox pointer, one complete unprocessed `CODEX_HANDOFF_READY` record, prerequisite verification and Issue #4 OPEN state.
