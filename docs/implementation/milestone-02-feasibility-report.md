# M02 OneDrive single-writer feasibility report

**Gate conclusion:** `PARTIAL — real multi-device evidence still required`

This report records the M02 feasibility evidence. It is not a specification amendment and does not authorize M03.

## Scope and provenance

- Branch: `codex/m02-onedrive-feasibility`
- Task contract: `docs/implementation/milestone-02-onedrive-feasibility.md`
- Evidence data: synthetic payloads, local synthetic SQLite files and in-memory transport simulation only; no customer, order, payment, credential, or account data.
- M02 does not activate the POS write authority, relocate `live.db`, or implement business features.

The final commit, SDK/build output, and test totals are filled from the verified build immediately before the M02 pull request is opened. This avoids claiming evidence from an unbuilt or superseded tree.

## Environment and documented platform surface

The environment was Windows 10.0.26200 x64 with .NET SDK `10.0.400`, host/runtime `10.0.11`, RID `win-x64`; all three M02 Windows projects pin `WindowsSdkPackageVersion=10.0.26100.87`. The required Windows/OneDrive multi-device experiment was not available in this execution environment. No account identifier, personal path, or file listing is recorded. Real OneDrive transport behavior therefore remains unverified.

The harness isolates the platform surface behind a small observation interface. The intended documented Windows APIs for a later operator run are:

- `StorageProviderSyncRootManager.GetCurrentSyncRoots()` and `StorageProviderSyncRootInfo` for registered sync-root identity and path containment;
- Cloud Files placeholder state (`CF_PLACEHOLDER_STATE`, including `IN_SYNC`, `PARTIAL`, `PARTIALLY_ON_DISK`, and `INVALID`) for exact-file publication observation.

An arbitrary local directory named `OneDrive`, file existence, timestamps, Explorer overlays, registry heuristics, or a local NTFS lock are not treated as cloud synchronization or cross-device exclusion evidence.

## What a per-file sync state can and cannot prove

An observed documented `IN_SYNC` state may support the narrow claim that the provider reports the local item as synchronized for that item. It does not, by itself, prove that a second client has downloaded the same immutable bytes, that a remote client can independently observe current state, or that no competing acquisition claim exists. Unknown, pending, partial, invalid, unavailable, and API-error states remain blocked and cannot activate writable authority.

## Deterministic evidence

The solution includes three non-shipping M02 projects: `tools/Sushi81.Pos.OneDriveFeasibility/` (observation, synthetic SQLite publication/validation and claims), `tools/Sushi81.Pos.OneDriveFeasibility.Tests/` (17 harness test cases, including six state rows), and `tests/Sushi81.Pos.OneDriveFeasibility.Tests/` (11 protocol tests). The inventory covers:

- strict protocol-version and required-field parsing;
- lineage, generation, handoff-version, source-device, checksum, length, and marker matching;
- valid pair acceptance and rejection of missing, malformed, stale, mismatched, corrupt, or integrity-failing units;
- snapshot synchronization before ready-marker publication, timeout/error blocking, and immutable retry behavior;
- one-device acquisition, near-simultaneous two-device acquisition, three-device observation, delayed/reordered/duplicate claims, participant disappearance, stale claimants, and unknown transport state;
- deterministic repeated evaluation and the safety assertion that unresolved contention never yields writable authority.

The publication path creates a closed synthetic SQLite database, runs `PRAGMA integrity_check`, computes SHA-256/length, promotes an immutable read-only snapshot, waits for confirmed `IN_SYNC`, then creates a matching read-only marker and waits for its state. Receiver validation requires both files, strict metadata, lineage/generation/version, size, checksum and SQLite integrity. Release totals are:

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 3 | 0 | 0 |
| Application | 2 | 0 | 0 |
| Infrastructure integration | 16 | 0 | 0 |
| Architecture/localization | 8 | 0 | 0 |
| M02 protocol simulator | 11 | 0 | 0 |
| M02 Windows/SQLite harness | 17 | 0 | 0 |
| **Total** | **57** | **0** | **0** |

The test inventory covers strict metadata and required-field parsing; valid/missing/malformed/unsupported handoff units; checksum, size, lineage, generation and version mismatch; SQLite integrity failure; snapshot-before-marker ordering; snapshot/marker timeout; local/non-cloud and pending/partial/invalid/unknown state; one-, two- and three-device contention; delayed/reordered/replayed claims; dropout; stale claimants; deterministic repeated evaluation; and the no-double-writer safety assertion. They are reproducible with:

```powershell
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
```

All three M02 projects are included in `Sushi81.Pos.sln`, so the standard solution commands cover them.

## Real experiment matrix

### One device

The documented sync-root API returned `0` registered roots. A temporary local root was rejected and a synthetic ordinary local file returned `NotCloudPlaceholder` with `isConfirmedInSync=false`. These negative checks confirm that arbitrary local directories/files do not pass as OneDrive evidence. Provider transitions, offline/paused states and a confirmed payload/marker transition remain unobserved.

### Two devices

Not available in this environment. No two-device success or contention result is claimed. The operator must run the Device A/Device B commands supplied by the harness, validate both immutable files independently, and repeat monotonic versions plus delayed/paused acquisition attempts.

### Three devices

Three-device behavior is covered by deterministic protocol simulation. Three distinct claimants with reordered/partial visibility produced an executable claim-only double-writer counterexample while all fail-closed decisions were `Blocked`; no simulated execution produced two writable results under the tested fail-closed protocol. A third real OneDrive device was not available.

## Device A/B procedure (operator follow-up)

On both devices, use the same M02 commit and a path printed by `roots --json`; do not type an arbitrary local folder named `OneDrive`. Device A:

```powershell
$env:PATH = 'C:\Users\zshu\.dotnet;' + $env:PATH
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
$env:PATH = 'C:\Users\zshu\.dotnet;' + $env:PATH
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

The simulation does not establish a globally atomic cross-client compare-and-swap primitive. Consequently, it is evidence for fail-closed interpretation and protocol behavior, not proof that the approved OneDrive transport supplies exclusive acquisition in the real world.

## Acceptance-criteria preparation

`AC-STO-002` through `AC-STO-005` and `AC-STO-007` through `AC-STO-010` remain owned by M06/M07/M08 and are not marked Passed. M02 provides feasibility preparation and deterministic safety evidence only. Real multi-device transport evidence is still required before the gate can become `FEASIBLE — evidence sufficient`.

## Final gate

`PARTIAL — real multi-device evidence still required`

M03 must not start from this result. No architecture workaround, backend, Graph/OAuth integration, business feature, or production handoff workflow was added.
