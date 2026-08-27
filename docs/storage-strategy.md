# Storage strategy

**Status:** Approved — Phase 3 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define how live data, local recovery snapshots, OneDrive handoff snapshots, disaster-recovery checkpoints and annual archives are stored and transferred safely across any number of paired Windows devices without silent divergence.

## 1. Core storage principle

Each paired Sushi81 POS computer uses its own **local working SQLite database**.

The live database is never directly opened from a OneDrive-synchronized folder and is never intentionally written by more than one device at the same time.

OneDrive is used only for controlled transfer of validated complete snapshots and for retained recovery/archive artifacts.

The architecture supports **an arbitrary number of paired devices by default**. The initial deployment may use two computers, but no storage structure or protocol may assume exactly two devices.

At any moment:

- at most one paired device is authoritative and writable;
- all other paired devices are non-authoritative and may use only approved read-only access;
- write authority moves only through a completed formal handoff or an explicit disaster-recovery action;
- v1 performs no automatic row-level database merge.

The operator does not manually manage `live.db` or local recovery snapshot files.

## 2. Application-managed local storage

Local business and technical data use the fixed per-user application-data root:

```text
%LOCALAPPDATA%\Sushi81 POS\
    Data\
        live.db
    Recovery\
    Cache\
    Logs\
    Config\
    Temp\
```

Semantics:

- `Data\live.db` is the active local working database;
- `Recovery\` contains rolling local recovery snapshots;
- `Cache\` contains disposable local caches, including locally hydrated read-only annual archives where needed;
- `Logs\` contains technical diagnostics;
- `Config\` contains local configuration and device identity;
- `Temp\` is used for safe staging of snapshots/archives before publication.

The operator is not offered an ordinary setting to relocate `live.db` or the local Recovery area.

Application binaries/install files remain separate from this business-data root. Updating or reinstalling the application must not treat the local business-data directory as disposable program content.

## 3. Local recovery snapshots

Important durable business saves trigger local recovery protection, including typically:

- confirming a new order;
- saving an order modification;
- changing CB/Espèce amounts;
- closing or cancelling an order;
- saving catalogue changes;
- successful catalogue batch import;
- saving business settings.

A short debounce/coalescing delay may combine several saves occurring within a few seconds into one local recovery snapshot.

Local recovery snapshots exist primarily for crash/corruption/accidental-change recovery on the current computer.

The application retains the **latest five successfully generated and validated local recovery snapshots**. An older snapshot may be deleted only after a newer valid snapshot has been secured.

These files are application-managed technical data and require no normal user file handling.

## 4. Formal handoff — normal exit equals release

A **formal handoff snapshot** is the only normal mechanism by which another paired device receives authority to continue editing Sushi81 POS business data.

Normal application exit triggers handoff automatically.

When the operator closes Sushi81 POS, the application does not immediately terminate. It enters a closing/handoff state in which new business edits are blocked and performs:

1. complete/commit all accepted application/database writes;
2. generate a complete SQLite snapshot through a SQLite-safe backup mechanism;
3. validate SQLite integrity locally;
4. assign the next immutable monotonically advancing handoff version;
5. compute a checksum/hash;
6. attach required lineage/generation/source-device metadata;
7. publish the snapshot into the configured OneDrive `Handoff` area;
8. monitor synchronization until the snapshot is confirmed no longer pending upload and no sync error is reported;
9. publish a small matching completion/ready marker containing at least the lineage, generation, handoff version and checksum;
10. wait until the ready marker is also confirmed synchronized;
11. only then mark the handoff released/successful, display a clear success confirmation and complete application exit.

If any required step fails, the application must not report a successful handoff.

Normal user workflow:

**Close POS -> wait for successful handoff confirmation -> leave.**

Forgetting to close POS means accepting that formal handoff did not occur; the normal remedy is to return to the current authoritative device and complete the close/handoff.

## 5. Immutable handoff versions and retention

Formal handoff snapshots are immutable versioned files rather than repeated in-place overwrites of one `latest.db`.

Conceptually:

```text
handoff-v000157.db
handoff-v000157.ready
handoff-v000158.db
handoff-v000158.ready
```

Exact filenames are implementation details, but semantics are fixed:

- a snapshot without its matching completed ready marker is not a released/accepted handoff;
- the marker references the exact expected lineage/generation/version/checksum;
- the database and ready marker are one retention/deletion unit;
- the application retains the **latest five complete validated handoff versions**;
- cleanup of an older handoff occurs only after a newer handoff has been generated, validated and confirmed synchronized.

## 6. OneDrive shared root and fixed subareas

The operator selects one folder inside the locally available OneDrive tree as the Sushi81 POS shared root.

The application creates/manages the logical subareas beneath it:

```text
<Selected OneDrive root>\
    Handoff\
    DisasterRecovery\
    Archive\
    System\
