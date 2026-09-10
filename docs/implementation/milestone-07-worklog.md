# M07 worklog — pairing, target-directed handoff and disaster recovery

**Status:** Authorized / WP10 automated closure — WP9 Passed; owner acceptance pending
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

### Review cycle 7 — WP8 re-review remediation

**Reviewed head:** `b581f438c547b0c010d531c951c5185f784f9988` — ChatGPT review `5142054269`, findings `AD` and `AE`.
**Remediation:** The new-recovery path now carries the exact `Recommended` candidate object from its single ranking discovery into `DisasterRecoveryPreparing`; only durable Preparing/Pending resume uses exact-identity `FindExactAsync`. This closes the Discover→FindExact freshness TOCTOU without timestamp ordering or a second authority model.
**AE evidence:** `M07ReviewRemediationTests.NewRecoveryUsesTheRankedSnapshotWhenAHigherRevisionAppearsDuringLookup`, `M07ReviewRemediationTests.NewRecoveryUsesTheRankedSnapshotWhenAHigherHandoffAppearsDuringLookup`, and the existing exact persisted-candidate restart test exercise the newer-candidate, equal-revision/high-water and restart identity cases.
**AD1 evidence:** New production `RecoveryCandidateDiscovery` suite `M07ProductionRecoveryCandidateDiscoveryTests` contains 13 explicit tests covering complete GitHub grant+snapshot eligibility, snapshot-only rejection, wrong-target recovery evidence, duplicate/starter/malformed/contradictory evidence, exact receipt/digest/lineage/generation binding, SQLite/schema/embedded revision validation, OneDrive checkpoint validation and timestamp-skew ordering.
**AD2/AD3 evidence:** New production `DisasterRecoveryService` suite `M07ProductionDisasterRecoveryServiceTests` contains 22 executed vectors (18 test methods plus five transport-failure data rows) covering Won/unknown re-observation/lost winner/blocked winner, exact starter cleanup, complete-occupant preservation, offline/401/403/404/5xx/timeout fail-closed behavior, Preparing/activation/Pending/staging/install/generation/membership/Authoritative crash boundaries, exact restart, and optional seed failure after durable authority.
**AD4 evidence:** The same production service suite covers stale-generation fencing, absent/corrupt current-generation seed rejection, successful old-database preservation plus exact seed install/revalidation, current-generation membership and read-only reinitialization.
**AD5 evidence:** `M07DisasterRecoveryUiTests` now has five STA/WPF tests, including shell action visibility across eligible/pending/stale/authoritative phases, first-use context/lineage/generation/transfer rendering and read-only recommended candidate selection, exact pending recovery identity with Retry-same-recovery and neutral Close only, and FR↔zh-CN runtime string coverage. Existing M03–M06 control-state preservation tests remain unchanged and were included in the full Release run.
**Historical evidence:** Exact-head CI #602 / `34228306762` verified `b581f438...` with Release `457/457`, build `0` warnings / `0` errors and successful self-contained `win-x64` publish; this is historical evidence for the reviewed baseline, not evidence for the new remediation head.
**New-head verification:** Remediation commit `caf5c157880d4446aa8ce2fef9df2e37ff091ee4`; local full Release solution `496/496` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; self-contained `win-x64` publish to ignored `artifacts/m07-wp8-remediation-10-publish` succeeded. Exact-head PR CI #604 / run `34233878222` completed `success` with Restore, Build and Test green.
**Status/boundaries:** WP8 remains **In progress pending ChatGPT re-review**. WP7 proof repository was not rerun or mutated. WP9 broad integration/closure, project-owner manual acceptance, M08 and merge remain not authorized/not started. This worklog evidence-only update is the final delivery bookkeeping for the remediation head.

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

### Review cycle 8 — WP8 review remediation `M07-WP8-REVIEW-REMEDIATION-11`

