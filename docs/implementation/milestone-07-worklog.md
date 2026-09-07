# M07 worklog — pairing, target-directed handoff and disaster recovery

**Status:** Authorized / In progress — WP0/WP1 executed
**Prepared:** 2026-09-07  
**Authorized:** 2026-09-07  
**Execution gate:** OPEN — verified against GitHub Issue #4 on 2026-09-07
**Contract:** `docs/implementation/milestone-07-pairing-handoff-disaster-recovery.md`  
**Authorization:** `docs/implementation/milestone-07-authorization.md`

This file is the durable execution/evidence log for M07. Project-owner implementation authorization, the active PR/mailbox pointer, the valid handoff and the OPEN Issue #4 gate were verified before implementation continued.

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
- Active M07 PR: #13 — `M07: pairing, target-directed handoff and disaster recovery`.
- Active implementation head at continuation: `40137b201b59c907bd0e7009d8cfae78deac14f9` before local WP0/WP1 work.
- `CODEX_HANDOFF_READY`: `M07-IMPLEMENTATION-01`, top-level and unprocessed at continuation.
- Issue #4 execution gate: OPEN.

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

**Status:** Passed
**Commit(s):** `ce52abb` — canonical M07 authority-state foundation
**Tests/evidence:** Inherited Release baseline 364/364 Passed; post-change Release suite 367/367 Passed; `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings/0 errors. Static audit covered catalogue/settings/order-entry/order-lifecycle writers and confirmed the single M06 guard is checked before persistence.
**Findings:** No discovered M03–M05 guard bypass. M07 still requires a shared in-flight mutation fence before irreversible authority transitions; this remains a WP4 integration prerequisite.
**Next:** Preserve the guard while integrating canonical state and transition services.

### M07-WP1 — canonical authority state and M06 migration

**Status:** Passed — bounded canonical-state slice
**Commit(s):** `ce52abb` — canonical M07 authority-state foundation
**Tests/evidence:** Canonical schema v2 is persisted only as detailed protocol metadata; `WriteAuthorityState` is derived. Exact M06 schema-v1 migration preserves Authoritative and NonAuthoritativeReadOnly semantics, maps unresolved Transitioning/RecoveryRequired to fail-closed RecoveryRequired, and preserves marker/anchor/live-database checks. Infrastructure authority tests: 67/67 Passed.
**Failure-injection evidence:** Same-volume temporary write, write-through/flush, replacement, reopen/reparse/read-back validation; injected failure before replacement leaves the prior canonical document intact.
**Next:** Freeze these DTO/store seams before WP2/WP3 lanes; do not implement transfer or DR authority transitions until the shared mutation fence and transport contracts exist.

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
