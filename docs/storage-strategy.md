# Storage strategy

**Status:** Approved — Phase 3 baseline  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define how live data, local recovery snapshots, OneDrive handoff snapshots, disaster-recovery checkpoints and annual archives are stored and transferred safely across paired Windows devices without silent divergence.

## 1. Core storage principle

Each paired Sushi81 POS computer uses its own **local working SQLite database**.

The live database is never directly opened from a OneDrive-synchronized folder and is never intentionally written by more than one device at the same time.

OneDrive is used only for controlled transfer of validated complete snapshots and retained recovery/archive artifacts.

The architecture supports an arbitrary number of paired devices. The initial deployment may use two computers, but no protocol or data structure may assume exactly two.

At any moment:

- at most one paired device is authoritative/writable;
- every other paired device is non-authoritative/read-only;
- write authority moves only through a completed formal handoff or explicit disaster recovery;
- V1 performs no automatic row-level database merge.

The operator does not manually manage `live.db` or local recovery files.

## 2. Application-managed local storage

Local business/technical data uses:

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

- `Data\live.db` — active local working database;
- `Recovery\` — rolling validated local recovery snapshots;
- `Cache\` — disposable local caches, including hydrated read-only archives;
- `Logs\` — technical diagnostics;
- `Config\` — local configuration/device identity;
- `Temp\` — safe staging for snapshot/archive/export-related file operations where needed.

The operator is not offered an ordinary setting to relocate the live database or local Recovery directory.

Application binaries/install files remain separate. Update/reinstall must not treat business data or device identity as disposable program content.

## 3. Local recovery snapshots

Important durable business saves trigger local recovery protection, including typically:

- confirming a new order;
- saving an order modification;
- changing CB/Espèce amounts;
- closing/cancelling an order;
- saving catalogue changes;
- successful catalogue batch import;
- saving business settings.

A short technical debounce may coalesce several saves occurring within a few seconds.

The application retains the latest **five** successfully generated and validated local recovery snapshots.

An older valid snapshot may be removed only after a newer valid replacement exists.

These snapshots are application-managed technical data, not an operator-facing business history.

## 4. Formal handoff — normal exit releases authority

A formal handoff snapshot is the only normal mechanism by which another paired device receives authority to continue editing the same business lineage.

Normal application exit triggers handoff automatically.

When the operator closes Sushi81 POS, the application enters a closing/handoff state, blocks new business edits and:

1. completes/commits accepted application/database writes;
2. generates a complete SQLite snapshot using a SQLite-safe mechanism;
3. validates SQLite integrity locally;
4. assigns the next immutable monotonically advancing handoff version;
5. computes a checksum/hash;
6. associates lineage/generation/source-device metadata;
7. publishes the snapshot into the configured OneDrive `Handoff` area;
8. waits until required synchronization status confirms no pending upload/error;
9. publishes a small matching completion/ready marker containing at least lineage, generation, handoff version and checksum;
10. waits until that marker is also confirmed synchronized;
11. only then records/reports a successful release and completes normal exit.

If any required step fails, the application must not report a successful handoff.

Normal operator behavior is therefore:

**Close POS -> obtain successful handoff confirmation -> leave.**

If the POS was not closed successfully, another device must not assume authority from an older version.

## 5. Immutable handoff versions and retention

Formal handoffs are immutable versioned snapshot+ready-marker units, conceptually:

```text
handoff-v000157.db
handoff-v000157.ready
handoff-v000158.db
handoff-v000158.ready
```

Exact filenames are technical details, but semantics are fixed:

- a snapshot without the matching completed ready marker is not a released handoff;
- marker and database must agree on lineage/generation/version/checksum;
- the pair is one retention unit;
- latest **five** complete validated handoff versions are retained;
- cleanup of an older handoff happens only after a newer replacement is validated and, where required, confirmed synchronized.

## 6. OneDrive shared root

The operator selects one locally available folder inside the OneDrive tree as the Sushi81 POS shared root.

The application creates/manages:

```text
<Selected OneDrive root>\
    Handoff\
    DisasterRecovery\
    Archive\
    System\