**Reviewed head:** `9ccb487c71d4c651edd71077c5ca5c68bb980996` — ChatGPT review `5142725252`, findings `AD3` through `AD5`.
**Status:** In progress — implementation remediation is limited to the authorized WP8 handoff; owner manual acceptance and merge remain unauthorized.
**AD3 remediation/evidence:** Added the production `IDisasterRecoveryFaultProbe` seam and explicit crash-boundary vectors through remote activation, durable Pending, candidate staging/validation, generation advance, membership publication and durable Authoritative persistence. The real `DisasterRecoveryService` is restarted from durable disk after each injected boundary; tests assert fail-closed pre-authoritative guards, exact RecoveryId/device/candidate/hash/revision/handoff/receipt identity, generation provenance, and post-authoritative restart behavior. Preservation/replacement boundaries verify old live data remains recoverable while replacement is validated before activation.
**AD4 remediation/evidence:** Added a real production stale-device lifecycle test: stale generation reinitialization installs and revalidates the current-generation seed, the device joins read-only, a current-generation source performs the normal target-directed handoff, and the real `TargetAcquisitionService` acquires only the exact current grant. Historical stale acquisition/transfer retry remains rejected, and old database markers are preserved across replacement.
**AD5 remediation/evidence:** Added real shown `MainWindow` phase/action vectors and shown first-use/pending dialogs. Tests verify visible enabled controls, fail-closed read-only status, exact pending identity, no retarget/cancel surface, FR↔zh-CN shown labels, and action-state preservation across M07 refresh/localization.
**Focused evidence:** `M07ProductionDisasterRecoveryServiceTests` `36/36` Passed; `M07ProductionStaleLifecycleTests` `1/1` Passed; `M07DisasterRecoveryUiTests` `6/6` Passed; all `0` failed and `0` skipped. Full Release/build/publish and exact-head CI evidence are pending completion of this handoff.
**Boundaries:** WP7 proof repository was not rerun or mutated. WP9 broad integration/closure, M08 and later milestones remain not authorized/not started; owner manual acceptance and merge remain pending.

**Final local verification before push:** Full Release solution `515/515` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; self-contained `win-x64` publish succeeded to ignored `artifacts/m07-wp8-remediation-11-publish`; `git diff --check` passed. Exact-head CI and durable `CODEX_DONE` remain pending push.

**Exact-head CI verification:** Commit `a485ba2d755fd1f4a37fd4f90701f577700191aa`, Continuous integration run #608 (`34239470323`) completed `success`; the `build-and-test` job Build and Test steps both completed `success`. This evidence-only worklog update is followed by a new exact-head CI run before final delivery.

**Evidence-count correction:** After factoring the shown-window vector into the existing M06 desktop STA sequence to keep WPF `Application` lifecycle deterministic, the focused `M07DisasterRecoveryUiTests` class count is `5/5` Passed; the new `M06DesktopTests.M07ShownMainWindowPreservesActionStateAcrossRefreshAndLocalizationOnSta` wrapper is included in the full Architecture total `112/112`. The prior focused `6/6` wording above is superseded by this correction.

**Final exact-head CI:** The correction head `bff0dc251a40e8e2ae12df3d053b414a6c216592` passed Continuous integration run #612 (`34240511714`), with `build-and-test` Build and Test both `success`. No repository or product changes follow this evidence record; the final delivery head will be the next evidence-only commit if required solely to record this result.

### Review cycle 9 — WP8 review remediation `M07-WP8-REVIEW-REMEDIATION-12`

