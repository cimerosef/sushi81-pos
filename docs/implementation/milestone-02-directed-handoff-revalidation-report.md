# M02 target-directed handoff amendment revalidation report

**Gate conclusion:** `PARTIAL — real multi-device evidence still required`

This report records the revalidation authorized by `docs/implementation/milestone-02-directed-handoff-revalidation.md`. It does not amend the specification and does not authorize M03 by itself.

## Branch, commits and amended sources

- Branch: `codex/m02-directed-handoff-revalidation`
- Verification implementation commit: `c1d717007f6b04ff16fb6b13b92ee60756057059`.
- Test-complete code head: `c1d717007f6b04ff16fb6b13b92ee60756057059` (the final evidence/status commit follows this code head).
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

The receiving side has an independent `DurableTargetAcquisitionState` and atomic store. It records the local target device, source/target IDs, transfer/lineage/generation/version, canonical snapshot path, checksum/byte length, acquisition status, monotonic revision and UTC update time. `DirectedTargetAcquisitionCoordinator` validates both immutable marker artifacts and SQLite integrity first, then commits this state; the centralized target write gate requires that exact durable state, so an in-memory validation flag, malformed state, failed commit, wrong target, stale generation/version or replay cannot enable writes. A fresh coordinator instance reconstructs the same decision from disk; the source and non-target devices cannot reuse the target state.

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

`ContinuousAuthorityLifecycleTests.SameLineageGenerationSupportsABv1ThenBAv2WithoutDeletingDurableState` executes the complete synthetic lifecycle in one durable fixture rather than resetting to fresh state for the second leg:

1. Device A completes the exact A → B handoff at lineage `L`, generation `7`, handoff version `1`. Device B is blocked before acquisition, then observes the snapshot and both target-bound markers as `ConfirmedInSync`, validates the SQLite checksum/length/integrity, and durably records its target acquisition.
2. The recorded B acquisition is promoted to B's next authoritative source state. The prior B target-acquisition file and the append-only lifecycle ledger remain byte-for-byte present; no durable state is deleted or replaced.
3. Device B completes the exact B → A handoff at the same lineage `L` and generation `7`, now at monotonic handoff version `2`. Device A independently acquires and durably records the target state. The ledger proves the source/target sequence `[A → B, B → A]` and versions `[1, 2]`.
4. A replay/stale version `1`, a wrong next source and any mismatched identity are rejected. Restarted B reconstructs `Released` and remains non-writable; restarted A reconstructs the durable acquisition and is the only device allowed through the target write gate. The B acquisition remains on disk after the reverse leg.

`DirectedLifecycleLedgerStore` is append-only and durable (`WriteThrough` plus `Flush(true)`); it validates unique revisions/transfers, same lineage/generation, monotonic handoff-version progression, and previous-target → next-source progression. `DirectedContinuousLifecycleCoordinator` owns completion and target-to-next-source promotion, so promotion cannot reset an existing authority file, bypass a missing completion record, or discard the previous target state. The companion regression covers an unpaired target and pending Cloud Files observations; both remain fail-closed.

## N-device safety and liveness