```

Semantics:

- `Handoff\` — completed formal handoff versions/markers;
- `DisasterRecovery\` — recovery-only checkpoints;
- `Archive\` — permanent annual archive databases;
- `System\` — small lineage/device/coordination metadata required by the protocol.

The selected root is never the live database location.

Changing the shared root occurs only through application settings. A proposed new root must be validated before adoption and must not silently join/combine an unrelated lineage.

## 7. Device and lineage identity

The protocol uses opaque identities rather than fixed roles such as SHOP/HOME.

### 7.1 `device_id`

Each installation receives an immutable random UUID-like `device_id` when initialized.

It is stored in local application configuration and is not ordinary user-editable business data.

A separate human-readable device display name may be editable without changing `device_id`.

If a complete Windows reinstall loses the local identity, that installation is treated as a new device and receives a new ID.

### 7.2 `lineage_id`

The first authoritative business database establishes a stable opaque `lineage_id` identifying that business-data family.

All valid live/handoff/disaster-recovery metadata belonging to it carries the same lineage.

A device encountering another lineage must not silently merge/adopt it.

### 7.3 `generation`

A lineage has a generation/epoch.

Normal handoffs preserve generation. Confirmed disaster recovery advances it so devices holding pre-recovery state cannot later resume writing silently.

### 7.4 `handoff_version`

Formal handoffs advance a monotonic handoff version within the current lineage history.

Protocol metadata must identify at least:

- lineage ID;
- generation;
- handoff version;
- relevant source/current device ID;
- checksum;
- required timestamps.

## 8. Pairing additional devices

The design contains no two-device slots or fixed logical maximum.

### 8.1 First device

On initial setup:

- application creates local live/recovery areas;
- operator selects/creates the OneDrive shared root;
- application establishes device/lineage metadata;
- subsequent formal handoffs use that root.

### 8.2 Second, third or later device

To add another computer:

1. install Sushi81 POS normally;
2. create its new `device_id`;
3. select the same existing OneDrive shared root;
4. validate lineage/generation/shared structure;
5. locate latest formally completed handoff;
6. verify snapshot + ready marker + checksum + SQLite integrity;
7. restore the validated version into that device's own local `live.db`;
8. join the existing lineage.

If another device still holds authority, the new device remains read-only.

If a released handoff is safely available, authority may be acquired according to the protocol.

Competing acquisition attempts must never silently activate multiple writers. Write activation remains blocked until one authoritative acquisition is established.

## 9. Receiving/acquiring a completed handoff

Before a non-authoritative device enters write mode it must:

1. identify the latest formally completed handoff;
2. ensure snapshot and matching ready marker are locally available;
3. verify lineage/generation/version/checksum consistency;
4. validate SQLite integrity;
5. compare with local version/generation;
6. safely restore the accepted snapshot to local `live.db` when required;
7. establish exclusive authority for this device;
8. only then enable writes.

A partial snapshot, missing marker, checksum mismatch, lineage/generation mismatch, integrity failure or unresolved competing acquisition must never become a writable live database automatically.

## 10. No normal force takeover

If the current authoritative device did not complete a formal handoff, another device must not begin writing from an older local/OneDrive version through an ordinary force-takeover path.

If the authoritative device remains available, the normal remedy is to return to it and complete the handoff.

Reliability and prevention of silent divergence take precedence over convenience.

## 11. Disaster recovery

Disaster Recovery is reserved for genuine abnormal loss of the authoritative device, such as unrecoverable hardware/disk/OS failure preventing normal handoff.

It is separate from normal handoff.

### 11.1 Recovery-only cloud checkpoints

While the authoritative device operates normally:

- local recovery continues;
- if durable business data changed since the previous cloud checkpoint, a validated recovery-only checkpoint may be published to `DisasterRecovery`;
- maximum normal publication frequency is **one checkpoint every 15 minutes**;
- if no durable data changed, no redundant checkpoint is required;
- publication must not unnecessarily block ordinary POS work like a formal handoff;
- latest **five** successfully published and validated recovery checkpoints are retained.

These checkpoints do **not** release write authority and are never consumed automatically as ordinary handoffs.

### 11.2 Recovery user flow

If no valid latest formal handoff exists, ordinary acquisition stays blocked.

A separate Disaster Recovery action displays at least:

- latest completed handoff timestamp/version;
- newest validated recovery checkpoint timestamp;
- checkpoint source device;
- warning that changes after the checkpoint may be lost.

After explicit confirmation, the application:

1. validates the selected appropriate checkpoint;
2. restores it to local `live.db`;
3. advances the lineage generation;
4. records the new authoritative generation;
5. enables writes only after activation succeeds.

### 11.3 Old-generation invalidation

A device later returning with an older generation may not resume writes from its stale local database, regardless of file timestamps.

It must be reinitialized from current authoritative released data before write capability is restored.

Merely forgetting to close the POS is not sufficient reason to use Disaster Recovery while the original device remains recoverable.

## 12. Non-authoritative read-only mode

A paired device that has not acquired authority may open Sushi81 POS in clearly marked non-authoritative/read-only mode.

It may consult:

- the most recent formally acquired local live-data copy it already holds;
- completed annual archives.

The UI must clearly and persistently show:

- read-only/non-authoritative state;
- that current live data may be stale;
- the version and/or effective time of the most recently acquired data.

A newer recovery-only checkpoint is not consumed as an ordinary read-only update.

Read-only mode blocks authoritative business writes including:

- creating/modifying orders;
- changing payments;
- closing/cancelling orders;
- catalogue/category/option edits;
- catalogue imports;
- business-setting changes;
- annual archive execution;
- formal handoff publication from the stale copy.

### 12.1 Printing from non-authoritative data — Phase 4 alignment

The later Approved printing decision is final for V1: a non-authoritative/read-only device **may print/reprint** from the committed live-data copy currently available on that device.

It is not hard-blocked, but before/at the print action the application must clearly indicate that:

- this device is non-authoritative;
- displayed live data may be stale;
- freshness has not been verified.

Printing performs no business write, does not transfer authority and does not claim synchronization success.

Detailed marking/printing behavior remains in `printing.md` and `docs/decisions/non-authoritative-device-printing.md`.

## 13. Offline behavior

The authoritative device may continue normal local operation during temporary Internet/OneDrive loss because `live.db` is local.

It cannot complete a successful formal handoff until required OneDrive publication/synchronization can be confirmed.

Cloud disaster-recovery checkpoints may remain pending/unavailable while offline; local work remains authoritative until valid handoff or explicit disaster recovery.

A non-authoritative device that cannot verify/acquire the latest handoff cannot enter write mode from an older copy, though read-only use and approved printing remain available.

## 14. Annual archive

Annual archives are separate historical SQLite files stored in OneDrive `Archive`, independent from installed binaries and active local `live.db`.

They are read-only historical databases and not part of normal live handoff lineage.

### 14.1 Schedule and strict calendar-year boundary

The authoritative device automatically processes the previous **complete calendar year** on **February 1** each year.

The trigger date never extends the target window into January of the current year.

Example: the February 1, 2027 archive processes only archive-year 2026 records ending no later than December 31, 2026.

If the POS is not run on February 1, the archive occurs on the first later startup on which the authoritative device can safely perform it. Delayed execution does not enlarge the target year.

Only the authoritative device may create an annual archive.

### 14.2 Archive-year rules

Archive year is the natural year in which the business order ends:

- `CLOSED` order -> year of `closed_at`;
- `CANCELLED` order -> year of `cancelled_at`;
- `OPEN` order -> remains in `live.db` regardless of age.

This rule applies equally to ordinary `POS` and hidden-source `HIBOUTIK_PASTE` orders. The source discriminator affects ordinary POS financial/export inclusion; it does not create different archive-year semantics.

Example: an order created December 2026 and Closed January 10, 2027 belongs to archive year 2027, stays live through the February 2027 archive and becomes eligible when archive year 2027 is processed in February 2028.

### 14.3 Publication safety

Archive creation is failure-safe:

1. identify target-year eligible records;
2. build archive in safe local staging;
3. validate archive/expected records;
4. publish to OneDrive `Archive`;
5. confirm successful publication/synchronization;
6. only then remove those records from `live.db`;
7. create a new local recovery point; later formal handoff contains the post-archive live state.

If creation, validation or publication fails, records remain live and archiving is retried later.

### 14.4 Archive independence and retention

Completed annual archives:

- survive application uninstall/reinstall;
- are outside install/local-live-data directories as their authoritative copy;
- are shared through OneDrive;
- are immutable/read-only in normal POS use;
- remain queryable/reprintable;
- may be hydrated transparently to a local read-only cache;
- are retained permanently unless the operator deliberately manages/removes them outside normal application workflow.

Conceptually:

```text
<Selected OneDrive root>\Archive\
    sushi81-archive-2026.db
    sushi81-archive-2027.db