**Reviewed head:** `cd91221414ade03592b90601223329dfd5365031` — ChatGPT review `5143427781`, focused on AF, AG and AH evidence gaps. AE, AD1 and AD2 remain accepted; AD3 and AD4 boundaries remain as previously recorded.
**Scope:** This cycle is limited to the authorized AF/AG/AH evidence remediation. WP7 proof was not rerun or mutated; no new authority source, lease/election/takeover, candidate guessing, retargeting, generation merge, offline force-authority path, WP9 closure, M08 or merge work is included.
**Implementation:** Added production-startup-equivalent authority reconstruction after durable Authoritative persistence, System-unavailable local-first restart and exact idempotent seed retry vectors, realistic historical old-generation target-bound grant/snapshot replay evidence with production stale acquisition/source-resume rejection and untouched remote assets, the remaining shown MainWindow phase matrix, shown fail-closed result/status vectors, exact Preparing/Pending identity surfaces, an explicit read-only/orientation-only first-use candidate notice, FR↔zh-CN dialog evidence, and shown M03/M04/M05 state preservation across authority refresh/localization with zero business writes.
**New evidence vectors:** `AfterAuthoritativePersistenceRestartUsesProductionStartupReconstruction`; `SeedFaultRestartRemainsAuthoritativeAndRetryIsExactIdempotent` (`BeforeSeedPublication`, `AfterSeedPublication`); `StaleGenerationHistoricalGrantAndSourceRetryRemainReadOnlyAndUntouched`; `PreparingAndPendingRecoverySurfacesShowExactPersistedIdentityOnSta`; `DisasterRecoveryDialogsRenderFrenchAndChineseWithoutChangingIdentityOnSta`; `M06DesktopTests.M07ShownFailClosedResultsRemainReadOnlyAcrossSafetyMatrixOnSta`; `M06DesktopTests.M07ShownShellPreservesM03M04M05StateAcrossRefreshAndLocalizationOnSta`; plus the expanded shown phase/action matrix and read-only candidate notice assertions. All 515 prior Release tests remain required and were not removed.
**Status:** In progress pending local full Release/build/publish and exact-head CI. Owner manual acceptance and merge remain unauthorized; WP9 and M08 remain not started.

**Final local evidence for implementation head:** Commit `9b78dda2d0dcf040c149998148ef76a3829ed792`; full Release solution `523/523` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors; self-contained `win-x64` publish to ignored `artifacts/m07-wp8-remediation-12-publish` succeeded; `git diff --check` passed.
**Exact-head CI for implementation head:** Continuous integration run #616 (`34245779234`) completed `success`; `build-and-test` Restore, Build and Test all completed `success`.
**Delivery boundary:** This evidence-only worklog update is followed by a fresh exact-head CI run before the matching `CODEX_DONE`. WP8 remains limited to this authorized remediation; owner manual acceptance, WP9, M08 and merge remain unauthorized.

### Review cycle 10 — WP9 integration/observability `M07-WP9-INTEGRATION-OBSERVABILITY-14`

**Entry baseline:** WP8 was accepted by ChatGPT review `5144819467` at head `46f6ba1338e7856b4a5812737e9af7b8dc9a2096`. The required governance-only CI optimization head `07154548cf96cd3f8f7f41260dbfeaaa0af4e649` was verified as the implementation starting point; it changes only CI trigger/concurrency policy and does not change product behavior.

**Scope:** This cycle is limited to the authorized WP9 integration, localization, dispatcher-responsiveness and observability audit. It does not redesign target-directed authority, add lease/election/claim/takeover semantics, mutate the disposable WP7 proof repository, implement WP10/M08 or later milestones, perform owner manual acceptance, or merge the PR.

**WP9.1 application mutation guard evidence:** Added deterministic application-boundary coverage for all current Catalogue, Business Settings, new-order confirmation and existing-order lifecycle mutation families. Every M07 non-writable phase maps to a non-authoritative guard state and rejects before store mutation; no durable notifier/revision signal occurs. An Authoritative vector confirms valid mutations remain enabled and notify after commit.

**WP9.2 shown WPF control matrix:** Extended the real STA `MainWindow` evidence across every declared M07 `AuthorityPhase`. All representative M03/M04/M05 mutation controls remain disabled when the central guard is non-authoritative or transitioning; authoritative and closed-retained-authority surfaces retain the valid create path.

**WP9.3 localization:** The actual shown shell verifies representative M07 action, setup, connection, DR, stale-device and pending-retry keys in both `fr-FR` and `zh-CN`, including a visible control content refresh without changing its authority state.

