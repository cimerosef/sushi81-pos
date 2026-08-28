# M02 target-directed handoff amendment revalidation report

**Gate conclusion (current):** `PARTIAL — GitHub transport implementation ready; real two-device private-repository evidence required`

This report records the revalidation authorized by `docs/implementation/milestone-02-directed-handoff-revalidation.md`. It does not amend the specification and does not authorize M03 by itself. Historical OneDrive evidence remains valid but is no longer the normal handoff transport.

## GitHub transport amendment implementation evidence

The approved `docs/decisions/github-handoff-transport.md` changes only the normal target-directed transport/acknowledgement boundary. The implementation adds a small direct .NET `HttpClient` seam (`GitHubReleaseAssetTransport`) for a configurable dedicated private repository and one long-lived release (`sushi81-handoff-v1`, `Sushi81 POS Handoff Transport`, `make_latest=false`). It does not use Git history, LFS, Actions artifacts, Packages, clone/push, filesystem synchronization or the source repository as an operational store.

Snapshot publication is accepted only on HTTP 201 with `state=uploaded`, exact requested filename, exact byte size, positive immutable asset ID and a present `sha256:<64-hex>` digest matching the local hash. Any missing/contradictory status, name, size, digest, authentication/API/network/timeout/cancellation response fails closed. The token is read only from `SUSHI81_GITHUB_HANDOFF_TOKEN`, never serialized or logged; diagnostics contain no authorization header or token.

