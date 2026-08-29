# M02 target-directed handoff amendment revalidation report

**Gate conclusion (current):** `PARTIAL — real A → B v1 and B → A v2 evidence complete; live retention observation remains required`

This report records the revalidation authorized by `docs/implementation/milestone-02-directed-handoff-revalidation.md`. It does not amend the specification and does not authorize M03 by itself. Historical OneDrive evidence remains valid but is no longer the normal handoff transport.

## GitHub transport amendment implementation evidence

The approved `docs/decisions/github-handoff-transport.md` changes only the normal target-directed transport/acknowledgement boundary. The implementation adds a small direct .NET `HttpClient` seam (`GitHubReleaseAssetTransport`) for a configurable dedicated private repository and one long-lived release (`sushi81-handoff-v1`, `Sushi81 POS Handoff Transport`, `make_latest=false`). It does not use Git history, LFS, Actions artifacts, Packages, clone/push, filesystem synchronization or the source repository as an operational store.

Snapshot publication is accepted only on HTTP 201 with `state=uploaded`, exact requested filename, exact byte size, positive immutable asset ID and a present `sha256:<64-hex>` digest matching the local hash. Any missing/contradictory status, name, size, digest, authentication/API/network/timeout/cancellation response fails closed. The token is read only from `SUSHI81_GITHUB_HANDOFF_TOKEN`, never serialized or logged; diagnostics contain no authorization header or token.