**WP9.4 responsiveness:** Added real STA dispatcher-pump vectors with delayed `TaskCompletionSource` gates for GitHub connection testing, self-join/System metadata, DR candidate discovery, stale reinitialization/System metadata and target-directed close transfer. The UI/event path processes a dispatcher pulse while each awaited I/O remains incomplete; no blocking sleep or dispatcher wait was introduced.

**WP9.5 observability/dependency boundary:** Extended the last-line diagnostic redactor to cover URL userinfo, Authorization headers, Bearer values, GitHub PAT forms, secret query parameters and common secret fields. Integration tests prove message and exception secrets do not reach rolling logs. Architecture source scans reject proof-repository identifiers, `SUSHI81_GITHUB_HANDOFF_TOKEN` and M02 OneDrive-feasibility runtime markers from production `src`; synthetic secret literals exist only in tests.

**WP9.6 composition/recovery audit:** Added a composition assertion for one central `WriteAuthorityGuard` injected into all mutation services, plus the existing startup/recovery and local-first authority tests remain in the full suite. No second authority/recovery system or proof composition was introduced.

**Local evidence before push:** Full Release solution `530/530` Passed, `0` failed, `0` skipped (Domain 33; Application 53; Infrastructure integration 200; Architecture/WPF 120; OneDrive feasibility 32; OneDrive tools 92). Release build passed with `0` warnings / `0` errors. Self-contained `win-x64` publish succeeded to ignored `artifacts/m07-wp9-publish`; `git diff --check` passed. Exact-head PR CI remains pending until the implementation head is pushed.

**Boundaries:** WP7 proof repository was not rerun or mutated. Project-owner M07 manual acceptance remains pending and must not be inferred from automated evidence. WP10, M08 and later milestones remain not started/unauthorized; merge remains unauthorized.

### Review cycle 11 — WP9 review remediation `M07-WP9-REVIEW-REMEDIATION-15`

**Reviewed head:** `aaad5bc06d09d9aa8dbb6ff8bb6b654c7cc43a2d` — ChatGPT review `5146794277`, findings AI, AJ and AK. WP9.1, WP9.2 and WP9.6 were accepted; this cycle is limited to the three remaining findings.

**AI remediation:** `SensitiveDataRedactor` now removes authenticated URL userinfo before email matching, preserving full `user:password@host` redaction. Secret-field matching now covers quoted JSON/common structured fields with optional whitespace while preserving existing bearer, PAT, query-string and unquoted field coverage. Direct redactor and rolling-log tests use synthetic URL, bearer/PAT/query and JSON secret vectors and assert that none reach output.

**AJ remediation:** Replaced the manually curated WP9 localization-key list with a test-side deterministic contract derived from actual M07 Desktop resource consumption in `Localization.cs`, `MainWindow.xaml` and `MainWindow.xaml.cs`; the runtime registration block is excluded from derivation. Every derived M07/authority/join key is asserted non-empty in both `fr-FR` and `zh-CN`, with explicit coverage for join success, transfer unavailable/failed, setup restart, candidate read-only and pending retry surfaces. Existing shown MainWindow phase/control and disaster-recovery dialog evidence remains in the suite.

**AK remediation:** Replaced the synthetic close-transfer delegate with the production `NormalHandoffService` behind `MainWindowCloseCoordinator`, an exact current-generation target and a delayed fake transport at the first real remote I/O point. The STA test proves dispatcher progress while the operation is incomplete, the source durable phase/central guard are already `TransferPreparing`/`Transitioning`, writes are rejected, the close action remains in progress with no final-close shortcut, and release completes the exact target-directed handoff to `ReleasedNonAuthoritative`.