The source command preserves the exact prepared Home Device A v1 identity (`device-a` → `device-b`, lineage `ca9dfdd4-fa48-4602-b993-23ce5c52a141`, generation 7, version 1, transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`) and resumes only that transfer. It creates a strict `YYYYMMDDHHMMSS.snapshot.db`, validates SQLite/hash/size, uploads and validates the snapshot receipt, durably persists relinquishment, verifies the source write gate is false, then creates/uploads the matching `YYYYMMDDHHMMSS.grant.json`, validates its receipt, persists `Released`, and runs post-completion newest-three cleanup. The target command discovers grants by metadata, validates exact target/lineage/generation/version/transfer and referenced asset identity, downloads/hash-checks/integrity-checks the snapshot, then reuses the crash-safe pending/evidence/final-cursor acquisition ordering.

Automated fake-HTTP/synthetic tests cover strict receipts, missing digest, public repository rejection, token redaction, source ordering, target acquisition, retention grouping and the pre-existing directed lifecycle safety suite. No real GitHub token, repository, business snapshot or live device run is included. Real operator verification remains required: private handoff repository setup, A → B v1, B → A v2, source restart/resume, exact target validation and retention observation.

## Branch, commits and amended sources

- Branch: `codex/m02-directed-handoff-revalidation`
- Verification implementation commit: `0e5938d1f3fd7d7bc0af6bf2f25eed31759a01dc`.
- Latest test-complete code head: `05c59f5bffd9f21f0f9deb52f5c70d0cab033509` (the final evidence/status commit follows this code head).
- PR: [#3](https://github.com/cimerosef/sushi81-pos/pull/3), open and not merged.
- Original feasibility evidence remains in `docs/implementation/milestone-02-feasibility-report.md`; it is not rewritten here.
- Amended sources: `docs/decisions/target-directed-authority-handoff.md`, `docs/architecture.md`, `docs/storage-strategy.md`, `docs/acceptance-criteria.md`, `docs/v1-specification-freeze.md`, and `docs/implementation-plan.md`.
- Evidence uses synthetic data only. No business database, customer/order/payment data, credential, account identifier or personal file listing is included.

## Authority state model

The pure model distinguishes `Authoritative`, `PreparingTransfer`, `RelinquishedPendingGrant`, and completed transfer/released states. Target-side evaluation distinguishes ordinary non-authoritative, designated target pending validation, acquired target, wrong/non-target, and stale/replayed/old-generation states. `MayBusinessWrite(device, durableAuthorityState, observedHandoffState)` is the single decision boundary used by the proof. Each durable source state also carries a device-local `AuthorityCursor` with lineage, generation and the last locally accepted handoff version; the target durable acquisition state is the corresponding local target cursor.

Normal close-and-retain leaves the source authoritative and creates no release marker. A normal transfer fixes exactly one valid, paired, distinct target and cannot be retargeted after commitment. Non-target devices never compete through claims/election. Each participating device also has a separate atomic `local-authority-cursor.json` containing its device ID, lineage, generation, high-water handoff version, current role, optional transfer ID, revision and UTC update time. The cursor is the sole current local authority anchor; source and target files are detailed evidence, not a substitute for it.

`RetainClose()` now has one safe durable outcome even when a transfer was prepared but not yet relinquished: it cancels the uncommitted transfer and persists `Authoritative + Transfer=null + SnapshotEvidence=null + ClosedWithAuthority=true`. Restart/reopen restores the source's ability to begin a new transfer; no prepared-transfer trap or marker is left behind.

## Durable relinquishment and publication ordering

The proof requires this ordering: finish accepted writes; create and integrity-check the immutable SQLite snapshot; publish and confirm the snapshot; durably persist the exact source/target/lineage/generation/version/checksum relinquishment record; then create and synchronize the target-bound ready/grant marker. Once durable relinquishment exists, source business writes remain blocked across restart and only technical retry of the same immutable transfer is permitted.

The required safety assertion is that no execution can expose a target-releasing marker while the source still evaluates business-writable. A snapshot without its matching marker never releases authority.

The executable coordinator enforces this with `DirectedSnapshotEvidence.CaptureAsync`: the source must provide a valid same-transfer SQLite file, a successful read-only `PRAGMA integrity_check`, a SHA-256 checksum/byte length and an explicit synchronized observation before the durable state can transition to `RelinquishedBlocked`. The state store writes the transition through a temporary file with `WriteThrough`/`Flush(true)` and atomic replace. Marker publication revalidates the persisted evidence, and source `MayBusinessWrite` remains false after restart. Initialization cannot reset an existing durable state.

The receiving side has an independent `DurableTargetAcquisitionState` and atomic store. It records the local target device, source/target IDs, transfer/lineage/generation/version, canonical snapshot path, checksum/byte length, acquisition status, monotonic revision and UTC update time. `DirectedTargetAcquisitionCoordinator` validates both immutable marker artifacts and SQLite integrity first, then writes an `AcquisitionPending` local cursor, commits the target evidence, and finalizes the cursor as `AcquiredTarget`; only the complete pair can make the centralized lifecycle-aware write gate return true. A missing, malformed or inconsistent cursor therefore fails closed even when an old target file remains. A fresh coordinator instance reconstructs the same decision from disk; the source and non-target devices cannot reuse the target state. `DurableAuthorityState.AuthorityCursor` remains the embedded transfer fact, while the separate cursor retains each device's current local lineage, generation and high-water version.

## Missing local state is fail-closed

The local cursor is created at initial source initialization (`InitialAuthoritative`, high-water `0`) or exact virgin-target acquisition (`AcquiredTarget`, version `1`). Subsequent source preparation requires the same lineage/generation and exactly `high-water + 1`. If source authority state, target evidence or the local cursor is missing or malformed after participation, the device is unresolved and `MayBusinessWrite` is false. `InitializeAuthoritative` refuses to recreate a missing source state when a cursor or any `target*.json` evidence remains in the device directory, so local-state loss cannot be interpreted as a virgin device. Historical target evidence is retained but cannot restore authority without the current cursor.

Target acquisition uses the explicit crash ordering: immutable handoff validation, pending cursor, durable target evidence, then final cursor. A crash after target evidence and before the final cursor leaves the target blocked; an exact retry completes the pending cursor without replacing immutable evidence. The regression deletes both B's source state and cursor after `A -> B v1 -> B -> A v2` while retaining B's v1 target evidence; B remains blocked and source reinitialization is rejected.

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
| target evidence committed before current cursor | target remains blocked; exact retry finalizes pending cursor |
| missing/malformed current cursor after prior participation | fail closed; no source reinitialization or historical-target takeover |

The automated tests cover each row with synthetic failure injectors; the historical directed evidence result was 119 passed, 0 failed and 0 skipped. The current GitHub transport implementation adds six deterministic synthetic tests, for a current total of 125.

## Target validation matrix

Target acquisition requires exact local target identity, distinct valid paired source/target identities, supported metadata, matching lineage/generation/version/source/target/checksum/size, complete synchronized snapshot and marker, SQLite integrity, non-stale durable target state, and durable target acquisition before writes enable. Wrong target, self-target, unpaired target, missing artifact, malformed/unsupported metadata, mismatch, stale/replayed handoff and old generation remain blocked.

## Continuous authority-transfer lifecycle proof

`ContinuousAuthorityLifecycleTests.DistributedDeviceLocalCursorsSupportABv1ThenBAv2ThenABv3WithoutSharedSafetyLedger` executes three complete legs with separate local directories for devices A, B and C and one immutable synthetic OneDrive handoff directory. No write gate receives a shared lifecycle ledger or another device's local authority file:

1. **Leg 1 — A → B v1.** A completes the exact handoff using only A's local source directory. B observes the snapshot and both target-bound markers as `ConfirmedInSync`, validates SQLite checksum/length/integrity, durably records its target cursor and becomes the only writable device. A and C remain blocked; `count(distinct writable devices) == 1`.
2. **Leg 2 — B → A v2.** B promotes its v1 target using only B's local state, then completes the reverse handoff from B's local authority cursor. A acquires v2 using only A's local source/target state and immutable artifacts, becoming the only writable device. B's v1 target evidence remains on disk but B's local Released v2 cursor makes it logically stale; C remains blocked and the writable count remains `1`.
3. **Leg 3 — A → B v3.** A promotes v2 using only A's local state, then completes A → B at version `3`. B's existing target cursor advances atomically from v1 to v3 using B's local Released v2 cursor and exact successor validation. B is again the only writable device; A's v2 cursor, B's historical v1 evidence, stale/replayed transfers and C are all blocked; the writable count remains `1`.

The test also verifies stale v1/v2 promotion is blocked from the real local authority paths, a conflicting v3 transfer ID cannot overwrite the current cursor, version 4 with a wrong next source is rejected, old Released source state cannot authorize writes, delayed knowledge of any global update cannot resurrect old authority, and the optional diagnostic ledger contains the exact `[A → B, B → A, A → B]` sequence with versions `[1, 2, 3]`. Historical target evidence is retained for audit; only each device's local high-water/cursor can authorize its own business writes.

The missing-state regression then removes both B's `source-authority.json` and `local-authority-cursor.json` while retaining B's historical v1 target evidence. B's old target remains non-writable, an exact reacquisition is rejected as `local-authority-cursor-missing`, and `InitializeAuthoritative` refuses to recreate B's source because target evidence proves prior participation. Separate regressions cover a true virgin v1 target, malformed cursor, restart reconstruction, and a crash after target evidence before final cursor commit.

`DirectedLifecycleAuthorityGate` is the centralized lifecycle-aware decision boundary used by both source and target coordinators. It reads only the local durable source/target evidence and the persistent local participation cursor, then applies the active role and exact lineage/generation/version rules; a missing or inconsistent cursor is unresolved. It never reads a cross-device lifecycle ledger. `DirectedLifecycleLedgerStore` remains append-only diagnostic/audit history for synthetic assertions only; missing, delayed or malformed audit history cannot make a device writable. `DirectedContinuousLifecycleCoordinator` performs local target promotion and exposes an optional target-only audit append operation, so no operator command must read both source and target local state. The companion regression covers an unpaired target and pending Cloud Files observations; both remain fail-closed.

## N-device safety and liveness

The deterministic model includes at least source A and target/non-target devices B and C with arbitrary artifact visibility order, delayed marker, duplicate/replayed marker, stale versions/generations, malformed identity, source==target, restart/retry and retarget attempts. The target path now requires both `AcquisitionValidated` and `DurableTargetAcquisitionPersisted`; `Restarted=true` without reconstructed durable acquisition remains blocked. The safety invariant is `writable-device-count <= 1` for every modeled interleaving. The valid-path liveness invariant is that, after successful durable relinquishment, complete transport, exact target validation and durable target acquisition, the selected target can become writable. The directed protocol suite passed 32/32 and the durable handoff/target suite passed 57/57, including the independent-device three-leg lifecycle/cursor regression, delayed-knowledge proof, missing-state reinitialization block, retained-history virgin-inference block, crash-between-evidence/cursor retry, malformed cursor and Cloud Files/paired-set regressions.

## Cloud Files and transport boundary

The Windows observation boundary uses documented registered sync-root metadata and Cloud Files placeholder state. See [`GetCurrentSyncRoots`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootmanager.getcurrentsyncroots?view=winrt-26100), [`StorageProviderSyncRootInfo`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootinfo?view=winrt-26100), and [`CF_PLACEHOLDER_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_placeholder_state). The corrected regression values remain: `0x00000009` is `PLACEHOLDER | IN_SYNC`; `0x00000011` and `0x00000021` are partial; `0xffffffff` is invalid; unknown bits fail closed.

