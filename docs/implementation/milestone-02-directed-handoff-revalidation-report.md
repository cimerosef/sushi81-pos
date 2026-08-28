# M02 target-directed handoff amendment revalidation report

**Gate conclusion:** `PARTIAL — real multi-device evidence still required`

This report records the revalidation authorized by `docs/implementation/milestone-02-directed-handoff-revalidation.md`. It does not amend the specification and does not authorize M03 by itself.

## Branch, commits and amended sources

- Branch: `codex/m02-directed-handoff-revalidation`
- Verification implementation commit: `c087235ab2af03506c6544d9ebdffa91a4716276`.
- Test-complete code head: `c087235ab2af03506c6544d9ebdffa91a4716276` (the final evidence/status commit follows this code head).
- PR: [#3](https://github.com/cimerosef/sushi81-pos/pull/3), open and not merged.
- Original feasibility evidence remains in `docs/implementation/milestone-02-feasibility-report.md`; it is not rewritten here.
- Amended sources: `docs/decisions/target-directed-authority-handoff.md`, `docs/architecture.md`, `docs/storage-strategy.md`, `docs/acceptance-criteria.md`, `docs/v1-specification-freeze.md`, and `docs/implementation-plan.md`.
- Evidence uses synthetic data only. No business database, customer/order/payment data, credential, account identifier or personal file listing is included.

## Authority state model

The pure model distinguishes `Authoritative`, `PreparingTransfer`, `RelinquishedPendingGrant`, and completed transfer/released states. Target-side evaluation distinguishes ordinary non-authoritative, designated target pending validation, acquired target, wrong/non-target, and stale/replayed/old-generation states. `MayBusinessWrite(device, durableAuthorityState, observedHandoffState)` is the single decision boundary used by the proof.

Normal close-and-retain leaves the source authoritative and creates no release marker. A normal transfer fixes exactly one valid, paired, distinct target and cannot be retargeted after commitment. Non-target devices never compete through claims/election.

`RetainClose()` now has one safe durable outcome even when a transfer was prepared but not yet relinquished: it cancels the uncommitted transfer and persists `Authoritative + Transfer=null + SnapshotEvidence=null + ClosedWithAuthority=true`. Restart/reopen restores the source's ability to begin a new transfer; no prepared-transfer trap or marker is left behind.

## Durable relinquishment and publication ordering

The proof requires this ordering: finish accepted writes; create and integrity-check the immutable SQLite snapshot; publish and confirm the snapshot; durably persist the exact source/target/lineage/generation/version/checksum relinquishment record; then create and synchronize the target-bound ready/grant marker. Once durable relinquishment exists, source business writes remain blocked across restart and only technical retry of the same immutable transfer is permitted.

The required safety assertion is that no execution can expose a target-releasing marker while the source still evaluates business-writable. A snapshot without its matching marker never releases authority.

The executable coordinator enforces this with `DirectedSnapshotEvidence.CaptureAsync`: the source must provide a valid same-transfer SQLite file, a successful read-only `PRAGMA integrity_check`, a SHA-256 checksum/byte length and an explicit synchronized observation before the durable state can transition to `RelinquishedBlocked`. The state store writes the transition through a temporary file with `WriteThrough`/`Flush(true)` and atomic replace. Marker publication revalidates the persisted evidence, and source `MayBusinessWrite` remains false after restart. Initialization cannot reset an existing durable state.

The receiving side has an independent `DurableTargetAcquisitionState` and atomic store. It records the local target device, source/target IDs, transfer/lineage/generation/version, canonical snapshot path, checksum/byte length, acquisition status, monotonic revision and UTC update time. `DirectedTargetAcquisitionCoordinator` validates both immutable marker artifacts and SQLite integrity first, then commits this state; the centralized lifecycle-aware write gate requires that exact durable state plus the current ledger position, so an in-memory validation flag, malformed state, failed commit, wrong target, stale generation/version or replay cannot enable writes. A fresh coordinator instance reconstructs the same decision from disk; the source and non-target devices cannot reuse the target state.

## Crash and failure matrix

| Injection point | Required result |
|---|---|
| snapshot creation or SQLite integrity failure | no marker; source retains authority |
| snapshot sync pending/timeout/error | no marker; source retains authority |
| durable-state commit failure | prior `TransferPrepared` state remains; abort is allowed |
| crash after durable relinquishment | source remains blocked; only same-transfer marker retry is accepted |
| ready/grant creation failure | source remains blocked; missing/partial marker cannot validate at target |
| marker synchronization failure | source remains blocked; retry is immutable and idempotent |
| cancellation before relinquishment | no marker; source may retain authority |
| crash immediately after durable relinquishment | source remains read-only/pending-transfer after restart; target blocked |
| crash before durable `Released` commit | source remains `RelinquishedBlocked`; retry of the same transfer reaches `Released` only after both sync observations |
| marker creation failure | source remains read-only; same transfer only may retry |
| marker sync pending/timeout/error | source remains read-only; target blocked |
| restart while pending | durable target/source/version binding is preserved |
| retarget, rollback, cancellation after relinquishment | rejected |
| source write after relinquishment | rejected |
| participant disappearance | no substitute writer; target remains blocked |

The automated tests cover each row with synthetic failure injectors; the corrected full solution result is 111 passed, 0 failed and 0 skipped.

## Target validation matrix

Target acquisition requires exact local target identity, distinct valid paired source/target identities, supported metadata, matching lineage/generation/version/source/target/checksum/size, complete synchronized snapshot and marker, SQLite integrity, non-stale durable target state, and durable target acquisition before writes enable. Wrong target, self-target, unpaired target, missing artifact, malformed/unsupported metadata, mismatch, stale/replayed handoff and old generation remain blocked.

## Continuous authority-transfer lifecycle proof

`ContinuousAuthorityLifecycleTests.SameLineageGenerationSupportsABv1ThenBAv2ThenABv3WithoutDeletingDurableState` executes three complete legs in one durable fixture, with one lineage and generation and no deletion of any durable file:

1. **Leg 1 — A → B v1.** A completes the exact handoff. B is blocked before acquisition, then observes the snapshot and both target-bound markers as `ConfirmedInSync`, validates SQLite checksum/length/integrity, durably records its target cursor, and becomes the only writable device. A and C remain blocked; `count(distinct writable devices) == 1`.
2. **Leg 2 — B → A v2.** B's v1 acquisition is promoted to B's source cursor. B completes the reverse handoff at the same lineage/generation and monotonic version `2`; A acquires and becomes the only writable device. B's v1 target evidence remains on disk but the lifecycle-aware gate classifies it as `target-acquisition-superseded`; B's Released source state and C remain blocked; the writable count remains `1`.
3. **Leg 3 — A → B v3.** A's v2 acquisition is promoted to A's source cursor. A completes A → B at version `3`; B's existing target cursor is atomically advanced from v1 to v3 (revision increment) after exact successor validation. B is again the only writable device; A's v2 cursor, B's historical v1 evidence, stale/replayed transfers and C are all blocked; the writable count remains `1`.

The test also verifies stale v1/v2 promotion is blocked, a conflicting v3 transfer ID cannot overwrite the current cursor, version 4 with a wrong next source is rejected, old Released source state cannot authorize writes, and the ledger contains the exact `[A → B, B → A, A → B]` sequence with versions `[1, 2, 3]`. Historical target evidence is retained for audit; only the latest completed lifecycle entry may be promoted, and only a target cursor matching that high-water mark may authorize business writes.

`DirectedLifecycleAuthorityGate` is the centralized lifecycle-aware decision boundary used by both source and target coordinators. It combines local device identity, durable source authority state, durable target acquisition cursor, the append-only lifecycle ledger's current lineage/generation high-water mark, current handoff version and current holder. `DirectedLifecycleLedgerStore` is append-only and durable (`WriteThrough` plus `Flush(true)`); it validates unique revisions/transfers, same lineage/generation, monotonic handoff-version progression, and previous-target → next-source progression. `DirectedContinuousLifecycleCoordinator` owns completion and target-to-next-source promotion, so promotion cannot bypass a missing completion record or discard historical evidence. The companion regression covers an unpaired target and pending Cloud Files observations; both remain fail-closed.

## N-device safety and liveness

The deterministic model includes at least source A and target/non-target devices B and C with arbitrary artifact visibility order, delayed marker, duplicate/replayed marker, stale versions/generations, malformed identity, source==target, restart/retry and retarget attempts. The target path now requires both `AcquisitionValidated` and `DurableTargetAcquisitionPersisted`; `Restarted=true` without reconstructed durable acquisition remains blocked. The safety invariant is `writable-device-count <= 1` for every modeled interleaving. The valid-path liveness invariant is that, after successful durable relinquishment, complete transport, exact target validation and durable target acquisition, the selected target can become writable. The directed protocol suite passed 32/32 and the durable handoff/target suite passed 50/50, including the continuous three-leg lifecycle/cursor regression and Cloud Files/paired-set fail-closed regressions.

## Cloud Files and transport boundary

The Windows observation boundary uses documented registered sync-root metadata and Cloud Files placeholder state. See [`GetCurrentSyncRoots`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootmanager.getcurrentsyncroots?view=winrt-26100), [`StorageProviderSyncRootInfo`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootinfo?view=winrt-26100), and [`CF_PLACEHOLDER_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_placeholder_state). The corrected regression values remain: `0x00000009` is `PLACEHOLDER | IN_SYNC`; `0x00000011` and `0x00000021` are partial; `0xffffffff` is invalid; unknown bits fail closed.

`IN_SYNC` is a narrow per-file provider state, not a distributed lock, remote acknowledgement or proof of absence of competing writers. Protocol safety comes from source-directed target binding and durable source relinquishment ordering. OneDrive is used only to transport immutable artifacts; atomic file creation, conflict naming, timing, quiet periods and propagation bounds are not mutual-exclusion primitives.

## Real transport evidence boundary

No real Device A/B run was available in this environment. The following commands are the reproducible future boundary; use only a registered sync root and synthetic IDs/data, and return technical states only:

```powershell
$project = 'tools\Sushi81.Pos.OneDriveFeasibility\Sushi81.Pos.OneDriveFeasibility.csproj'
$root = '<registered-OneDrive-root>'
$lineage = '11111111-1111-1111-1111-111111111111'
$stateA = '<synthetic-state-dir-device-a>'
$stateB = '<synthetic-state-dir-device-b>'
$ledger = '<synthetic-shared-lifecycle-ledger.jsonl>'
$generation = 7
dotnet run --project $project -c Release --no-build -- validate-root $root --json
$v1 = [guid]::NewGuid().ToString()
$a1 = dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --ledger $ledger --timeout-seconds 120 --poll-ms 500 --json | ConvertFrom-Json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --ledger $ledger --json
dotnet run --project $project -c Release --no-build -- directed-lifecycle-complete --source-state-dir $stateA --target-state-dir $stateB --ledger $ledger --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --json
dotnet run --project $project -c Release --no-build -- directed-target-promote --state-dir $stateB --ledger $ledger --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --json

# Round 2: B -> A v2, same state directories, lineage and generation.
$v2 = [guid]::NewGuid().ToString()
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateB --device device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --ledger $ledger --timeout-seconds 120 --poll-ms 500 --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateA --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --ledger $ledger --json
dotnet run --project $project -c Release --no-build -- directed-lifecycle-complete --source-state-dir $stateB --target-state-dir $stateA --ledger $ledger --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --json
dotnet run --project $project -c Release --no-build -- directed-target-promote --state-dir $stateA --ledger $ledger --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --json

# Optional Round 3: A -> B v3 proves cursor advancement on the same target.
$v3 = [guid]::NewGuid().ToString()
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --ledger $ledger --timeout-seconds 120 --poll-ms 500 --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --ledger $ledger --json
dotnet run --project $project -c Release --no-build -- directed-lifecycle-complete --source-state-dir $stateA --target-state-dir $stateB --ledger $ledger --source device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --json

# Restart/resume and stale checks use the same durable paths; no JSON is edited manually.
dotnet run --project $project -c Release --no-build -- directed-source-resume $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --ledger $ledger --json
```

`directed-target-acquire` reports the three Device B Cloud Files observations and persists the target cursor; before `directed-lifecycle-complete`, `mayBusinessWrite` remains false because the lifecycle high-water mark is not yet current. Completion appends the immutable leg, and a repeated acquire/restart observes the centralized gate. `directed-target-promote` advances the exact latest target to the next source without deleting the target cursor or ledger. A Device C check uses the same transfer metadata with `--device device-c` and must return `wrong-target` with no durable state. These real commands have not been executed here because this environment has no registered OneDrive sync root; they cannot replace deterministic safety proof.

## Build, tests and AC mapping

Verification was run on Windows 10.0.26200 x64 with .NET SDK 10.0.400 (runtime 10.0.11). Central package versions are `Microsoft.Data.Sqlite` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.0 and `MSTest` 4.0.2. `dotnet restore Sushi81.Pos.sln` passed with network access; the corrected full solution result is 111 passed, 0 failed and 0 skipped: Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, and directed durable handoff/target/lifecycle 50. `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings and 0 errors. The required self-contained `win-x64` publish with `PublishSingleFile=false` passed and produced the ignored Desktop publish directory. The non-escalated restore/publish attempts were blocked only by NuGet network policy; the escalated reruns passed.

GitHub Actions Continuous Integration for the pushed branch completed successfully; the PR checks are the authoritative per-head CI record.

Final implementation tree is limited to the M02 harness and evidence: `tools/Sushi81.Pos.OneDriveFeasibility` (Cloud Files observation, centralized lifecycle-aware authority gate, directed source coordinator, durable source/target state stores, append-only lifecycle ledger, marker/snapshot validation and directed CLI including completion, promotion and restart/resume); `tools/Sushi81.Pos.OneDriveFeasibility.Tests` (50 directed durable/source/target/lifecycle tests, including the three-leg cursor regression); `tests/Sushi81.Pos.OneDriveFeasibility.Tests` (32 pure protocol tests); unchanged M01/product projects under `src/` and their existing test projects; and the two M02 evidence documents under `docs/implementation/` and `docs/implementation-status.md`. No Catalogue, BusinessSettings, Order, Cart, Payment, pricing/VAT, printing, export, Hiboutik, pairing, disaster-recovery, archive, installer or legacy emergency-model code was added.

No real two-device OneDrive transport run was available: the harness reported zero registered sync roots in this environment. Therefore the deterministic protocol/durable-state evidence is complete, but the gate remains Partial rather than Feasible.

The amended preparation mapping is: AC-STO-002 (N-device target-directed single writer), AC-STO-003 (close and handoff ordering), AC-STO-004 (target validation/acquisition), AC-STO-005 (no silent takeover), AC-STO-007 (target-directed retention), AC-STO-008 (transport/checkpoint assumptions), AC-STO-009 (generation invalidation), and AC-STO-010 (read-only authority boundary). These remain owner-milestone criteria and are not marked Passed by M02 revalidation; the report is preparation evidence for their M07 owner milestone.

## Final gate

The amended protocol and synthetic persistence proof are conforming. Required real Device A/B transport evidence remains outstanding, so the approved conclusion is exactly:

`PARTIAL — real multi-device evidence still required`

M03 has not started. No specification was weakened and no sensitive or real business data was added.