The deterministic model includes at least source A and target/non-target devices B and C with arbitrary artifact visibility order, delayed marker, duplicate/replayed marker, stale versions/generations, malformed identity, source==target, restart/retry and retarget attempts. The target path now requires both `AcquisitionValidated` and `DurableTargetAcquisitionPersisted`; `Restarted=true` without reconstructed durable acquisition remains blocked. The safety invariant is `writable-device-count <= 1` for every modeled interleaving. The valid-path liveness invariant is that, after successful durable relinquishment, complete transport, exact target validation and durable target acquisition, the selected target can become writable. The directed protocol suite passed 32/32 and the durable handoff/target suite passed 50/50, including the continuous two-leg lifecycle and Cloud Files/paired-set fail-closed regressions.

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
dotnet run --project $project -c Release --no-build -- validate-root $root --json
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation 1 --version 1 --timeout-seconds 120 --poll-ms 500 --json
```

Copy the `transferId` from the Device A JSON result, then run independently on Device B against the same registered root (and a separate synthetic state directory):

```powershell
$stateB = '<synthetic-state-dir-device-b>'
$transferId = '<transferId-from-device-a-json>'
dotnet run --project $project -c Release --no-build -- validate-root $root --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation 1 --version 1 --transfer-id $transferId --json
```

The target command independently validates snapshot/ready/grant identity, checksum/size and SQLite integrity, persists durable acquisition, reloads it, and reports `mayBusinessWrite=true` only for Device B. A Device C check uses the same transfer metadata with `--device device-c` and must return `wrong-target` with no durable state. For a restart/resume check, rerun Device A with the same state directory and immutable transfer identity using `directed-source-resume`; it must report the durable pending/released state and keep `sourceMayBusinessWrite=false`. The continuous two-leg version-2 transition (B → A, same lineage/generation, monotonic version, target promotion and stale/replay rejection) is exercised by the synthetic lifecycle test above; it intentionally retains the durable state directory and ledger rather than resetting them. These real commands have not been executed here because this environment has no registered OneDrive sync root; they cannot replace deterministic safety proof.

## Build, tests and AC mapping

Verification was run on Windows 10.0.26200 x64 with .NET SDK 10.0.400 (runtime 10.0.11). Central package versions are `Microsoft.Data.Sqlite` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.0 and `MSTest` 4.0.2. `dotnet restore Sushi81.Pos.sln` passed with network access; the corrected full solution result is 111 passed, 0 failed and 0 skipped: Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, and directed durable handoff/target/lifecycle 50. `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings and 0 errors. The required self-contained `win-x64` publish with `PublishSingleFile=false` passed and produced the ignored Desktop publish directory. The non-escalated restore/publish attempts were blocked only by NuGet network policy; the escalated reruns passed.

GitHub Actions Continuous Integration for the pushed branch completed successfully; the PR checks are the authoritative per-head CI record.

Final implementation tree is limited to the M02 harness and evidence: `tools/Sushi81.Pos.OneDriveFeasibility` (Cloud Files observation, directed source coordinator, durable source/target state stores, append-only lifecycle ledger, marker/snapshot validation and directed CLI including restart/resume); `tools/Sushi81.Pos.OneDriveFeasibility.Tests` (50 directed durable/source/target/lifecycle tests); `tests/Sushi81.Pos.OneDriveFeasibility.Tests` (32 pure protocol tests); unchanged M01/product projects under `src/` and their existing test projects; and the two M02 evidence documents under `docs/implementation/` and `docs/implementation-status.md`. No Catalogue, BusinessSettings, Order, Cart, Payment, pricing/VAT, printing, export, Hiboutik, pairing, disaster-recovery, archive, installer or legacy emergency-model code was added.

No real two-device OneDrive transport run was available: the harness reported zero registered sync roots in this environment. Therefore the deterministic protocol/durable-state evidence is complete, but the gate remains Partial rather than Feasible.

The amended preparation mapping is: AC-STO-002 (N-device target-directed single writer), AC-STO-003 (close and handoff ordering), AC-STO-004 (target validation/acquisition), AC-STO-005 (no silent takeover), AC-STO-007 (target-directed retention), AC-STO-008 (transport/checkpoint assumptions), AC-STO-009 (generation invalidation), and AC-STO-010 (read-only authority boundary). These remain owner-milestone criteria and are not marked Passed by M02 revalidation; the report is preparation evidence for their M07 owner milestone.

## Final gate

The amended protocol and synthetic persistence proof are conforming. Required real Device A/B transport evidence remains outstanding, so the approved conclusion is exactly:

`PARTIAL — real multi-device evidence still required`

M03 has not started. No specification was weakened and no sensitive or real business data was added.