The source command preserves the exact prepared Home Device A v1 identity (`device-a` → `device-b`, lineage `ca9dfdd4-fa48-4002-b993-23ce5c52a141`, generation 7, version 1, transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`) and resumes only that transfer. It creates a strict `YYYYMMDDHHMMSS.snapshot.db`, validates SQLite/hash/size, uploads and validates the snapshot receipt, durably persists relinquishment, verifies the source write gate is false, then creates/uploads the matching `YYYYMMDDHHMMSS.grant.json`, validates its receipt, persists `Released`, and runs post-completion newest-three cleanup. The target command discovers grants by metadata, validates exact target/lineage/generation/version/transfer and referenced asset identity, downloads/hash-checks/integrity-checks the snapshot, then reuses the crash-safe pending/evidence/final-cursor acquisition ordering.

Automated fake-HTTP/synthetic tests cover strict receipts, missing digest, public repository rejection, token redaction, source ordering, target acquisition, retention grouping and the pre-existing directed lifecycle safety suite. The real operator run below uses only synthetic state/data and records sanitized repository, release, asset, digest and technical-state metadata; no token, business snapshot or credential is included. Private repository setup, A → B v1, B → A v2, source restart/resume and exact target validation are now evidenced. A destructive live retention observation remains the sole outstanding operator evidence.

## Correction pass — GitHub wrapper lifecycle review

The latest correction pass adds deterministic regression coverage for the full GitHub wrapper lifecycle and keeps the gate Partial pending the live retention observation:

- Create Release sends `make_latest` as the documented JSON string `"false"`; HTTP 201 is accepted and HTTP 422 is fail-closed.
- Target grant validation now requires the complete immutable identity tuple, including `sourceDeviceId`, before any local durable mutation.
- A persisted pre-relinquishment snapshot receipt is transfer-bound and restart-safe: the exact GitHub asset is re-read and checked for release, uploaded state, name, size and digest, while the current local SQLite integrity/hash/size is recomputed before relinquishment. Missing, corrupt or changed receipt/snapshot leaves `TransferPrepared` and creates no grant. A valid receipt belonging to an earlier completed transfer is retained as history but ignored for a later transfer on the same device, which reserves a fresh collision-free snapshot name.
- After relinquishment a fresh coordinator is reconstructed from disk and its source write gate is asserted false before grant creation/upload.
- Retention validates complete units release-wide and retains exactly the newest three across all lineages, ordered by generation, handoff version and deterministic metadata tie-breakers; filename timestamps never determine authority order. A durable exact-ID deletion plan makes grant-success/snapshot-failure cleanup resumable, retry-safe and 404-idempotent, while protected current assets remain untouched.
- Release Asset listing follows `per_page=100` pagination until the complete collection is read. Network, timeout, authentication, permission, rate-limit and malformed receipt failures surface as structured secret-free transport errors.
- The continuous-lifecycle fixture now passes its requested version argument through to the transfer identity rather than hard-coding v1.
- The GitHub target wrapper now supplies the device-local `source-authority.json` when reconstructing the target coordinator. This preserves the exact Released predecessor and enables the real wrapper sequence A → B v1, B → A v2, then A → B v3 without treating a returning device as virgin.
- Grant upload failure after relinquishment returns a retryable fail-closed result; a grant already uploaded before a Released commit interruption is discovered by exact name/size/digest and is not duplicated on restart.
- Recovery hardening now removes an exact-ID `starter` asset left behind by a GitHub `502`/gateway failure and performs one bounded upload retry; a complete same-name asset is reused only when its immutable size and digest match.
- Snapshot receipts and grant artifacts are written through temporary files with write-through/durable flush and atomic replacement. Existing grant files use strict JSON deserialization and complete immutable-identity validation before upload, so truncated or malformed artifacts fail closed without creating a duplicate.
- Retention cleanup re-enumerates after completing any persisted old deletion plan and converges again to exactly the newest three release-wide units, covering a newer handoff arriving during retry.
- GitHub-specific target regressions cover truncated/hash-mismatched downloads, corrupt SQLite content whose remote metadata is internally consistent, duplicate-name HTTP 422, upstream HTTP 502/starter responses, and timestamp-collision name reservation.

The correction-pass fake-HTTP matrix includes release payload/422, repository/token/auth/rate-limit failures, upload state/name/size/digest contradictions, duplicate-name 422, upstream 502/starter, pagination, wrong-source target rejection, receipt restart/revalidation, generation advancement, release-wide retention, grant upload/restart and pre-commit recovery, target truncation/hash/SQLite corruption, timestamp collision, partial-delete retry/idempotency, starter deletion/retry, malformed grant rejection, crash-atomic artifact writes, post-plan retention reconvergence and network/timeout redaction. Full solution verification is 153 passed, 0 failed, 0 skipped (Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, GitHub wrapper/harness 92); build is 0 warnings/0 errors; self-contained win-x64 publish passed. CI must be green for the final pushed head. The preceding correction-pass verification was synthetic/fake-HTTP only; sanitized real operator evidence is recorded below.

## Real private-repository operator evidence — Device A and Device B

This section records the completed real two-device GitHub transport run using synthetic M02 state/data only. It contains no token, PAT, authorization header, business snapshot contents or personal file listing.

**Transport container**

- Dedicated private repository: `cimerosef/sushi81-pos-handoff`.
- Long-lived release: tag `sushi81-handoff-v1`, release ID `378925560`.
- The release was inspected after the return transfer; exactly four uploaded assets were present, representing two complete handoff units. With only two units, the approved newest-three cleanup had no eligible deletion, so destructive live retention behavior was not observed.

**A → B v1 — source publication and target acquisition**

- Transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`, lineage `ca9dfdd4-fa48-4002-b993-23ce5c52a141`, generation `7`, handoff version `1`, source `device-a`, target `device-b`.
- Snapshot `20260829104435.snapshot.db`, asset ID `534990583`, size `8192`, digest `sha256:67ec69e5e1cbb73fd0b312a759390e6696f87563a2c0459eee1d299a260091e1`.
- Grant `20260829104435.grant.json`, asset ID `534990601`, size `699`, digest `sha256:b37034026a16b7f219068dfcd40754674e10bb5c9e4914e83b1b101116e72dab`.
- Device A reached durable `Released`; a fresh process reload confirmed `mayBusinessWrite=false`. Device B validated the exact grant/snapshot pair and acquired the target-bound state.

**B → A v2 — return publication and restart reconstruction**

- Transfer `e55cc196-cd67-4b9a-a15e-662ac3e0f0ea`, same lineage `ca9dfdd4-fa48-4002-b993-23ce5c52a141`, generation `7`, handoff version `2`, source `device-b`, target `device-a`.
- Snapshot `20260829125825.snapshot.db`, asset ID `535105715`, size `8192`, digest `sha256:67ec69e5e1cbb73fd0b312a759390e6696f87563a2c0459eee1d299a260091e1`.
- Grant `20260829125825.grant.json`, asset ID `535105732`, size `699`, digest `sha256:5bb7c3f441990a58121538ed97a574c5e074416260308ff41b86c22ae21e5bbd`.
- Device B reached durable `Released`; immediately before acquisition a fresh Device A state read confirmed `Released(v1)` and `mayBusinessWrite=false`. Device A exact v2 acquisition returned `target-acquired`, `acquisitionSucceeded=true`, `mayBusinessWrite=true`, durable `Acquired`, and the exact downloaded snapshot checksum/8192-byte length. An independent fresh process repeated the same v2 acquisition as `already-acquired` with the same durable state, proving restart/reconstruction rather than a second handoff.
- The grant was independently read on Device A and validated as protocol version 1, generation 7, handoff version 2, source `device-b`, target `device-a`, `IsValid=true`.

The real run therefore closes private repository/release access, valid server receipts, authenticated target download/validation, A → B v1, B → A v2, source restart/resume, exact target filtering and at-most-one-writer observations for the exercised states. It does not close live destructive retention observation; the M02 gate remains Partial until that remaining evidence (or an explicit approved waiver) is recorded.

## Branch, commits and amended sources

- Branch: `codex/m02-directed-handoff-revalidation`
- Correction-pass implementation head: `edc7fdc5ccdd75f8d97f9cb0d6c2bb239a95dea8` (recovery-hardening code/tests; final branch head is recorded in PR #3 checks).
- Correction-pass CI status: the final pushed branch-head result is recorded in PR #3 checks.
- PR: [#3](https://github.com/cimerosef/sushi81-pos/pull/3), open and not merged.
- Original feasibility evidence remains in `docs/implementation/milestone-02-feasibility-report.md`; it is not rewritten here.
- Amended sources: `docs/decisions/target-directed-authority-handoff.md`, `docs/architecture.md`, `docs/storage-strategy.md`, `docs/acceptance-criteria.md`, `docs/v1-specification-freeze.md`, and `docs/implementation-plan.md`.
- Current GitHub automated transport evidence is synthetic/fake-HTTP; sanitized real private-repository operator evidence is recorded in the dedicated section above. The report also preserves sanitized real historical Home Device A OneDrive evidence. No business database, customer/order/payment data, credential, account identifier or personal file listing is included.

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

The automated tests cover each row with synthetic failure injectors; the historical directed evidence remains recorded separately. The current solution verification is 153 passed, 0 failed and 0 skipped, including 92 GitHub wrapper/harness tests and 32 pure protocol tests.

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

## Real OneDrive evidence — Home Device A (historical)

This section records the sanitized real transport observation supplied from Home Device A. It contains no username, account identifier, personal path, screenshot or business data. Paths are represented only as `<home-OneDrive-root>\\...` and `<home-local-state>\\device-a`.

- An earlier operator attempt used the wrong lineage `ca9dfdd4-fa48-4602-b993-23ce5c52a141`; the implementation returned `transfer-state-mismatch`, left the source at the safe prepared state and created no remote asset. This is retained only as fail-closed historical evidence and is not the real transfer identity. The corrected real GitHub run uses lineage `ca9dfdd4-fa48-4002-b993-23ce5c52a141` as recorded above.
- Windows accepted the registered OneDrive root; the historical observation used .NET SDK 10.0.400, source `device-a`, target `device-b`, generation `7`, handoff version `1`, and transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`.
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

## Isolated live-retention preparation (not executed)

This section is an operator plan only. No live GitHub handoff, retention cleanup, asset deletion, or device test was executed for this preparation pass. The current gate remains `PARTIAL` and retention is still outstanding.

### Safety decision and isolation boundary

The current CLI has a safe same-home sequence without copying or forging another device's durable authority state: `github-directed-target-acquire` writes the target's own cursor, then the existing transport-independent `directed-target-promote` advances that same local cursor to source authority. `github-directed-source-run` then performs the next exact target-directed transfer and runs post-completion retention. Each synthetic device uses a separate state directory; no command below may reference `C:\Users\zshu\Documents\Sushi81-M02-Test\device-a`, `C:\Users\zshu\Documents\Sushi81-M02-Test\device-b`, `live.db`, application data, or a copied authority JSON.

The drill must use a **new disposable GitHub Release** (for example, a unique `sushi81-retention-prep-<date>` tag) in the already-approved private handoff repository, or another separately approved disposable private handoff repository. Do not use the real `sushi81-handoff-v1` release. Retention is release-wide: adding four disposable units to the current release would make the known real v1 pair (`snapshot asset 534990583`, `grant asset 534990601`) an eligible old-unit candidate and could also make the real v2 pair eligible. Those real evidence assets are never in the deletion scope of this plan. If the preflight release is not empty or cannot be proven disposable, stop before the first source command.

The CLI performs cleanup inside the source command immediately after `Released`; therefore the transient fourth-unit count is an expected internal transition (eight complete assets before cleanup) rather than a separately inspectable remote state. The externally verifiable result is the exact pre-v4 six-asset set followed by a post-v4 six-asset set in which only the recorded oldest disposable pair is absent. This limitation must be stated in the evidence; it must not be “proved” by pausing or bypassing cleanup.

### One-step-at-a-time operator sequence

Run each step only after inspecting the previous JSON result. Within a grouped code block, execute each command as a separate invocation and wait for its result before continuing. Use a fresh disposable tag and fresh synthetic state directories; never paste the token into a command or evidence. The token is supplied only in the process environment as `SUSHI81_GITHUB_HANDOFF_TOKEN`.

```powershell
$project = 'tools\Sushi81.Pos.OneDriveFeasibility\Sushi81.Pos.OneDriveFeasibility.csproj'
$owner = 'cimerosef'
$repo = 'sushi81-pos-handoff'
$tag = 'sushi81-retention-prep-<unique-date>'
$lineage = [guid]::NewGuid().ToString()
$generation = 1
$prep = Join-Path $env:TEMP ('Sushi81-M02-RetentionPrep-' + $lineage)
$stateA = Join-Path $prep 'device-a'
$stateB = Join-Path $prep 'device-b'
```

1. Bootstrap/check only the disposable release. Expect the release to be newly created or otherwise explicitly confirmed empty; stop on any unexpected pre-existing asset.

   ```powershell
   dotnet run --project $project -c Release -- github-transport-check --owner $owner --repo $repo --release-tag $tag --create-release --json
   ```

2. Record the initial read-only remote inspection. Expected complete-asset count: `0`.

   ```powershell
   dotnet run --project $project -c Release -- github-remote-inspect --owner $owner --repo $repo --release-tag $tag --json
   ```

3. Create disposable A → B v1. Use the same `$v1` value for every v1 command.

   ```powershell
   $v1 = [guid]::NewGuid().ToString()
   dotnet run --project $project -c Release -- github-directed-source-run --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --owner $owner --repo $repo --release-tag $tag --json
   ```

   Expect source `Released`, `sourceMayBusinessWrite=false`, and two complete assets (one snapshot plus one grant); retention deletes nothing.

4. Inspect and record the two v1 asset IDs, then acquire and promote v1 in the separate B directory.

   ```powershell
   dotnet run --project $project -c Release -- github-remote-inspect --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- github-directed-target-acquire --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- directed-target-promote --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 1 --transfer-id $v1 --json
   ```

5. Create B → A v2 with the same lineage/generation and `$v2` used for both v2 commands. Expect the remote count to move from `2` to `4`, with no deletion.

   ```powershell
   $v2 = [guid]::NewGuid().ToString()
   dotnet run --project $project -c Release -- github-directed-source-run --state-dir $stateB --device device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- github-directed-target-acquire --state-dir $stateA --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- directed-target-promote --state-dir $stateA --device device-a --source device-b --target device-a --lineage $lineage --generation $generation --version 2 --transfer-id $v2 --json
   ```

6. Create A → B v3 and acquire it in B. Before starting v4, inspect and preserve the exact six-asset list; this is the proof that the first three complete units caused no retention deletion.

   ```powershell
   $v3 = [guid]::NewGuid().ToString()
   dotnet run --project $project -c Release -- github-directed-source-run --state-dir $stateA --device device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- github-directed-target-acquire --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --owner $owner --repo $repo --release-tag $tag --json
   dotnet run --project $project -c Release -- directed-target-promote --state-dir $stateB --device device-b --source device-a --target device-b --lineage $lineage --generation $generation --version 3 --transfer-id $v3 --json
   dotnet run --project $project -c Release -- github-remote-inspect --owner $owner --repo $repo --release-tag $tag --json
   ```

7. Create B → A v4. The source command first completes the fourth unit (logical count `8`) and then performs its post-completion cleanup. Record the v1 disposable snapshot/grant IDs from step 4 before running this command.

   ```powershell
   $v4 = [guid]::NewGuid().ToString()
   dotnet run --project $project -c Release -- github-directed-source-run --state-dir $stateB --device device-b --target device-a --lineage $lineage --generation $generation --version 4 --transfer-id $v4 --owner $owner --repo $repo --release-tag $tag --json
   ```

8. Perform a final read-only inspection. Expected result: exactly six complete assets (three snapshot/grant units), the recorded disposable v1 pair is absent, every v2/v3/v4 pair remains, and no unrelated asset was deleted. Record the source `Released` state and all four transfer identities. Leave this disposable release and its synthetic state directories intact for audit; do not manually delete or edit JSON.

   ```powershell
   dotnet run --project $project -c Release -- github-remote-inspect --owner $owner --repo $repo --release-tag $tag --json
   ```

If any command reports a mismatched identity, stale/replayed version, unexpected asset, incomplete receipt, non-empty preflight release, or count other than the expected transition, stop and preserve the durable synthetic state for review. Do not retry with changed IDs and do not touch the real release. A successful sequence would provide the missing live retention observation but would not by itself change the gate wording until the evidence is reviewed.

## Build, tests and AC mapping

Verification was run on Windows 10.0.26200 x64 with .NET SDK 10.0.400 (runtime 10.0.11). Exact package versions are `Microsoft.Data.Sqlite` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.0, `MSTest` 4.0.2 and Windows SDK projection `10.0.26100.87`. `dotnet restore Sushi81.Pos.sln` passed with the approved network escalation; `dotnet build Sushi81.Pos.sln -c Release --no-restore` passed with 0 warnings and 0 errors; `dotnet test Sushi81.Pos.sln -c Release --no-build` passed 153, 0 failed and 0 skipped (Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, GitHub wrapper/harness 92); the required self-contained `win-x64` publish with `PublishSingleFile=false` passed. The preceding automated verification is synthetic/fake-HTTP; the real private-repository run is recorded in the operator-evidence section above.

GitHub Actions Continuous Integration must complete for the newly pushed branch head; the PR checks will be the authoritative per-head CI record for the final documentation head.

Final implementation tree is limited to the M02 harness and evidence: `tools/Sushi81.Pos.OneDriveFeasibility` (Cloud Files historical observation, centralized device-local lifecycle authority gate, persistent authority cursor, durable source/target stores, marker/snapshot validation, direct `HttpClient` GitHub Release Asset transport, grant schema, source/target GitHub coordinators, release-wide newest-three retention and directed CLI including promotion/restart); `tools/Sushi81.Pos.OneDriveFeasibility.Tests` (92 directed durable/source/target/lifecycle/diagnostic/GitHub wrapper and transport tests, including strict receipt, token redaction, source ordering, exact target, continuous A→B→A→B→A→B lifecycle, upload-retry, exact-ID starter recovery, crash-atomic artifacts, strict grant validation, download-integrity and retention reconvergence tests); `tests/Sushi81.Pos.OneDriveFeasibility.Tests` (32 pure protocol tests); unchanged M01/product projects under `src/` and their existing test projects; and the M02 evidence/status documents. No Catalogue, BusinessSettings, Order, Cart, Payment, pricing/VAT, printing, export, Hiboutik, pairing, disaster-recovery, archive, installer or legacy emergency-model code was added.

The real Home Device A run above supplied historical OneDrive transport evidence, but it also demonstrated that the local observer is a false negative for a remotely-visible regular file (`NO_STATES`). That historical local-only gate remains blocked and is superseded for normal handoff by the approved GitHub transport amendment. Earlier CI runs remain historical; the latest pushed correction/evidence head and its CI result are recorded in the branch/PR evidence below.

The amended preparation mapping is: AC-STO-002 (N-device target-directed single writer), AC-STO-003 (close and handoff ordering), AC-STO-004 (target validation/acquisition), AC-STO-005 (no silent takeover), AC-STO-007 (target-directed retention), AC-STO-008 (transport/checkpoint assumptions), AC-STO-009 (generation invalidation), and AC-STO-010 (read-only authority boundary). These remain owner-milestone criteria and are not marked Passed by M02 revalidation; the report is preparation evidence for their M07 owner milestone.

## Final gate

The original competitive OneDrive design remains historically `BLOCKED`, and the real OneDrive local-acknowledgement observation remains preserved. The approved GitHub transport implementation and automated evidence are conforming, and the real private-repository A → B v1 / B → A v2 round trip is now recorded above. The only remaining operator evidence is a destructive live retention observation; until it is recorded (or explicitly waived), the current conclusion is:

`PARTIAL — real A → B v1 and B → A v2 evidence complete; live retention observation remains required`

M03 has not started. No specification was weakened and no sensitive or real business data was added.