```

Exact filenames may be refined technically.

## 15. Rolling retention

V1 uses:

- Local Recovery: latest **5** validated snapshots;
- OneDrive Handoff: latest **5** complete validated versions;
- OneDrive Disaster Recovery: latest **5** validated checkpoints;
- Annual Archive: permanent/no rolling deletion.

Cleanup happens after, never before, a newer replacement is safely generated/validated and, where required, synchronized.

These counts are fixed V1 technical defaults rather than ordinary operator-configurable settings.

## 16. Storage invariants

Implementation must preserve all of the following:

1. `live.db` is local/application-managed, never a live OneDrive database.
2. Any number of devices may pair; exactly-two-device assumptions are forbidden.
3. At most one device writes at a time.
4. Normal write transfer requires a validated completed formal handoff.
5. No stale normal force takeover.
6. Disaster Recovery is explicit/recovery-only and creates a new generation.
7. Old-generation devices cannot resume writes without reinitialization.
8. Non-authoritative devices use clearly marked stale/read-only state.
9. Non-authoritative printing is allowed only under the explicit stale-data warning rules already Approved in Phase 4.
10. Local Recovery/Handoff/Disaster Recovery each retain five valid rolling versions.
11. Annual archive targets only the previous complete natural year when triggered on/after February 1.
12. Closed and Cancelled orders use their end timestamp year; Open orders remain live.
13. Archive records leave `live.db` only after validated successful OneDrive archive publication.
14. Annual archives remain independent permanent historical files.

## 17. Approval

This document is the **Approved — Phase 3 baseline**, aligned during Phase 5 with the later Approved Phase 4 printing/source decisions.

The storage architecture is local SQLite per paired device, N-device single-writer authority, validated immutable OneDrive handoff, application-managed local recovery, change-triggered recovery-only cloud checkpoints, explicit generation-changing Disaster Recovery, non-authoritative read-only access, five-version rolling technical protection and independent February natural-year archives.

Low-level filenames, coordination serialization and similar pure implementation details may be selected during implementation only if every invariant above remains true.