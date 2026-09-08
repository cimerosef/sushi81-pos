# M07 worklog — pairing, target-directed handoff and disaster recovery

**Status:** Authorized / In progress — WP8 review remediation; owner acceptance pending
**Prepared:** 2026-09-07  
**Authorized:** 2026-09-07  
**Execution gate:** OPEN — verified against GitHub Issue #4 on 2026-09-08
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

**Status:** Integrated read-only production path — seed hydration intentionally deferred
**Commit(s):** `1414f8d`
**Tests/evidence:** `PairingSystemMetadataTests`: 4/4 Passed; current full Release suite: 389/389 Passed. OneDrive `System` lineage/device artifacts use non-overwriting same-identity retry; optional seed metadata is size/SHA-256 and SQLite integrity/schema validated.
**N-device evidence:** Concurrent same-device retry is idempotent; independent devices register without overwriting; contradictory identity/lineage/generation fails closed; missing/corrupt seed remains read-only. The lane intentionally does not create lineage, persist local device identity or change the canonical authority document.
**Next:** Preserve the restart/evidence fence while completing the separately authorized WP7 proof.

### M07-WP3 — production GitHub configuration/credential/transport

**Status:** Bounded seam complete — authority/state-machine integration pending
**Commit(s):** `138f7e1`; activation-name validation fix `7e6ca3a`
**Tests/evidence:** `GitHubTransportTests`: 17/17 Passed; current full Release suite: 389/389 Passed. Dedicated private-repository/release configuration rejects the source repository, strict transport validates HTTP 201, uploaded state, exact name/size/asset ID and SHA-256 digest, and exposes list/get/download/delete seams.
**Secret/redaction evidence:** Protected credential interface is isolated from transport; API/provider failures do not echo PAT or Authorization text. A real Windows protected-credential implementation and non-mutating connection action remain integration work.
**Next:** Bind this transport to the main-owned normal-transfer and DR activation state machines; no transport receipt alone changes the write guard.

### M07-WP4 — normal source close/handoff

**Status:** Passed — bounded source state-machine plus exact pending-resume seam
**Commit(s):** `e15aa3e` — source normal handoff ordering; `61e72ac` — immutable relinquished-at evidence in grant
**Tests/evidence:** `NormalHandoffTests`: 3/3 Passed; source validates exact current-generation target, enters Transitioning before remote work, persists `TransferPreparing`, creates/validates the SQLite snapshot, persists `RelinquishedPendingGrant` before constructing the target grant, and persists `ReleasedNonAuthoritative` only after the strict grant receipt. Full Release suite at the WP4 boundary: 392/392 Passed.
**Irreversible-point/restart evidence:** Pre-relinquishment failure returns to Authoritative while consuming the allocated handoff version; post-relinquishment grant failure remains pending/read-only; exact persisted transfer identity and receipts support retry without writable rollback. Cleanup is best-effort and never rolls back relinquishment.
**Next:** Preserve irreversible relinquishment while completing the separately authorized WP7 proof.

### M07-WP5 — target acquisition

**Status:** Passed — data-first acquisition with current-generation round-trip re-entry
**Commit(s):** `b55cf66`
**Tests/evidence:** `TargetAcquisitionTests`: 3/3 Passed; exact target/current-generation/lineage/version checks, strict grant/snapshot receipt validation, durable `TargetAcquisitionPending`, atomic SQLite install/reopen validation, and authority-last commit are covered. Non-target grants remain read-only; install failure leaves the transfer pending and guard fail-closed.
**Wrong-target/crash/idempotency evidence:** The pending retry path revalidates the same transfer identity and grant receipt; the installer stages by exact size/SHA-256 and validates SQLite integrity/schema before replacing `Data/live.db`. Broad restart/failure-boundary coverage remains part of the final M07 matrix.
**Next:** Preserve acquisition startup evidence and stale-generation fencing while completing the separately authorized WP7 proof.

### M07-WP6 — OneDrive recovery checkpoints