**Local evidence:** Focused AI redactor and rolling-log tests `2/2` Passed; real production AK responsiveness test `1/1` Passed; mechanical AJ localization contract `1/1` Passed; complete shown M07 phase/control matrix `1/1` Passed; dependency/composition boundary tests `6/6` Passed. Full Release solution `531/531` Passed, `0` failed, `0` skipped (Domain 33; Application 53; Infrastructure integration 200; Architecture/WPF 121; OneDrive feasibility 32; OneDrive tools 92). Release build passed with `0` warnings / `0` errors; self-contained `win-x64` publish succeeded to ignored `artifacts/m07-wp9-remediation-15-publish`; `git diff --check` passed.

**Status/boundaries:** WP9 remains **In progress pending ChatGPT re-review**. WP7 proof repository was not rerun or mutated. WP10/manual acceptance, M08 and later milestones remain not started/unauthorized; merge remains unauthorized. Exact-head CI, push and matching `CODEX_DONE` remain pending delivery.

### Review cycle 12 — WP9 review remediation `M07-WP9-REVIEW-REMEDIATION-16`

**Reviewed head:** `2c367c11745b46a1713977fb5d0a6691ac110929` — ChatGPT review `5147078157`, limited to AL and AM evidence gaps. WP7/WP8, WP9.1, WP9.2, WP9.6, AJ and the accepted AI/AK production changes remain unchanged.

**AL evidence:** In `tests/Sushi81.Pos.Infrastructure.IntegrationTests/InfrastructureIntegrationTests.cs`, extended `RedactorExcludesRepresentativeSensitiveValues` with username-only URL userinfo while retaining useful URL structure, and extended `RollingFileLoggerRedactsMessageAndExceptionSecretsBeforeWriting` so both message and exception paths cover URL userinfo (including username-only), query-string, quoted JSON fields, Bearer, GitHub PAT-shaped synthetic values and additional structured secrets. Authorization-header placement is bounded so the synthetic vectors independently exercise every redaction rule. No production redactor churn was required.

**AM evidence:** In `tests/Sushi81.Pos.ArchitectureTests/M07Wp9ResponsivenessTests.cs`, strengthened `RealStaShellAndClosePathsRemainDispatcherResponsiveWhileM07IoAwaits` with in-flight fail-closed assertions for GitHub connection testing, self-join, Disaster Recovery discovery and stale reinitialization: the action remains disabled, `CanWrite` remains false, the central guard remains non-authoritative/RecoveryRequired, and durable protocol phase/identity is not promoted before the delayed operation releases. The accepted production target-directed close vector remains unchanged.

**Local evidence:** Focused AL redactor and rolling-log tests `2/2` Passed; focused AM real STA responsiveness test `1/1` Passed; prior AJ localization contract `1/1` Passed; dependency/composition boundary tests `6/6` Passed. Full Release solution `531/531` Passed, `0` failed, `0` skipped (Domain 33; Application 53; Infrastructure integration 200; Architecture/WPF 121; OneDrive feasibility 32; OneDrive tools 92). Release build passed with `0` warnings / `0` errors; self-contained `win-x64` publish succeeded to ignored `artifacts/m07-wp9-remediation-16-publish`; `git diff --check` passed. Synthetic secret literals are test-only; no real credentials, response bodies or business data were added.

**Status/boundaries:** WP9 remains **In progress pending ChatGPT re-review**. WP7 proof repository was not rerun or mutated. WP10/manual acceptance, M08 and later milestones remain not started/unauthorized; merge remains unauthorized. The reviewed head, review `5147078157`, exact test names and this evidence remain limited to the authorized AL/AM remediation.

### Review cycle 13 — WP10 automated closure `M07-WP10-AUTOMATED-CLOSURE-17`

**Entry baseline:** WP9 is accepted by ChatGPT review `5147243664` at exact production/evidence head `79db4bb8a92f401fbc40d464a2f7792d269cdb83`. WP7, WP8 and WP9 including AL/AM remain frozen and accepted. Production `src/` is frozen for this closure handoff.

