# M02 OneDrive single-writer feasibility report

**Gate conclusion:** `BLOCKED — specification/architecture amendment required`

This report records the M02 feasibility evidence. It is not a specification amendment and does not authorize M03.

## Scope and provenance

- Branch: `codex/m02-onedrive-feasibility`
- Verified correction code commit: `5f44e34ec4a98b767d01a35846deec75e6d29825`.
- Task contract: `docs/implementation/milestone-02-onedrive-feasibility.md`
- Evidence data: synthetic payloads, local synthetic SQLite files and in-memory transport simulation only; no customer, order, payment, credential, or account data.
- M02 does not activate the POS write authority, relocate `live.db`, or implement business features.

The final commit, SDK/build output, and test totals are filled from the verified build on this correction pass. This avoids claiming evidence from an unbuilt or superseded tree.

## Environment and documented platform surface

The environment was Windows 10.0.26200 x64 with .NET SDK `10.0.400`, host/runtime `10.0.11`, RID `win-x64`; all three M02 Windows projects pin `WindowsSdkPackageVersion=10.0.26100.87`. The required Windows/OneDrive multi-device experiment was not available in this execution environment. No account identifier, personal path, or file listing is recorded. Real OneDrive transport behavior therefore remains unverified.

The harness isolates the platform surface behind a small observation interface. The intended documented Windows APIs for a later operator run are:

- [`StorageProviderSyncRootManager.GetCurrentSyncRoots()`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootmanager.getcurrentsyncroots?view=winrt-26100) and [`StorageProviderSyncRootInfo`](https://learn.microsoft.com/en-us/uwp/api/windows.storage.provider.storageprovidersyncrootinfo?view=winrt-26100) for registered sync-root identity and path containment;
- Cloud Files [`CF_PLACEHOLDER_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_placeholder_state), including `IN_SYNC`, `PARTIAL`, `PARTIALLY_ON_DISK`, and `INVALID`, for exact-file publication observation.

An arbitrary local directory named `OneDrive`, file existence, timestamps, Explorer overlays, registry heuristics, or a local NTFS lock are not treated as cloud synchronization or cross-device exclusion evidence.

The correction pass uses the official `CF_PLACEHOLDER_STATE` values as a single `[Flags]` enum: `NO_STATES=0x00000000`, `PLACEHOLDER=0x00000001`, `SYNC_ROOT=0x00000002`, `ESSENTIAL_PROP_PRESENT=0x00000004`, `IN_SYNC=0x00000008`, `PARTIAL=0x00000010`, `PARTIALLY_ON_DISK=0x00000020`, and `INVALID=0xffffffff`. `IN_SYNC` means the placeholder content is in sync with the cloud; it is not a distributed lock, lease, compare-and-swap, or acknowledgement from another client.

## What a per-file sync state can and cannot prove

An observed documented `IN_SYNC` state may support the narrow claim that the provider reports the local item as synchronized for that item. It is not a distributed lock or remote acknowledgement. It does not, by itself, prove that a second client has downloaded the same immutable bytes, that a remote client can independently observe current state, or that no competing acquisition claim exists. Unknown, pending, partial, invalid, unavailable, and API-error states remain blocked and cannot activate writable authority. OneDrive conflict behavior is separately documented by [Microsoft Support](https://support.microsoft.com/office/64883a5d-228e-48f5-b3d2-eb39e07630fa).

## Deterministic evidence

The solution includes three non-shipping M02 projects: `tools/Sushi81.Pos.OneDriveFeasibility/` (observation, synthetic SQLite publication/validation and claims), `tools/Sushi81.Pos.OneDriveFeasibility.Tests/` (25 harness test cases, including fourteen documented-state rows plus an exact-enum regression), and `tests/Sushi81.Pos.OneDriveFeasibility.Tests/` (13 protocol tests). The inventory covers:

- strict protocol-version and required-field parsing;
- lineage, generation, handoff-version, source-device, checksum, length, and marker matching;
- valid pair acceptance and rejection of missing, malformed, stale, mismatched, corrupt, or integrity-failing units;
- snapshot synchronization before ready-marker publication, timeout/error blocking, and immutable retry behavior;
- one-device acquisition, near-simultaneous two-device acquisition, three-device observation, delayed/reordered/duplicate claims, participant disappearance, stale claimants, and unknown transport state;
- deterministic repeated evaluation and the safety assertion that unresolved contention never yields writable authority.

The publication path creates a closed synthetic SQLite database, runs `PRAGMA integrity_check`, computes SHA-256/length, promotes an immutable read-only snapshot, waits for confirmed `IN_SYNC`, then creates a matching read-only marker and waits for its state. Receiver validation requires both files, strict metadata, lineage/generation/version, size, checksum and SQLite integrity. Release verification totals are:

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 3 | 0 | 0 |
| Application | 2 | 0 | 0 |
| Infrastructure integration | 16 | 0 | 0 |
| Architecture/localization | 8 | 0 | 0 |
| M02 protocol simulator | 13 | 0 | 0 |
| M02 Windows/SQLite harness | 25 | 0 | 0 |
| **Total** | **67** | **0** | **0** |

The test inventory covers strict metadata and required-field parsing; valid/missing/malformed/unsupported handoff units; checksum, size, lineage, generation and version mismatch; SQLite integrity failure; snapshot-before-marker ordering; snapshot/marker timeout; local/non-cloud and pending/partial/invalid/unknown state; one-, two- and three-device contention; delayed/reordered/replayed claims; dropout; stale claimants; deterministic repeated evaluation; and the no-double-writer safety assertion. They are reproducible with:

```powershell
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
```

All three M02 projects are included in `Sushi81.Pos.sln`, so the standard solution commands cover them.

## Real experiment matrix

### One device

On this host, `roots --json` found zero registered sync roots; `validate-root` rejected a temporary local directory, and observing an ordinary synthetic local file returned `NotCloudPlaceholder` with `isConfirmedInSync=false`. These negative checks show that arbitrary local paths/files are not accepted as OneDrive evidence. A real provider transition, offline/paused state and confirmed payload/marker transition remain unverified; they are transport evidence, not mutual-exclusion proof, and are not required to resolve the present blocker.

### Two devices

Not required to establish the present blocker and not available in this environment. No two-device success or contention result is claimed. If an approved amendment supplies a documented external atomic grant, a future operator may validate transport behavior using synthetic files only.

### Three devices

Three-device behavior is covered by deterministic protocol simulation. Three distinct claimants with reordered/partial visibility demonstrate that immutable-file claims alone cannot provide a globally atomic exclusive grant. The tested fail-closed decisions remain `Blocked`; no writable result is claimed. A third real OneDrive device was not available.

## Future transport evidence boundary (after any approved amendment)

If an approved specification amendment supplies and documents an external atomic grant, use the same M02 commit and a path printed by `roots --json`; do not type an arbitrary local folder named `OneDrive`. This procedure is deferred during the current blocked gate. Device A:

```powershell
$env:PATH = (Join-Path $env:USERPROFILE '.dotnet') + ';' + $env:PATH
$root = '<registered-OneDrive-root>'
$lineage = '11111111-1111-1111-1111-111111111111'
$project = 'tools\Sushi81.Pos.OneDriveFeasibility\Sushi81.Pos.OneDriveFeasibility.csproj'
dotnet run --project $project -c Release --no-build -- validate-root $root --json
dotnet run --project $project -c Release --no-build -- publish $root --device device-a --lineage $lineage --generation 1 --version 1 --timeout-seconds 120 --json
$handoff = Join-Path $root 'Sushi81-M02-Synthetic\Handoff'
dotnet run --project $project -c Release --no-build -- inspect $handoff --json
$claims = Join-Path $root 'Sushi81-M02-Synthetic\Claims'
dotnet run --project $project -c Release --no-build -- claim $claims --lineage $lineage --device device-a --generation 1 --version 1 --json
```

Device B, after both files are independently visible:

```powershell
$env:PATH = (Join-Path $env:USERPROFILE '.dotnet') + ';' + $env:PATH
$root = '<the same registered-OneDrive-root>'
$lineage = '11111111-1111-1111-1111-111111111111'
$project = 'tools\Sushi81.Pos.OneDriveFeasibility\Sushi81.Pos.OneDriveFeasibility.csproj'
dotnet run --project $project -c Release --no-build -- validate-root $root --json
$handoff = Join-Path $root 'Sushi81-M02-Synthetic\Handoff'
dotnet run --project $project -c Release --no-build -- inspect $handoff --json
dotnet run --project $project -c Release --no-build -- observe-claims (Join-Path $root 'Sushi81-M02-Synthetic\Claims') --lineage $lineage --generation 1 --version 1 --json
```

Repeat with versions 2 and 3. Then start `claim` on A and B nearly simultaneously for the same released version. Return only pass/blocked codes, technical states and synthetic device IDs; omit account email, personal listings, credentials and tokens. Contention or provider uncertainty must remain blocked; timing luck is not evidence of exclusive authority.

## Failure and safety outcomes

The harness treats all of the following as non-writable: unavailable/offline/paused transport; pending, partial, invalid, unknown, or API-error item state; missing marker or snapshot; checksum, size, SQLite-integrity, lineage, generation, or handoff-version mismatch; malformed/unsupported metadata; stale versions; unresolved competing claims; and a participant disappearing mid-protocol. A formal release requires a complete validated immutable snapshot followed by its matching synchronized ready marker.

The simulation and documented transport surface do not establish a globally atomic cross-client compare-and-swap or external grant primitive. Immutable-file publication and per-file `IN_SYNC` therefore support transport observation and fail-closed handoff validation, but cannot prove the frozen exclusive-acquisition guarantee. Keeping all unresolved claims blocked preserves safety but cannot provide the required normal acquisition semantics. This is a material architecture gap, not a reason to add a silent timing or filename-conflict workaround.

## Acquisition conclusion and safety reasoning

No frozen-compatible acquisition protocol was found that avoids an external/documented atomic exclusive grant. The deterministic simulator enumerates all six legal two-device `publish -> deliver` schedules and all ninety corresponding three-device schedules, evaluating after every intermediate operation. In a schedule where each device has published a claim but has only received its own claim, the claim-only protocol makes both devices writable; this is an executable double-writer counterexample. A fail-closed protocol cannot distinguish that execution from one in which the other claim is merely delayed, so it must refuse writable activation for every unresolved N-device view. That preserves the `at most one writer` safety invariant but fails the frozen requirement that an arbitrary paired device can normally acquire a released handoff. Therefore a timing wait, quiet period, local lock, conflict filename, or finite real-device experiment cannot supply the missing all-schedules safety proof.

## Acceptance-criteria preparation

`AC-STO-002` through `AC-STO-005` and `AC-STO-007` through `AC-STO-010` remain owned by M06/M07/M08 and are not marked Passed. M02 provides transport observation, deterministic validation, and the blocker finding only. They cannot be closed under the current approved OneDrive/local-filesystem model.

## Final gate

`BLOCKED — specification/architecture amendment required`

The minimal blocker is the absence of a documented cross-client atomic exclusive-grant primitive in the approved OneDrive/local-filesystem model. A specification amendment must define and approve such a primitive or change handoff/acquisition semantics before M03 can start. No architecture workaround, backend, Graph/OAuth integration, business feature, or production handoff workflow was added.