**Status:** Passed — bounded checkpoint/revision slice
**Commit(s):** `dbec2d9`
**Tests/evidence:** `RecoveryCheckpointTests`: 5/5 Passed; `BusinessRevisionTests`: 3/3 Passed; full Release suite at the WP6 boundary: 403/403 Passed (0 failures, 0 skipped). Checkpoints are staged with write-through/flush, exact size/SHA-256, SQLite integrity/schema and embedded business-revision validation, then atomically finalized under `DisasterRecovery/Checkpoints/<generation>/<checkpoint-id>`.
**15-minute/newest-five/offline evidence:** Scheduler coalesces post-commit changes, enforces the injectable 15-minute boundary, persists/reloads a separate local cloud-checkpoint watermark, retains newest five valid units, leaves corrupt/incomplete units untouched, and retries publication failures without changing authority. SQLite business revision advances in the same transaction as accepted durable mutations; rollback/commit-failure tests prove it does not falsely advance, and post-commit notifications carry the canonical revision.
**Next:** Execute WP7 deterministic and disposable real-private-repository single-winner proof before any broad Disaster Recovery implementation.

### M07-WP7 — DR activation single-winner proof

**Status:** Proof environment authorized / real proof pending execution
**Commit(s):** `ddb1647`
**Deterministic concurrency evidence:** `RecoveryActivationTests`: 4/4 Passed. The isolated proof primitive uses one deterministic `dr-<lineage>-g-<next-generation>.activation.json` name, strict immutable artifact binding, atomic fake create-once behavior, same-winner resume, loser read-only result, starter occupancy fail-closed behavior, and unknown create outcome re-observation. The primitive never changes `IWriteAuthorityGuard`.
**Disposable real private GitHub race/retry evidence:** Not run at the earlier WP7 boundary. The owner has since separately authorized disposable private repository `cimerosef/sushi81-pos-handoff-m07-proof` on `main`; no production/customer credential or data was used, and the real proof remains pending its separate handoff.
**Result:** The isolated proof environment is now separately owner-authorized, but the real private-repository proof has not been executed. The mandatory stop/go gate remains open; do not implement WP8 or any weaker DR takeover until the separate proof handoff completes.

#### WP7 execution update — `M07-WP7-REAL-PROOF-07` — 2026-09-08

**Status:** Passed — real private GitHub proof completed.
**Implementation/evidence:** `9023250` adds the explicit proof tool; the durable report is `docs/implementation/m07-wp7-real-github-proof.md`.
**Proof repository/release:** Private `cimerosef/sushi81-pos-handoff-m07-proof`, release `m07-wp7-proof-v1`, release id `384571289`. Synthetic disposable assets are retained for review; no production/customer data or credential was persisted.
**Concurrent race:** Selected deterministic asset `dr-9d1713f59d41430f87187b0aa5e19037-g-8.activation.json`, remote asset id `550176057`; one accepted contender, one fail-closed `BlockedNoWinner` observation, one remote asset, same-winner retry `ResumedSameWinner`, different-device retry `LostToExistingWinner`, loser not accepted, server/local SHA-256 equal `bdc5ef0e59a762560af654507c620c45b4d0dcb4006cefbb226b9823c1809926`, strict artifact validation passed.
**Unknown create outcome:** Selected deterministic asset `dr-7049af930fa443008153e992eba6b2e5-g-12.activation.json`, remote asset id `550176156`; first and exact retry `ResumedSameWinner`, different-device retry `LostToExistingWinner`, one remote asset, server/local SHA-256 equal `59c58321ba95edf30c0cb09be023ae8badb1a9afe11008786c50fbd25e2e3d5e`, strict artifact validation passed.
**Verification:** Full Release solution tests `444/444` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors. Detailed evidence is in `docs/implementation/m07-wp7-real-github-proof.md`.
**Boundary:** WP8 remains not started/forbidden; M08 and later milestones remain not started/unauthorized. Owner manual acceptance and merge remain pending.

### M07-WP8 — production Disaster Recovery/stale generation/reinit

**Status:** In progress — implementation pushed for authorized handoff `M07-WP8-PRODUCTION-DR-08`; owner manual acceptance and merge remain unauthorized
**Commit(s):** `ae211e3` — production WP8 implementation, tests and integration wiring
**Tests/evidence:** Local full Release solution `448/448` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; self-contained `win-x64` publish succeeded. No real WP7 proof repository was rerun or mutated.
**Replacement-PC evidence:** Production candidate discovery/order, explicit quarantine confirmation, online activation, data-first restore and current-generation read-only reinitialization paths are implemented; Windows/project-owner acceptance remains pending.
**Generation/fencing evidence:** Canonical DR preparing/pending evidence, exact `System` generation advance, startup stale fencing and read-only reinitialization are implemented; exact-head CI evidence is pending.
**Next:** Publish the final documentation/evidence head, obtain exact-head CI, then post the matching durable handoff completion report.

### M07-WP9 — WPF/localization/observability/integration

