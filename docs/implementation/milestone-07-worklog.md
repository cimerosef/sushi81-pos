# M07 worklog — pairing, target-directed handoff and disaster recovery

**Status:** Authorized / In progress — WP0–WP6 executed; WP7 hard-stop gate pending
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
**Commit(s):** `ce52abb` — canonical M07 authority-state foundation; `5e82b1c` — mutation fence
**Tests/evidence:** Inherited Release baseline 364/364 Passed; post-change Release suite 368/368 Passed; `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings/0 errors. Static audit covered catalogue/settings/order-entry/order-lifecycle writers and confirmed the single M06 guard is checked before persistence.
**Findings:** No discovered M03–M05 guard bypass. The shared asynchronous write scope now blocks authority state changes until an in-flight mutation has released its scope; production services use it around their business read/validate/commit sequence.
**Next:** Preserve the guard while integrating canonical state and transition services.

### M07-WP1 — canonical authority state and M06 migration

**Status:** Passed — bounded canonical-state slice
**Commit(s):** `ce52abb` — canonical M07 authority-state foundation; `5e82b1c` — mutation fence
**Tests/evidence:** Canonical schema v2 is persisted only as detailed protocol metadata; `WriteAuthorityState` is derived. Exact M06 schema-v1 migration preserves Authoritative and NonAuthoritativeReadOnly semantics, maps unresolved Transitioning/RecoveryRequired to fail-closed RecoveryRequired, and preserves marker/anchor/live-database checks. Infrastructure authority tests: 68/68 Passed; full Release suite before WP2/WP3 lanes: 368/368 Passed.
**Failure-injection evidence:** Same-volume temporary write, write-through/flush, replacement, reopen/reparse/read-back validation; injected failure before replacement leaves the prior canonical document intact.
**Next:** Integrate the bounded WP2/WP3 seams into the state-machine and desktop composition without allowing either seam to grant authority.

### M07-WP2 — self-join/System metadata/read-only initialization

**Status:** Bounded seam complete — authority/startup integration pending
**Commit(s):** `1414f8d`
**Tests/evidence:** `PairingSystemMetadataTests`: 4/4 Passed; current full Release suite: 389/389 Passed. OneDrive `System` lineage/device artifacts use non-overwriting same-identity retry; optional seed metadata is size/SHA-256 and SQLite integrity/schema validated.
**N-device evidence:** Concurrent same-device retry is idempotent; independent devices register without overwriting; contradictory identity/lineage/generation fails closed; missing/corrupt seed remains read-only. The lane intentionally does not create lineage, persist local device identity or change the canonical authority document.
**Next:** Integrate local immutable identity/current-generation membership into the canonical state and read-only setup flow.

### M07-WP3 — production GitHub configuration/credential/transport

**Status:** Bounded seam complete — authority/state-machine integration pending
**Commit(s):** `138f7e1`; activation-name validation fix `7e6ca3a`
**Tests/evidence:** `GitHubTransportTests`: 17/17 Passed; current full Release suite: 389/389 Passed. Dedicated private-repository/release configuration rejects the source repository, strict transport validates HTTP 201, uploaded state, exact name/size/asset ID and SHA-256 digest, and exposes list/get/download/delete seams.
**Secret/redaction evidence:** Protected credential interface is isolated from transport; API/provider failures do not echo PAT or Authorization text. A real Windows protected-credential implementation and non-mutating connection action remain integration work.
**Next:** Bind this transport to the main-owned normal-transfer and DR activation state machines; no transport receipt alone changes the write guard.

### M07-WP4 — normal source close/handoff

**Status:** Passed — bounded source state-machine slice
**Commit(s):** `e15aa3e` — source normal handoff ordering; `61e72ac` — immutable relinquished-at evidence in grant
**Tests/evidence:** `NormalHandoffTests`: 3/3 Passed; source validates exact current-generation target, enters Transitioning before remote work, persists `TransferPreparing`, creates/validates the SQLite snapshot, persists `RelinquishedPendingGrant` before constructing the target grant, and persists `ReleasedNonAuthoritative` only after the strict grant receipt. Full Release suite at the WP4 boundary: 392/392 Passed.
**Irreversible-point/restart evidence:** Pre-relinquishment failure returns to Authoritative while consuming the allocated handoff version; post-relinquishment grant failure remains pending/read-only; exact persisted transfer identity and receipts support retry without writable rollback. Cleanup is best-effort and never rolls back relinquishment.
**Next:** Verify data-first target acquisition without allowing a receipt or local membership to grant authority.

### M07-WP5 — target acquisition

**Status:** Passed — bounded data-first acquisition slice
**Commit(s):** `b55cf66`
**Tests/evidence:** `TargetAcquisitionTests`: 3/3 Passed; exact target/current-generation/lineage/version checks, strict grant/snapshot receipt validation, durable `TargetAcquisitionPending`, atomic SQLite install/reopen validation, and authority-last commit are covered. Non-target grants remain read-only; install failure leaves the transfer pending and guard fail-closed.
**Wrong-target/crash/idempotency evidence:** The pending retry path revalidates the same transfer identity and grant receipt; the installer stages by exact size/SHA-256 and validates SQLite integrity/schema before replacing `Data/live.db`. Broad restart/failure-boundary coverage remains part of the final M07 matrix.
**Next:** Add independent changed-only OneDrive DR checkpoint publication and the canonical durable business-data revision seam.

### M07-WP6 — OneDrive recovery checkpoints

**Status:** Passed — bounded checkpoint/revision slice
**Commit(s):** `dbec2d9`
**Tests/evidence:** `RecoveryCheckpointTests`: 5/5 Passed; `BusinessRevisionTests`: 3/3 Passed; full Release suite at the WP6 boundary: 403/403 Passed (0 failures, 0 skipped). Checkpoints are staged with write-through/flush, exact size/SHA-256, SQLite integrity/schema and embedded business-revision validation, then atomically finalized under `DisasterRecovery/Checkpoints/<generation>/<checkpoint-id>`.
**15-minute/newest-five/offline evidence:** Scheduler coalesces post-commit changes, enforces the injectable 15-minute boundary, persists/reloads a separate local cloud-checkpoint watermark, retains newest five valid units, leaves corrupt/incomplete units untouched, and retries publication failures without changing authority. SQLite business revision advances in the same transaction as accepted durable mutations; rollback/commit-failure tests prove it does not falsely advance, and post-commit notifications carry the canonical revision.
**Next:** Execute WP7 deterministic and disposable real-private-repository single-winner proof before any broad Disaster Recovery implementation.

### M07-WP7 — DR activation single-winner proof

**Status:** Blocked — architecture decision required
**Commit(s):** `ddb1647`
**Deterministic concurrency evidence:** `RecoveryActivationTests`: 4/4 Passed. The isolated proof primitive uses one deterministic `dr-<lineage>-g-<next-generation>.activation.json` name, strict immutable artifact binding, atomic fake create-once behavior, same-winner resume, loser read-only result, starter occupancy fail-closed behavior, and unknown create outcome re-observation. The primitive never changes `IWriteAuthorityGuard`.
**Disposable real private GitHub race/retry evidence:** Not available. No authorized disposable dedicated private handoff repository/release configuration or test credential is present in the project environment. The source-code repository is explicitly rejected by the M07 transport contract and cannot be used as a substitute; no production/customer credential or data was used.
**Result:** The mandatory real single-winner proof gate cannot be closed in this execution. Per contract, do not implement WP8 or any weaker DR takeover; await an approved disposable private-repository proof configuration/architecture decision.

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

**Head reviewed:** `0a717c883fec67b07e1bd6b73a968671a5bdd872` — review findings `A` through `F` and the M06 notifier/regression wiring review.
**Findings:** Grant retry bytes were not durably fixed before the first upload; retention trusted filename time and did not validate complete matching snapshot/grant units; production self-join/System metadata and non-DR composition were incomplete; stale same-device historical grants, visible-name collisions and the M06 local/cloud scheduler boundary lacked the required remediation evidence.
**Disposition:** Remediated in the existing M07 branch. Grant creation now uses persisted immutable timestamp evidence; retention validates complete lineage-bound units and deletes exact IDs by generation/handoff ordering; self-join is durable/read-only and authoritative startup publishes idempotent membership; production configuration/credential/test-connection, normal handoff/target acquisition composition, independent OneDrive scheduling, close choice and localized read-only onboarding are wired without WP8 DR UI. Historical grants and occupied snapshot/grant name pairs are ignored/advanced safely. No real WP7 private-repository drill was run, and WP8/M08 remain unstarted.
**Remediation head:** `5067caaf528947f29fd771bbfb6810a20017bb8b` (rebased onto governance commit `22ef4c5aaafc6cf21b9dbdfec3093c06775dbf44`).
**CI/tests:** Focused remediation/recovery tests passed; full Release suite `414/414` Passed with `0` skipped; `dotnet build Sushi81.Pos.sln --configuration Release --no-restore` passed with `0` warnings / `0` errors. Exact-head CI verification remains pending after push.

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