**WP10-A reconciliation:** Accepted production-code candidate remains `79db4bb8a92f401fbc40d464a2f7792d269cdb83`. The final manual checklist header is now **Ready for project-owner execution — NOT EXECUTED / NOT PASSED** and records the current automated candidate/evidence without checking any owner scenario. This worklog records WP10 automated closure as complete pending ChatGPT review and owner manual acceptance; historical WP8/WP9 pending entries are preserved as historical evidence.

**WP10-B focused final M07 safety sweep:**
- Infrastructure M07/canonical/transport/handoff/acquisition/checkpoint/recovery classes: `InfrastructureIntegrationTests` 32, `PairingSystemMetadataTests` 12, `GitHubTransportTests` 17, `NormalHandoffTests` 3, `TargetAcquisitionTests` 5, `RecoveryCheckpointTests` 6, `BusinessRevisionTests` 3, `RecoveryActivationTests` 4, `M07ReviewCycle3Tests` 2, `M07ProductionRecoveryCandidateDiscoveryTests` 13, `M07ProductionDisasterRecoveryServiceTests` 41, `M07ProductionStaleLifecycleTests` 1, `M07ConfigurationSetupTests` 7, `M07ConnectionTesterTests` 4, and `M07ReviewRemediationTests` 13 — `163/163` Passed, `0` failed, `0` skipped. Representative exact vectors include `ServiceWonCommitsPendingThenAuthoritativeAfterExactDataFirstInstall`, `ProductionRecoveryCrashBoundariesRestartExactDurableIdentity`, `SuccessfulStaleReinitializationPreservesOldDataInstallsSeedAndJoinsCurrentGenerationReadOnly`, `ExactTargetAcquisitionInstallsDataBeforeDurableAuthority`, and `RedactorExcludesRepresentativeSensitiveValues` / `RollingFileLoggerRedactsMessageAndExceptionSecretsBeforeWriting`.
- Application guard: `M07Wp9GuardIntegrationTests.EveryNonWritableM07PhaseRejectsAllApplicationMutationFamiliesBeforePersistence` and `AuthoritativeGuardAllowsEachApplicationMutationFamilyAndNotifiesAfterCommit` — `2/2` Passed, `0` failed, `0` skipped.
- Architecture/WPF safety classes: `M06DesktopTests` 14, `M06StartupTests` 3, `M07DisasterRecoveryUiTests` 7, `M07SetupDesktopTests` 1, `M07Wp9ResponsivenessTests` 1, `DependencyBoundaryTests` 6, and `LocalizationTests` 5 — `37/37` Passed, `0` failed, `0` skipped. The real STA responsiveness vector is `RealStaShellAndClosePathsRemainDispatcherResponsiveWhileM07IoAwaits`.