**Status:** Pending WP8 completion — desktop/localization/observability integration is being finalized within the authorized WP8 handoff; owner acceptance remains pending
**Commit(s):** `f780762` — cycle-2 desktop close arbiter and read-only actions; `1c05e12` — cycle-3 pending-transfer resume/status, authority-derived close/setup visibility and immediate write-control refresh; `9492e4d6cbd374b183116ac5b651015e3ad4c59d` — cycle-4 fresh production setup reachability and evidence tests; `21e0781` — cycle-5 technical-setup authority phase lock and authoritative empty-root binding.
**STA/WPF evidence:** 106/106 Architecture/WPF tests passed, including Cancel, Retain, target-directed transfer ordering, transfer failure, repeated-close reentrancy, non-authoritative close, flush failure, visible read-only M07 action/status controls, fresh/default technical setup localization/self-join visibility, and FR/zh-CN hiding of technical setup during an unsafe authority phase.
**FR/zh-CN evidence:** Localized authority close/target-selection, transferred-authority acquisition/status, connection-test/status and safe 401/403/404/credential states are present and exercised through the STA shell language round-trip.
**Responsiveness/redaction evidence:** Closing remains asynchronous and dispatcher-responsive; Retain/Cancel do not enumerate targets. Connection tests are non-mutating and expose only safe categorized status, never credential or transport detail.
**Next:** Review the exact WP8 production head, then obtain project-owner M07 manual acceptance.

### M07-WP10 — automated/CI closure before owner acceptance

**Status:** Pending WP8 completion — exact-head CI, publish evidence and owner acceptance remain pending
**Accepted production-code head candidate:** `ae211e3` — local exact candidate; exact-head CI and owner acceptance remain pending
**Release tests:** local full solution `448/448` Passed, `0` failed, `0` skipped (Domain 33; Application 49; Infrastructure integration 136; test OneDrive feasibility 32; tool OneDrive feasibility 92; Architecture/WPF 106). The prior exact-head repository-wide CI count remains historical evidence.
**Release build warnings/errors:** `0/0`
**Self-contained win-x64 publish:** Succeeded locally for the WP8 implementation candidate.
**Exact-head CI run:** Pending for the pushed WP8 implementation head.
**Latest WP7 evidence:** `M07-WP7-REAL-PROOF-07` passed with the selected real private GitHub race/unknown-outcome evidence recorded above. Final accepted WP7 implementation/documentation head before WP8 is `188f24c9ce94da86fbb2b86830fcd0568a1bdf5b`; exact-head GitHub Actions Continuous integration run #594 (`34208178930`) succeeded. WP8 is authorized/in progress; M08 and later milestones remain not started/unauthorized.

## 4. Review/remediation log

Append each ChatGPT review/remediation cycle with exact head SHA, findings, severity, Codex remediation commit(s), tests and disposition.

### Review cycle 1

**Head reviewed:** `0a717c883fec67b07e1bd6b73a968671a5bdd872` — review findings `A` through `F` and the M06 notifier/regression wiring review.
**Findings:** Grant retry bytes were not durably fixed before the first upload; retention trusted filename time and did not validate complete matching snapshot/grant units; production self-join/System metadata and non-DR composition were incomplete; stale same-device historical grants, visible-name collisions and the M06 local/cloud scheduler boundary lacked the required remediation evidence.
**Disposition:** Remediated in the existing M07 branch. Grant creation now uses persisted immutable timestamp evidence; retention validates complete lineage-bound units and deletes exact IDs by generation/handoff ordering; self-join is durable/read-only and authoritative startup publishes idempotent membership; production configuration/credential/test-connection, normal handoff/target acquisition composition, independent OneDrive scheduling, close choice and localized read-only onboarding are wired without WP8 DR UI. Historical grants and occupied snapshot/grant name pairs are ignored/advanced safely. No real WP7 private-repository drill was run, and WP8/M08 remain unstarted.
**Remediation head:** `5067caaf528947f29fd771bbfb6810a20017bb8b` (rebased onto governance commit `22ef4c5aaafc6cf21b9dbdfec3093c06775dbf44`).
**CI/tests:** Focused remediation/recovery tests passed; full Release suite `414/414` Passed with `0` skipped; `dotnet build Sushi81.Pos.sln --configuration Release --no-restore` passed with `0` warnings / `0` errors. Exact-head CI verification remains pending after push.

### Review cycle 2