```

Semantics:

- `Handoff\` contains completed formal handoff versions and markers;
- `DisasterRecovery\` contains recovery-only cloud checkpoints;
- `Archive\` contains permanent annual archive databases;
- `System\` contains small lineage/device/coordination metadata required by the protocol.

The selected OneDrive root is never used as the location of `live.db`.

Users may change the configured shared root only through application settings. The application must validate a new root before adoption and must not silently create or join an unrelated data lineage.

## 7. Device and lineage identity — approved Phase 3 design

The synchronization/storage design uses identities, not fixed computer roles such as SHOP/HOME.

### 7.1 `device_id`

Each installation/device receives an opaque immutable random UUID-like `device_id` when it is first initialized.

The device identity is stored in local application configuration and is not ordinary user-editable data.

A separate human-readable device name may be shown/editable, for example `店铺电脑`, `家里电脑`, or another label. Changing the display name must not change `device_id`.

If Windows is fully reinstalled and the local Sushi81 POS identity is lost, that installation is treated as a new device and receives a new `device_id`.

### 7.2 `lineage_id`

The first authoritative Sushi81 POS business database creates one stable opaque `lineage_id` identifying the business-data family.

All valid live/handoff/disaster-recovery metadata belonging to that business dataset carries the same `lineage_id`.

A device attempting to join a OneDrive root with a different lineage must not silently adopt or combine it.

### 7.3 `generation`

Each lineage has a generation/epoch value.

Normal formal handoffs keep the same generation.

A confirmed disaster recovery creates a new generation so that old devices/data from the pre-recovery generation cannot silently resume writing later.

### 7.4 `handoff_version`

Formal handoffs advance a monotonically increasing handoff version within the authoritative lineage history.

Technical metadata must therefore be sufficient to identify at least:

- `lineage_id`;
- `generation`;
- `handoff_version`;
- source/current `device_id` where applicable;
- snapshot checksum and timestamps where required.

## 8. Pairing and adding any number of devices

The design has no fixed two-device slots and no logical maximum tied to the original shop/home use case.

### 8.1 First device

On first setup:

- the application creates/manages its local `live.db` and local recovery area;
- the operator selects/creates the OneDrive shared root;
- the application establishes the lineage metadata and device identity;
- subsequent formal handoffs use that shared root automatically.

### 8.2 Additional device — second, third or later

To add any later computer:

1. install Sushi81 POS normally;
2. the installation creates a new unique `device_id`;
3. in settings, select the same existing OneDrive shared root;
4. validate the existing `lineage_id`, generation and shared structure;
5. locate the latest formally completed handoff;
6. verify snapshot + ready marker + checksum + SQLite integrity;
7. restore the validated version into the new device's own local application-managed `live.db`;
8. join the same lineage without changing the architecture or migrating the database format.

If write authority is still held by another device, the newly paired device may only use non-authoritative read-only mode.

If a released handoff is safely available and the new/other device acquires authority according to the handoff protocol, that device becomes the sole writer until it releases through its next formal handoff.

Shared state and application code must use collections/device IDs rather than fixed fields for exactly two computers.

If competing acquisition attempts for the same released handoff are detected, the application must not silently allow multiple writers; it must block write activation until one authoritative acquisition is safely established. Exact low-level coordination representation is an implementation detail, but silent multi-writer acquisition is forbidden.

## 9. Receiving/acquiring a completed handoff

Before any non-authoritative paired device may enter write/edit mode, it must establish that a valid released handoff exists.

Acquisition sequence:

1. identify the latest formally completed handoff available in `Handoff`;
2. ensure snapshot and matching ready marker are locally available;
3. verify lineage/generation/version/checksum consistency;
4. validate SQLite integrity;
5. compare with the device's current local version/generation;
6. safely/atomically restore the accepted handoff into local `live.db` if required;
7. establish that this device is the one authorized writer for the acquired lineage state;
8. only then enable write operations.

A partial snapshot, missing ready marker, checksum mismatch, lineage/generation mismatch or integrity failure must never automatically become `live.db`.

## 10. No normal force takeover

If the current authoritative device did not complete a formal handoff, another device must not begin writing from an older OneDrive version.

There is no ordinary force-takeover button that silently promotes stale data.

If the authoritative device remains available, the normal remedy is to return to it and complete normal close/handoff.

Preventing silent data loss/divergence has priority over convenience.

## 11. Disaster recovery

Disaster recovery is reserved for genuine abnormal loss of the authoritative device, such as unrecoverable hardware/disk/operating-system failure that prevents normal handoff.

It is separate from normal handoff.

### 11.1 OneDrive disaster-recovery checkpoints

While the authoritative device is in normal use:

- local recovery continues normally;
- if durable business data has changed since the previous cloud checkpoint, the application publishes a validated recovery-only checkpoint to `DisasterRecovery`;
- maximum normal publication frequency is **one checkpoint every 15 minutes**;
- if no durable data changed, no redundant checkpoint is required;
- checkpoint publication must not block routine POS work merely to behave like formal handoff;
- the application retains the **latest five successfully published and validated disaster-recovery checkpoints**.

These checkpoints are technically/logically marked **recovery-only**. They do not release write authority and cannot be automatically consumed as a normal handoff.

### 11.2 Recovery user flow

If the latest formal handoff was not completed, normal write acquisition remains blocked.

A separate Disaster Recovery action may be used only when the operator knows the authoritative device genuinely cannot complete handoff.

Before confirmation the application shows at least:

- latest completed formal handoff timestamp/version;
- newest validated disaster-recovery checkpoint timestamp;
- checkpoint source device;
- clear warning that changes after the checkpoint time may be lost.

After explicit confirmation the application:

1. validates the selected newest appropriate recovery checkpoint;
2. restores it into the recovering device's local `live.db`;
3. advances the lineage generation/epoch;
4. records the recovery as establishing the new authoritative generation;
5. allows writes only after recovery activation succeeds.

### 11.3 Old-device invalidation

Any device returning later with an older generation must not resume writing from its stale local database, regardless of ordinary file timestamps.

It must be reinitialized from current authoritative released data before regaining write capability.

Merely forgetting to close POS is not by itself a reason to use Disaster Recovery if the original device remains available.

## 12. Non-authoritative read-only mode

Any paired device that has not acquired the latest completed formal handoff may still open Sushi81 POS in a non-authoritative read-only mode.

It may consult the most recent formally acquired local business database already present on that device and completed annual archives.

The UI must clearly and persistently show:

- read-only / not current status;
- that displayed live data may be stale;
- the version and/or effective time of the most recently formally acquired data.

The device must not use a newer Disaster Recovery checkpoint as an ordinary read-only update.

Read-only mode blocks every operation that changes authoritative business state, including at least:

- creating/modifying orders;
- changing payments;
- closing/cancelling orders;
- editing catalogue/categories/options;
- catalogue imports;
- business-setting changes;
- annual archive execution;
- publishing formal handoff from the stale copy.

Whether printing/reprinting from a non-authoritative current-data copy is allowed is deferred to `printing.md` because stale printed operational output can create business risk even though printing does not modify the database.

## 13. Offline behavior

The current authoritative device may continue normal local operation during temporary Internet/OneDrive loss because `live.db` is local.

It cannot complete a successful formal handoff until required OneDrive synchronization can be confirmed.

Cloud disaster-recovery checkpoints may also remain unavailable/pending during prolonged OneDrive loss; local work on the current authoritative device remains authoritative until valid handoff or explicit disaster recovery.

A non-authoritative device that cannot verify/acquire the latest completed handoff may not enter write mode from an older local copy, although approved read-only access remains available.

## 14. Annual archive

Annual archives are separate historical SQLite database files stored in OneDrive `Archive` and independent from the installed application and active local `live.db`.

They are read-only historical databases, not part of normal handoff lineage.

### 14.1 Schedule and strict natural-year boundary

The application automatically performs the previous **complete calendar year's** archive on **February 1** each year.

The trigger date never extends the archive window into January of the current year.

Example: the February 1, 2027 run processes only archive-year **2026** records, ending at December 31, 2026. Records assigned to archive year 2027, including records ending in January 2027, remain in `live.db`.

If POS is not run on February 1, the archive occurs on the first later startup when the current authoritative/writable device can safely perform it. Delayed execution does not enlarge the target period.

Only the current authoritative device may create an annual archive.

### 14.2 Archive-year rules

Archive year is the natural year in which the order actually ends:

- ordinary `CLOSED` POS order => year of `closed_at`;
- ordinary `CANCELLED` POS order => year of `cancelled_at`;
- Hiboutik emergency `CLOSED` => year of `closed_at`;
- Hiboutik emergency `CANCELLED` => year of `cancelled_at`;
- any `OPEN` order remains in `live.db` regardless of age.

Example: an order created in December 2026 but closed January 10, 2027 belongs to archive year 2027, remains live during the February 2027 archive run, and becomes eligible during the February 2028 archive of 2027.

### 14.3 Archive publication and safety

Conceptually:

```text
<Selected OneDrive root>\Archive\
    sushi81-archive-2026.db
    sushi81-archive-2027.db