`IN_SYNC` is a narrow per-file provider state, not a distributed lock, remote acknowledgement or proof of absence of competing writers. Protocol safety comes from source-directed target binding and durable source relinquishment ordering. OneDrive is used only to transport immutable artifacts; atomic file creation, conflict naming, timing, quiet periods and propagation bounds are not mutual-exclusion primitives.

## Real OneDrive evidence — Home Device A

This section records the sanitized real transport observation supplied from Home Device A. It contains no username, account identifier, personal path, screenshot or business data. Paths are represented only as `<home-OneDrive-root>\\...` and `<home-local-state>\\device-a`.

- Windows accepted the registered OneDrive root; the run used .NET SDK 10.0.400, source `device-a`, target `device-b`, generation `7`, handoff version `1`, lineage `ca9dfdd4-fa48-4602-b993-23ce5c52a141`, and transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`.
- The synthetic local state was outside OneDrive. `directed-source-run` created `directed-fc235936-64a6-460b-bcaf-f2f0b212790e.snapshot.db` under `<home-OneDrive-root>\\Sushi81-M02-Synthetic\\DirectedHandoff`, waited 120 seconds, and safely returned `succeeded=false`, `code=snapshot-not-synchronized`, `snapshotSync.status=Unknown`, `isConfirmedInSync=false`.
- An independent observation reported `state=NotCloudPlaceholder`, `rawPlaceholderState=0`, `isConfirmedInSync=false`. Microsoft defines `CF_PLACEHOLDER_STATE_NO_STATES (0)` as “the file or directory ... is not a placeholder”; it is not an upload-complete signal. [`CF_PLACEHOLDER_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_placeholder_state)
- OneDrive web independently showed the exact snapshot remotely at 8 KB / 8192 bytes. This proves the current Cloud Files observer can produce a false negative after the remote upload is complete: a locally-created regular file can remain `NO_STATES` while being visible in the cloud.
- The source durable state remained `Mode=TransferPrepared` for the exact v1 transfer, with `snapshotEvidence=null` and `markerEvidence=null`. The handoff directory contained only the `.snapshot.db`; `.ready.json` and `.grant.json` were absent, so irreversible relinquishment was not crossed.
- No source/target state, cursor, artifact or Files On-Demand pinning was reset or deleted. The exact prepared transfer remains resumable/retryable after a future approved correction.