**Head reviewed:** `848a7a73475fb3946d624b489c1a07899a9e5d7c` — review `5135084944`, findings `G` through `K`.
**Findings:** `G` critical close orchestration had separate `MainWindow` and `CompositionRoot` Closing handlers; `H` target acquisition had no production user/startup action or status; `I` the composed GitHub connection tester was not visible; `J` the desktop evidence seam did not cover the required M07 interaction matrix; `K` the worklog and WP9 truth were stale.
**Disposition:** `f780762` installs one MainWindow-owned close arbiter with deterministic Cancel/Retain/transfer/failure/reentrancy behavior and one orderly recovery flush; target enumeration is deferred until Transfer is explicitly selected. Read-only M07 UI now exposes target acquisition and non-mutating GitHub connection testing with FR/zh-CN safe status classification. STA/WPF tests cover close paths, visible operator actions/status and language round-trip; infrastructure tests cover credential/401/403/404 classification without exposing transport details. The implementation preserves M06 fail-closed/write-guard behavior and does not implement WP8 or M08.
**CI/tests:** Full Release suite `425/425` Passed with `0` skipped; `dotnet build Sushi81.Pos.sln --configuration Release --no-restore` passed with `0` warnings / `0` errors. Exact-head CI for pushed implementation head `17fb2c1aa0a553da638e58549e5294851fc6e0b7` is run `34161273997` — `success`; subsequent worklog commits are documentation-only. WP7 real private-repository proof was not run; WP8 and M08 remain not started. Project-owner manual acceptance remains pending; it is not the only unresolved M07 prerequisite because WP7 is still hard-blocked.

### Review cycle 3

**Head reviewed:** `02b6e0330b16802b7efa16b91d1b5c62cdcdbb07` — review `5135255189`, findings `L` through `P`.
**Remediation commit:** `1c05e12`.
**L — restart-safe self-join evidence / startup fence:** Fresh self-join now durably establishes and validates the existing independent marker/anchor evidence, retries the same local identity after a membership/evidence failure, preserves arbitrary established `RecoveryRequired`, and rejects acquisition when the current guard/startup evidence is not compatible with the canonical phase. Corrupt/deleted evidence remains fail-closed. Exact safe unbound M06 read-only migration can join without promotion.
**M — released-source re-entry:** `ReleasedNonAuthoritative` is now an eligible current-generation target phase; the deterministic A→B→A integration test proves strict newer-version acquisition, lineage/target binding and old-version replay rejection.
**N — production pending-transfer resume:** `NormalHandoffService.ResumePendingTransferAsync()` accepts no target and resumes only the durable transfer identity. Desktop operational affordances use the detailed phase, show a localized pending status/action, hide target acquisition on the source, and refresh child write-control bindings after authority changes/failures.
**O — local-first/setup semantics:** Unavailable shared System metadata is logged/deferred without revoking a valid local authority; readable contradictory metadata still fails closed. Authoritative close detection no longer depends on `NormalHandoff` availability when the M07 runtime is composed, and the non-mutating GitHub connection test is visible on the authoritative setup surface.
**P — seed readiness truth:** Self-join no longer claims a validated seed's business revision or hydrated read-only phase without installing/revalidating its SQLite payload; production self-join remains `PairedUninitializedReadOnly` until explicit hydration exists.
**Tests/evidence:** Full local Release suite `401/401` Passed, `0` skipped; Architecture/WPF `103/103`; Infrastructure integration `126/126`; Release build `0` warnings / `0` errors. WP7 real disposable private-repository proof was not run under this handoff; WP8 and M08 remain not started; merge remains unauthorized.
**Disposition:** Remediated and pushed at `b1cf28c140f8c749a8bb496091649b6bd9477207`; exact-head CI run `34163900315` is `success`. The prior local `401/401` remains historical narrower-scope evidence, not the repository-wide CI total.

### Review cycle 4