```

Exact filenames may be refined during implementation.

Archive creation is failure-safe:

1. identify target-year eligible records;
2. build the archive in a safe local staging location;
3. validate the archive and expected records;
4. publish to OneDrive `Archive`;
5. confirm successful synchronization/publication;
6. only then remove archived records from `live.db`;
7. generate a new local recovery point; the later normal close/handoff contains the post-archive live state.

If creation, validation or publication fails, records remain in `live.db` and archiving is retried later.

### 14.4 Archive independence and retention

Completed annual archives:

- survive application uninstall/reinstall;
- are outside application install/local-data directories as their authoritative copy;
- are shared through OneDrive;
- are normally immutable/read-only;
- remain queryable and reprintable from Sushi81 POS;
- may be transparently hydrated to a local read-only cache before querying;
- are retained permanently unless the operator deliberately manages/removes them outside normal application workflow.

## 15. Rolling retention

The rolling technical protection sets use one simple fixed v1 rule:

- Local Recovery: latest **5** validated snapshots;
- OneDrive Handoff: latest **5** complete validated versions;
- OneDrive Disaster Recovery: latest **5** validated checkpoints;
- Annual Archive: permanent, no rolling deletion.

Cleanup always occurs after, never before, a newer replacement has been successfully generated and validated. Where synchronization confirmation is part of the artifact's safety contract, cleanup also waits for the newer replacement to be confirmed synchronized.

These counts are fixed v1 defaults rather than ordinary operator-configurable settings.

## 16. Storage invariants for implementation

Codex/implementation must preserve all of the following:

1. `live.db` is local and application managed, never a live OneDrive database.
2. Any number of devices may pair with one lineage; the design must not contain exactly-two-device assumptions.
3. At most one paired device may write at a time.
4. Normal write transfer requires a validated completed formal handoff.
5. No stale normal force takeover.
6. Disaster Recovery is explicit, recovery-only and creates a new generation.
7. Old-generation devices cannot resume writes without reinitialization.
8. Non-authoritative devices may consult stale formally acquired data only in clearly marked read-only mode.
9. Local Recovery/Handoff/Disaster Recovery retain five valid rolling versions.
10. Annual archive runs on February 1 for the previous complete natural year only and never consumes current-year January records.
11. Archive records are removed from `live.db` only after validated OneDrive archive publication.
12. Annual archives remain independent permanent historical files.

## 17. Approval

This document is **Approved — Phase 3 baseline**.

The approved storage architecture is local SQLite per device, N-device pairing through a user-selected OneDrive root, single-writer authority, verified immutable formal handoff, application-managed local Recovery, 15-minute change-triggered Disaster Recovery checkpoints, explicit recovery generation renewal, non-authoritative read-only access, five-version rolling technical retention, and independent February natural-year annual archives.

Low-level file naming, exact coordination serialization and similar implementation details may be selected during implementation only if they preserve every invariant in this document.