## Transport-readiness investigation and decision

The investigation used Microsoft documentation as the authority for every signal:

1. **Cloud Files placeholder state.** `PLACEHOLDER | IN_SYNC` is strong evidence only for a placeholder-backed item because Microsoft states that `IN_SYNC` “must be a placeholder” and its content is in sync with the cloud. `NO_STATES` means the item is not a placeholder; it therefore says neither uploaded nor not uploaded for a regular file. The existing observer correctly retains `PLACEHOLDER | IN_SYNC` and rejects `NO_STATES`, partial, invalid and unknown states.
2. **Files On-Demand attributes.** Microsoft maps `Pinned`, clearpin/locally available and `Unpinned` to user-controlled local retention states. `FILE_ATTRIBUTE_PINNED` expresses intent to keep content local; `FILE_ATTRIBUTE_UNPINNED` expresses the opposite; `RECALL_ON_OPEN`/`RECALL_ON_DATA_ACCESS` describe virtualization/local availability. None is documented as a remote acknowledgement, and `CfSetPinState` explicitly has asynchronous behavior with no completion guarantee. [`File attribute constants`](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants), [`Query and set Files On-Demand states`](https://learn.microsoft.com/en-us/sharepoint/files-on-demand-windows), [`CF_PIN_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_pin_state)
3. **Property System / storage-provider properties.** `System.StorageProviderId` identifies the provider portion of a fully qualified provider ID; `System.StorageProviderFileRemoteUri` is a provider-supplied remote URI; `System.FilePlaceholderStatus` contains placeholder status flags. These are useful diagnostics, but Microsoft does not define the provider ID or remote URI as a commit receipt for a newly-created regular file, and placeholder status retains the same placeholder precondition. [`System.StorageProviderFileRemoteUri`](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-storageproviderfileremoteuri), [`System.FilePlaceholderStatus`](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-fileplaceholderstatus), [`System.StorageProviderId`](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-storageproviderid)
4. **Provider status.** `CfGetSyncRootInfoByPath` can return documented provider status for the sync root containing a path; `SYNC_FULL` is defined as fully synced *placeholder file data*, not as a per-artifact receipt for a normal file. `StorageProviderStatusUI.ProviderState` exposes a provider-status UI container (`InSync`, `Syncing`, `Paused`, `Error`, `Offline`, `Warning`) but Microsoft provides no path-specific read/acknowledgement contract, so provider-wide `InSync` cannot be combined into a proof for this newly-created artifact. [`CfGetSyncRootInfoByPath`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/nf-cfapi-cfgetsyncrootinfobypath), [`CF_SYNC_PROVIDER_STATUS`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_sync_provider_status), [`StorageProviderStatusUI.ProviderState`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageproviderstatusui.providerstate?view=winrt-26100)

The technical gate therefore chooses **B — no documented local-only per-artifact confirmation**. No local signal can safely certify that a locally-created non-placeholder handoff artifact has reached the remote service. The available architecture options must remain an explicit future decision: (1) remote explicit verification through Microsoft Graph/OAuth, (2) an approved protocol amendment that makes target receipt/validation a transport acknowledgement while keeping source confirmation fail-closed, or (3) another documented supported transport with an equivalent per-artifact acknowledgement. This implementation does not silently choose any of them, add Graph/OAuth, or weaken source-before-relinquishment ordering.

## Read-only transport diagnostic

The harness now exposes a non-mutating diagnostic command that can inspect an existing artifact without regenerating it:

```powershell
dotnet run --project tools/Sushi81.Pos.OneDriveFeasibility/Sushi81.Pos.OneDriveFeasibility.csproj -c Release --no-build -- transport-probe <registered-OneDrive-root> <existing-artifact> --json
```

The report includes the supplied root validation and registered-root identity, path/existence, size and last-write time, file attributes (including pinned/unpinned/recall/reparse flags), Cloud Files raw/interpreted state, storage-provider properties when Windows exposes them, documented sync-root provider status when `CfGetSyncRootInfoByPath` is available, and an explicit `StorageProviderStatusUI` unavailable explanation. Missing APIs/properties are represented as `Unavailable` with an error; they never become confirmation. The command performs no writes, pinning, hydration, state/cursor access, artifact replacement or deletion and always reports local-only transport confirmation as `Blocked` for a regular newly-created file.

The diagnostic is covered by a synthetic regression that verifies `rawPlaceholderState=0` is `NotCloudPlaceholder`, the 8192-byte file remains byte-for-byte and attribute-identical, and no local state is touched. It is intentionally diagnostic-only and is not wired into the release/relinquishment observer.

## Real transport evidence boundary

The following commands are the reproducible operator boundary; use only a registered sync root and synthetic IDs/data, and return technical states only. `$stateA` and `$stateB` are separate local directories on their respective devices. No command below requires the other device's authority JSON or a shared mutable lifecycle ledger. The optional `directed-lifecycle-complete` command is diagnostic-only and is intentionally omitted from this operator flow. The Home Device A source-run observation is recorded separately above; a complete two-device lifecycle was not claimed from that single-device run.

### Device A — setup and A → B v1

Device A owns `$stateA` and runs `validate-root` plus `directed-source-run`; it reads only its local source cursor and publishes immutable handoff artifacts.

### Device B — acquire v1

Device B owns `$stateB` and runs `directed-target-acquire`, then `directed-target-promote`; it reads only its local target/source cursor and the immutable handoff artifacts.

### Device B — promote and B → A v2

After promotion, Device B reuses `$stateB` for `directed-source-run` at exact version 2. Device A's local state is not read.

### Device A — acquire v2

Device A reuses `$stateA` for `directed-target-acquire` and `directed-target-promote`; Device B's local authority JSON is not copied or inspected.

### Optional Device A — promote and A → B v3

Device A reuses `$stateA` for `directed-source-run` at exact version 3.

### Device B — acquire v3

Device B reuses `$stateB` for `directed-target-acquire`; its retained v1 target cursor advances atomically from its local released v2 cursor.

### Device A — restart/resume

Device A may rerun `directed-source-resume` for the same immutable v3 transfer using only `$stateA`; no JSON is edited manually.

```powershell
$project = 'tools\Sushi81.Pos.OneDriveFeasibility\Sushi81.Pos.OneDriveFeasibility.csproj'
$root = '<registered-OneDrive-root>'
$lineage = '11111111-1111-1111-1111-111111111111'
$stateA = '<synthetic-state-dir-device-a>'
$stateB = '<synthetic-state-dir-device-b>'
$generation = 7
dotnet run --project $project -c Release --no-build -- validate-root $root --json
$v1 = [guid]::NewGuid().ToString()
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --timeout-seconds 120 --poll-ms 500 --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --json
dotnet run --project $project -c Release --no-build -- directed-target-promote --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --json

# Round 2: B -> A v2, same local paths, lineage and generation.
$v2 = [guid]::NewGuid().ToString()
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateB --device device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --timeout-seconds 120 --poll-ms 500 --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateA --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --json
dotnet run --project $project -c Release --no-build -- directed-target-promote --state-dir $stateA --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --json

# Optional Round 3: A -> B v3 proves cursor advancement on B.
$v3 = [guid]::NewGuid().ToString()
dotnet run --project $project -c Release --no-build -- directed-source-run $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --timeout-seconds 120 --poll-ms 500 --json
dotnet run --project $project -c Release --no-build -- directed-target-acquire $root --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --json

# Restart/resume and stale checks use the same local paths; no JSON is edited manually.
dotnet run --project $project -c Release --no-build -- directed-source-resume $root --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --json
```

`directed-source-run` reports the source cursor, snapshot checksum/path, marker paths and technical sync states; it persists `Released` only after both markers are observer-confirmed `IN_SYNC`. `directed-target-acquire` reports the three Device B Cloud Files observations, validates the exact directed grant and persists the target cursor; after that local durable commit, `mayBusinessWrite=true` without any completion command. `directed-target-promote` reads only the exact local target state and advances that same device's local authority cursor without deleting target evidence. A Device C check uses the same immutable transfer metadata with `--device device-c` and must return `wrong-target` with no durable state. The complete multi-device sequence remains an operator procedure, not a claim of execution from the single Home Device A run; it cannot replace the deterministic safety proof or the transport gate.

## Build, tests and AC mapping

Verification was run on Windows 10.0.26200 x64 with .NET SDK 10.0.400 (runtime 10.0.11). Exact package versions are `Microsoft.Data.Sqlite` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.0, `MSTest` 4.0.2 and Windows SDK projection `10.0.26100.87`. `dotnet restore Sushi81.Pos.sln` passed with the approved network escalation; `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings and 0 errors; `dotnet test Sushi81.Pos.sln -c Release --no-build` passed 125, 0 failed and 0 skipped (Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, directed durable handoff/target/lifecycle/diagnostic plus GitHub transport 64); the required self-contained `win-x64` publish with `PublishSingleFile=false` passed. No real GitHub transport run was executed.

GitHub Actions Continuous Integration has completed successfully for the pushed branch; the PR checks are the authoritative per-head CI record for the final documentation head.

Final implementation tree is limited to the M02 harness and evidence: `tools/Sushi81.Pos.OneDriveFeasibility` (Cloud Files historical observation, centralized device-local lifecycle authority gate, persistent authority cursor, durable source/target stores, marker/snapshot validation, direct `HttpClient` GitHub Release Asset transport, grant schema, source/target GitHub coordinators, newest-three retention and directed CLI including promotion/restart); `tools/Sushi81.Pos.OneDriveFeasibility.Tests` (64 directed durable/source/target/lifecycle/diagnostic/GitHub transport tests, including strict receipt, token redaction, source ordering, exact target and retention tests); `tests/Sushi81.Pos.OneDriveFeasibility.Tests` (32 pure protocol tests); unchanged M01/product projects under `src/` and their existing test projects; and the M02 evidence/status documents. No Catalogue, BusinessSettings, Order, Cart, Payment, pricing/VAT, printing, export, Hiboutik, pairing, disaster-recovery, archive, installer or legacy emergency-model code was added.

The real Home Device A run above supplied historical OneDrive transport evidence, but it also demonstrated that the local observer is a false negative for a remotely-visible regular file (`NO_STATES`). That historical local-only gate remains blocked and is superseded for normal handoff by the approved GitHub transport amendment. CI run #75 verified the prior directed evidence head `49c9866576dee27b35af62b0e177464b176830eb`; the current GitHub verification head and CI run will be recorded after push.

The amended preparation mapping is: AC-STO-002 (N-device target-directed single writer), AC-STO-003 (close and handoff ordering), AC-STO-004 (target validation/acquisition), AC-STO-005 (no silent takeover), AC-STO-007 (target-directed retention), AC-STO-008 (transport/checkpoint assumptions), AC-STO-009 (generation invalidation), and AC-STO-010 (read-only authority boundary). These remain owner-milestone criteria and are not marked Passed by M02 revalidation; the report is preparation evidence for their M07 owner milestone.

## Final gate

The original competitive OneDrive design remains historically `BLOCKED`, and the real OneDrive local-acknowledgement observation remains preserved. The approved GitHub transport implementation and automated evidence are conforming for the M02 revalidation contract, but no live private-repository receipt or two-device round-trip has been executed in this environment. The current conclusion is exactly:

`PARTIAL — GitHub transport implementation ready; real two-device private-repository evidence required`

M03 has not started. No specification was weakened and no sensitive or real business data was added.