**Head reviewed:** `b1cf28c140f8c749a8bb496091649b6bd9477207` — review `5135408665`, findings `Q` through `S`.
**Findings:** A fresh/default installation could not reach self-join because the M07 runtime required a hand-edited absolute OneDrive root; non-secret GitHub transport settings were not reachable through a supported production setup surface; the worklog/test-count evidence was stale or scope-ambiguous; and the prior Remediation-04 completion record still contained non-terminal `browserNotification: pending`.
**Disposition:** Review-cycle-4 remediation adds a localized technical setup/onboarding action available before M07 runtime composition, validates the existing OneDrive root and production System lineage through `JsonSystemMetadataStore` before persistence, persists only non-secret GitHub transport values through `ILocalConfigurationService`, and requires restart before runtime composition changes. It never creates authority, membership, bootstrap evidence or business state. Fresh/default, valid persistence, invalid-root/lineage preservation, re-composition/read-only self-join, fresh-shell STA visibility and FR/zh-CN evidence are covered. The worklog now distinguishes the prior local `401/401` scope from the exact-head CI repository-wide `434/434` count, records the actual `b1cf28c`/`34163900315` evidence, and changes WP7 to `proof environment authorized / real proof pending execution`. Current execution history also verifies the Remediation-04 browser notification succeeded; the historical completion marker remains otherwise untouched.
**Remediation commit(s):** `9492e4d6cbd374b183116ac5b651015e3ad4c59d`.
**Tests/evidence:** Focused setup infrastructure tests `3/3` Passed; fresh-shell/recomposition STA/WPF tests `3/3` Passed; full local Release solution `439/439` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; exact-head CI run `34166285616` succeeded with the same `439/439` test result and `0/0` build warnings/errors.

### Review cycle 5

**Head reviewed:** `cb1db0c38993687e9b9546f80f4586ef23647de0` — review `5135562994`, findings `T` through `V`.
**Findings:** Technical setup remained writable during immutable transfer/acquisition/disaster-recovery and recovery-required phases; a stable authoritative device could not perform its first M06-to-M07 setup against an empty shared root and then publish its existing lineage on restart; the worklog needed the exact remediation evidence while preserving the pending WP7/manual-acceptance truth.
**Disposition:** `21e0781` makes the existing M07 setup service and desktop entry point phase-aware. TransferPreparing, RelinquishedPendingGrant, TargetAcquisitionPending, DisasterRecoveryPending, RecoveryRequired and StaleGeneration reject setup before any root or persistence work, and FR/zh-CN UI hides the action. A stable bound Authoritative/ClosedRetainedAuthority device may bind an empty/new root without creating lineage; the existing startup coordinator subsequently publishes the exact local lineage/generation and device membership. Existing lineage must match the local bound identity/generation, while fresh/uninitialized and non-authoritative empty-root setup fails closed. No authority, transfer, recovery, database, marker, anchor or shared metadata mutation is performed by setup.
**Remediation commit:** `21e0781`.
**Tests/evidence:** Focused setup infrastructure tests `7/7` Passed; unsafe-phase/FR/zh-CN WPF tests `3/3` Passed; full local Release solution `444/444` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; exact-head CI run `34168357623` succeeded with the same `444/444` result and `0/0` build warnings/errors. WP7 real disposable private-repository proof was not run; WP8 and M08 remain not started; manual acceptance and merge remain unauthorized.

### Review cycle 6 — WP8 review remediation

**Reviewed head:** `e28efff765f650fdb24b88334c82a98430ae63e2` — review `5141523616`, findings `W` through `AC`; the historical WP8 CI #596 / `34213557391` evidence was verified as `448/448`, `0` skipped, build `0` warnings / `0` errors.
**Remediation commit:** `92fc66a89fd8b40155ccd9aeed6a695679d1b17f`.
**W/X:** The main-owned DR service now re-discovers immediately before new Preparing, enforces the deterministic freshest candidate at the service boundary, preserves exact candidate identity on Preparing/Pending restart, and exposes phase-derived context with separate normal-path-unavailable and quarantine confirmations. The UI shows read-only diagnostic candidates, the protocol-selected candidate, lineage/generation, transfer provenance and truthful warnings; the pending surface permits only exact retry.
**Y:** Added deterministic lower-revision/lower-handoff/retarget/restart/confirmation/provenance/seed-retry coverage plus a real STA/WPF dialog test. Existing candidate discovery, activation, stale/reinitialization and crash-boundary suites remain in the full Release run; WP7 proof was not rerun or mutated.
**Z/AA/AB:** Current-generation seed publication is reconstructibly retryable and recognizes an equivalent validated seed without revoking authority; abandoned irreversible transfer provenance is retained as local-only `LastRecovery` history; Preparing/Pending uses a dedicated exact-resume surface.
**AC/status:** Full local Release solution `457/457` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; self-contained `win-x64` publish passed. Exact-head CI for `cdf1f970811e8a8a6bf4b6c68a6337439fe47f46` was run #600 (`34227982366`) and succeeded with Build/Test green; the subsequent evidence-only worklog commit is pending its own exact-head CI. WP8 remains **In progress pending ChatGPT re-review**; WP9 broad integration/closure is **not authorized / not started under this handoff**; project-owner manual acceptance remains Pending; M08 is not started; merge is not authorized.

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