**WP10-C clean Release verification:** `dotnet restore Sushi81.Pos.sln` succeeded; runtime-specific `dotnet restore Sushi81.Pos.sln -r win-x64` succeeded; `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with `0` warnings / `0` errors. `dotnet test Sushi81.Pos.sln -c Release --no-build` passed `531/531`, `0` failed, `0` skipped: Domain 33; Application 53; Infrastructure integration 200; Architecture/WPF 121; OneDrive feasibility tests 32; OneDrive tools 92. `git diff --check` and the non-sensitive production dependency/secret boundary scan passed; production `src` contains no proof-repository/M02 runtime marker or proof-only credential token.

**WP10-D publish candidate:** Fresh self-contained `win-x64` Release publish succeeded at ignored `artifacts/m07-wp10-final-publish`. Primary executable: `Sushi81.Pos.Desktop.exe`, `162816` bytes, SHA-256 `D9310D527BF2D64D60B3CEA65EDA04274366289576B485F08C7B02C78E826F0D`. The publish contains no customer database, authority-state file, credential file or business-data directory. Static launchability sanity passed: the native executable is present with the managed `Sushi81.Pos.Desktop.dll`, `.deps.json` and `.runtimeconfig.json` payload; this is not owner manual acceptance and no user-facing run was claimed.

**Boundaries/status:** WP7 proof repository/release/assets were not rerun, mutated or deleted. WP10 automated closure is **complete pending ChatGPT review / owner manual acceptance**. The manual checklist remains **NOT EXECUTED / NOT PASSED**; M07 is not marked Passed. M08 and later milestones were not started/authorized; merge remains unauthorized. Exact-head CI for the final closure commit and matching `CODEX_DONE` remain pending push.

### Review cycle 14 — manual-acceptance remediation `M07-MANUAL-ACCEPTANCE-REMEDIATION-18`

**Entry finding:** The project-owner review of Scenario A on the prior WP10 production candidate
`79db4bb8a92f401fbc40d464a2f7792d269cdb83` found that a genuinely fresh install could become a
new local Authoritative lineage on a later restart. Review reference: `5167820845`. The local
state was ultimately fenced by shared-lineage contradiction, but creating that local authority
before the fence was unsafe. Scenario A remains **Blocked / failed on the old candidate**.

**Scope:** This remediation is limited to the fresh-install provenance/legacy-bootstrap boundary
already authorized under M07. It does not redesign target-directed handoff, target acquisition,
disaster-recovery generation, single-winner rules, GitHub/OneDrive transport, business data,
WP7 proof assets, WP9 behavior, M08 or merge authorization.

**Root-cause remediation:** Startup preflight now creates a versioned non-authority pair only when
the beginning-of-startup evidence proves that no `live.db` and no established authority artifacts
existed: `Config/m07-fresh-install.marker` and `Data/m07-fresh-install.anchor`. Each is create-only,
write-through and flushed. A valid pair makes legacy bootstrap false regardless of migrated
`schema_migrations`; a missing half or corrupt content fails closed. Existing supported legacy
databases without the pair retain one-time bootstrap, and existing M06 authority state/marker/
anchor upgrades remain unchanged.

**Automated evidence added:**

- `InfrastructureIntegrationTests.FreshMigratedDatabaseDoesNotQualifyAsLegacyBootstrapEvidence`
  now captures pre-migration fresh provenance and proves repeated migration history cannot qualify
  as legacy evidence.
- `InfrastructureIntegrationTests.FreshInstallProvenancePartialOrCorruptEvidenceAlwaysFailsClosed`
  covers Config-only, Data-only, corrupt and crash-shaped partial provenance.
- `M06StartupTests.ProductionStartupWithExistingRecoveryShowsMainWindowAndClosesOnSta`
  exercises production `CompositionRoot` on a real STA dispatcher for repeated fresh restarts,
  shared-lineage setup, no-grant acquisition rejection, explicit self-join, exact identity/
  lineage/generation persistence and read-only restart behavior.

**Documentation:** The finding and owner boundary are recorded in
`docs/implementation/milestone-07-manual-acceptance-findings-01.md`; the final manual acceptance
checklist explicitly records Scenario A as blocked on the old candidate and requiring retest on
the replacement candidate. Exact replacement head/artifact and full Release/CI evidence are
recorded only after verification below.

**Status/boundaries:** Project-owner manual acceptance remains **not executed / not passed**;
Scenario A must be rerun on the replacement production artifact. WP7 proof repository/assets
were not rerun or mutated. M08 and later milestones remain unauthorized/not started; merge remains
unauthorized.

**Local remediation evidence:** Production/evidence commit `b58c26473e3f4158bcafb16be07a8f8c7ea40c8f`.
Focused Infrastructure provenance tests and the production M06/R18 STA sequence passed. Full
Release solution passed `532/532`, `0` failed, `0` skipped; Release build passed with `0` warnings
and `0` errors. Fresh self-contained `win-x64` publish succeeded at
`artifacts/m07-manual-remediation-18-publish`; primary `Sushi81.Pos.Desktop.exe` is `162816` bytes,
SHA-256 `C8C6B7F2420B016FEA9AE079E6B1E4D7FCB981313B2F213D9FDDD2AAED90A45C`. Exact-head CI,
project-owner retest and merge approval remain pending; these automated results do not claim M07
manual acceptance.
