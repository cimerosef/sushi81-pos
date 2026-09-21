# Storage strategy

**Status:** Approved — Phase 3 baseline, amended through 2026-09-21  
**Last updated:** 2026-09-21  
**Product:** Sushi81 POS  
**Purpose:** Define how live data, local recovery snapshots, GitHub handoff snapshots, disaster-recovery checkpoints and annual archives are stored and transferred safely across paired Windows devices without silent divergence.

**Approved amendments:** `docs/decisions/target-directed-authority-handoff.md` supersedes generic competitive handoff acquisition and automatic authority release on every normal application exit. `docs/decisions/github-handoff-transport.md` supersedes OneDrive desktop publication/synchronization as the normal handoff transport and acknowledgement path. `docs/decisions/m12-local-archive-and-user-selected-export.md` moves canonical annual archives to application-managed local storage and removes OneDrive from annual-archive publication/access while preserving OneDrive Disaster Recovery.

## 1. Core storage principle

Each paired Sushi81 POS computer uses its own **local working SQLite database**.

The live database is never directly opened from a OneDrive-synchronized folder and is never intentionally written by more than one device at the same time.

GitHub Release Assets in the configured dedicated private handoff repository are used for controlled transfer of validated complete snapshots and target-bound grants. They are not a distributed lock or live database engine. OneDrive is used only for separately approved Disaster Recovery artifacts and historical diagnostics. Annual archives are local application-managed business data.

The architecture supports an arbitrary number of paired devices. The initial deployment may use two computers, but no protocol or data structure may assume exactly two.

At any moment:

- at most one paired device is authoritative/writable;
- every other paired device is non-authoritative/read-only;
- normal write authority moves only by a completed **target-directed** formal handoff from the current authoritative device;
- explicit Disaster Recovery is the abnormal recovery path;
- V1 performs no automatic row-level database merge.

The operator does not manually manage `live.db` or local recovery files.

## 2. Application-managed local storage

Local business/technical data uses:

```text
%LOCALAPPDATA%\Sushi81 POS\
    Data\
        live.db
    Recovery\
    Archive\
    Cache\
    Logs\
    Config\
    Temp\
```

Semantics:

- `Data\live.db` — active local working database;
- `Recovery\` — rolling validated local recovery snapshots;
- `Archive\` — permanent application-managed local annual archive databases; not disposable cache content;
- `Cache\` — disposable local caches only;
- `Logs\` — technical diagnostics;
- `Config\` — local configuration/device identity and durable local authority/transfer state;
- `Temp\` — safe staging for snapshot/archive/export-related file operations where needed.

The operator is not offered an ordinary setting to relocate the live database, local Recovery directory or canonical local Archive directory. A separate explicit archive-export action may copy a completed validated archive to an operator-selected destination.

Application binaries/install files remain separate. Update/reinstall must not treat business data, device identity or durable local authority/transfer state as disposable program content.

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

## 4. Closing the application and normal authority transfer

A normal application close no longer automatically releases authority.

When the current device is authoritative, the close flow distinguishes two explicit intents.

### 4.1 Close and retain authority

**Close and retain authority**:

- closes the application without creating a formal authority release;
- leaves this same device authoritative for the lineage;
- allows this same device to continue writable operation on its next valid launch;
- leaves every other paired device non-authoritative/read-only;
- is the safe/default close meaning when no transfer is explicitly requested.

No other device may infer authority merely because the authoritative application process is not currently running.

### 4.2 Transfer authority and close

**Transfer authority and close** transfers normal write authority to exactly one eligible paired target device.

- With one eligible other paired device, the UI may preselect that device, but transfer remains an explicit operator action.
- With more than one eligible target, the operator selects one by human-readable device name.
- The current authoritative device itself is not a valid target.
- The target must belong to the current paired-device set for the current lineage/generation.

A formal target-directed handoff snapshot is the only normal mechanism by which that selected target receives authority to continue editing the same business lineage.

## 5. Target-directed formal handoff protocol

When the operator explicitly chooses **Transfer authority and close**, the source authoritative device enters a handoff state, blocks new business edits and performs the following sequence:

1. complete/commit all accepted application/database writes;
2. generate a complete SQLite snapshot using a SQLite-safe mechanism;
3. validate SQLite integrity locally;
4. assign the next immutable monotonically advancing handoff version;
5. compute the snapshot checksum/hash and any required size metadata;
6. bind immutable protocol metadata to the current lineage, generation, source `device_id` and exactly one target `device_id`;
7. publish the immutable `YYYYMMDDHHMMSS.snapshot.db` to the configured private GitHub Release Asset container;
8. require HTTP 201 plus uploaded state, exact name/size, asset ID and usable `sha256:<hex>` digest matching the local hash; missing/contradictory server evidence fails closed;
9. **durably persist local relinquishment/pending-transfer state** containing at least lineage, generation, handoff version, source device, target device and checksum;
10. from the durable relinquishment point onward, block all business-authoritative writes on the source across restart;
11. only after step 9 succeeds, create/upload the matching immutable `YYYYMMDDHHMMSS.grant.json` target-bound grant and validate its GitHub receipt;
12. only after the grant receipt succeeds, persist Released and run post-completion retention cleanup;
13. only then record/report successful normal authority transfer and complete the transfer-and-close flow.

The target-releasing ready/grant marker must never exist before the source has durably crossed the local relinquishment point.

A snapshot without the matching completed target-bound ready/grant marker is not a released handoff for the target.

GitHub server receipt is the normal handoff publication acknowledgement. OneDrive synchronization state is not consulted by this authority gate and is not a distributed mutex or cross-client compare-and-swap.

### 5.1 Failure before relinquishment

Before the durable relinquishment point, a transfer may fail or be cancelled without releasing authority, provided no target-bound ready/grant marker has been published.

The source may remain authoritative and writable after the transfer is safely aborted.

### 5.2 Failure after relinquishment

After the durable relinquishment point:

- the source may not roll back to ordinary writable authority;
- source restart remains read-only/pending-transfer;
- the source may perform technical retries needed to finish publication/synchronization of the **same immutable target-bound transfer**;
- the source may not choose a different target for that handoff version;
- the source may not create new business writes while retrying;
- if the designated target cannot be recovered and the handoff cannot be completed, the exceptional path is explicit Disaster Recovery, not ordinary takeover.

This asymmetric failure rule intentionally favors safety over convenience.

## 6. Immutable handoff versions and retention

Formal handoffs are immutable versioned GitHub snapshot+target-bound-grant units, conceptually:

```text
20260827231152.snapshot.db
20260827231152.grant.json
20260828231152.snapshot.db
20260828231152.grant.json
```

Exact filenames are technical details, but semantics are fixed:

- a snapshot without the matching completed ready/grant marker is not a released handoff;
- marker and database must agree on lineage, generation, handoff version, source device, target device and checksum;
- source and target device IDs must be distinct;
- target binding is immutable once the source crosses the durable relinquishment point;
- the pair is one retention unit;
- latest **three** complete validated GitHub handoff units are retained;
- cleanup deletes snapshot+grant by exact remote asset identity only after the newer unit is Released; a temporary fourth unit is allowed and cleanup failure is retryable/non-authority-critical.

## 7. OneDrive shared root

The operator selects one locally available folder inside the OneDrive tree as the Sushi81 POS shared root.

The application creates/manages:

```text
<Selected OneDrive root>\
    Handoff\
    DisasterRecovery\
    System\
```

Semantics:

- `Handoff\` — immutable target-directed formal handoff versions/markers;
- `DisasterRecovery\` — recovery-only checkpoints;
- `System\` — small lineage/device/coordination metadata, including the paired-device set required to validate target identities.

Annual archives are deliberately not stored beneath this OneDrive root after the M12 local-archive amendment.

The selected root is never the live database location.

Changing the shared root occurs only through application settings. A proposed new root must be validated before adoption and must not silently join/combine an unrelated lineage.

## 8. Device and lineage identity

The protocol uses opaque identities rather than fixed roles such as SHOP/HOME.

### 8.1 `device_id`

Each installation receives an immutable random UUID-like `device_id` when initialized.

It is stored in local application configuration and is not ordinary user-editable business data.

A separate human-readable device display name may be editable without changing `device_id` and is used to make target selection understandable when more than one eligible paired device exists.

If a complete Windows reinstall loses the local identity, that installation is treated as a new device and receives a new ID.

### 8.2 `lineage_id`

The first authoritative business database establishes a stable opaque `lineage_id` identifying that business-data family.

All valid live/handoff/disaster-recovery metadata belonging to it carries the same lineage.

A device encountering another lineage must not silently merge/adopt it.

### 8.3 `generation`

A lineage has a generation/epoch.

Normal target-directed handoffs preserve generation. Confirmed Disaster Recovery advances it so devices holding pre-recovery state cannot later resume writing silently.

### 8.4 `handoff_version`

Formal handoffs advance a monotonic handoff version within the current lineage history.

Protocol metadata must identify at least:

- lineage ID;
- generation;
- handoff version;
- source/current device ID;
- target device ID for normal handoff;
- checksum and any required size metadata;
- required timestamps/protocol version.

## 9. Pairing additional devices

The design contains no two-device slots or fixed logical maximum.

### 9.1 First device

On initial setup:

- application creates local live/recovery areas;
- operator selects/creates the OneDrive shared root;
- application establishes device/lineage metadata;
- the first device is authoritative;
- subsequent normal authority transfers use target-directed handoff through that root.

### 9.2 Second, third or later device

To add another computer:

1. install Sushi81 POS normally;
2. create its new `device_id`;
3. select the same existing OneDrive shared root;
4. validate lineage/generation/shared structure;
5. register/join the paired-device set using the approved pairing flow;
6. locate the most recent appropriate formally completed data snapshot available for initialization/read-only hydration;
7. verify applicable metadata + checksum + SQLite integrity;
8. restore the validated version into that device's own local `live.db`;
9. join the lineage in non-authoritative/read-only state unless and until a later formal handoff is specifically targeted to this device.

If another device holds or retains authority, the new device remains read-only.

Pairing a device does not itself release or grant authority.

## 10. Receiving/acquiring a target-directed handoff

Before a non-authoritative device enters write mode from a normal handoff it must:

1. identify the latest formally completed handoff relevant to its known lineage/generation;
2. confirm that its immutable local `device_id` exactly equals the handoff `target_device_id`;
3. confirm source and target IDs are distinct and valid members of the paired-device set for the current lineage/generation;
4. obtain the exact target-bound grant and referenced snapshot from the authenticated private GitHub Release Asset container;
5. verify lineage/generation/handoff-version/source-device/target-device/checksum consistency and required protocol metadata;
6. validate SQLite integrity;
7. compare against durable local generation/version/transfer state and reject stale/replayed acquisition;
8. safely restore the accepted snapshot to local `live.db` when required;
9. durably establish this device as the local authoritative state for that accepted transfer;
10. only then enable business writes.

A non-target device seeing the same valid handoff remains read-only. It does **not** create a competing acquisition claim and may not acquire the handoff by waiting longer or by being the only currently running device.

A partial/starter asset, missing grant/snapshot, checksum or size mismatch, source/target mismatch, lineage/generation mismatch, integrity failure, stale/replayed handoff, authentication/API error or other unresolved failure must never become a writable live database automatically.

## 11. No normal force takeover or retargeting

If the current authoritative device retained authority when it closed, another device must not begin writing from an older local or GitHub version.

If a target-directed transfer has crossed the durable relinquishment point, neither the former source nor a third device may silently substitute itself for the designated target.

Normal remedies are:

- return to/start the current authoritative device and explicitly transfer authority;
- complete/recover the already-designated target handoff and, after the target becomes authoritative, transfer onward if desired; or
- when the applicable authoritative/target device is genuinely unavailable and normal handoff cannot be completed, use explicit Disaster Recovery.

Reliability and prevention of silent divergence take precedence over convenience.

## 12. Disaster recovery

Disaster Recovery is reserved for genuine abnormal loss/unavailability that prevents normal target-directed handoff, such as unrecoverable hardware/disk/OS failure.

It is separate from normal handoff and is never a convenience shortcut for choosing a different target.

### 12.1 Recovery-only cloud checkpoints

While the authoritative device operates normally:

- local recovery continues;
- if durable business data changed since the previous cloud checkpoint, a validated recovery-only checkpoint may be published to `DisasterRecovery`;
- maximum normal publication frequency is **one checkpoint every 15 minutes**;
- if no durable data changed, no redundant checkpoint is required;
- publication must not unnecessarily block ordinary POS work like a formal handoff;
- latest **five** successfully published and validated recovery checkpoints are retained.

These checkpoints do **not** release write authority and are never consumed automatically as ordinary handoffs.

### 12.2 Recovery user flow

If no valid normal authority path is available, ordinary acquisition stays blocked.

A separate Disaster Recovery action displays at least:

- latest completed handoff timestamp/version and designated target where applicable;
- newest validated recovery checkpoint timestamp;
- checkpoint source device;
- warning that changes after the checkpoint may be lost;
- warning that Disaster Recovery creates a new generation and invalidates older-generation write eligibility.

After explicit confirmation, the application:

1. validates the selected appropriate checkpoint;
2. restores it to local `live.db`;
3. advances the lineage generation;
4. records the new authoritative generation/device;
5. enables writes only after activation succeeds.

### 12.3 Old-generation invalidation

A device later returning with an older generation may not resume writes from its stale local database or old handoff grant, regardless of file timestamps.

It must be reinitialized from current authoritative released data before write capability is restored.

Merely forgetting to transfer authority is not sufficient reason to use Disaster Recovery while the applicable authoritative/designated-target path remains recoverable.

## 13. Non-authoritative read-only mode

A paired device that is not the current authoritative device may open Sushi81 POS in clearly marked non-authoritative/read-only mode.

This includes:

- ordinary paired devices that were not selected as the target;
- a former source that has crossed the durable relinquishment point;
- a designated target before it has fully validated/acquired its handoff;
- stale/old-generation devices.

It may consult:

- the most recent formally acquired local live-data copy it already holds;
- completed annual archives.

The UI must clearly and persistently show:

- read-only/non-authoritative or pending-transfer state;
- that current live data may be stale where applicable;
- the version and/or effective time of the most recently acquired data;
- target/pending-transfer information where operationally useful.

A newer recovery-only checkpoint is not consumed as an ordinary read-only update.

Read-only mode blocks authoritative business writes including:

- creating/modifying orders;
- changing payments;
- closing/cancelling orders;
- catalogue/category/option edits;
- catalogue imports;
- business-setting changes;
- annual archive execution;
- creation of a new formal handoff from a stale/non-authoritative copy.

A former source in pending-transfer state may perform only technical retries needed to complete its already-fixed immutable transfer; those retries are not business-authoritative writes.

### 13.1 Printing from non-authoritative data — Phase 4 alignment

The later Approved printing decision remains final for V1: a non-authoritative/read-only device **may print/reprint** from the committed live-data copy currently available on that device.

It is not hard-blocked, but before/at the print action the application must clearly indicate that:

- this device is non-authoritative;
- displayed live data may be stale;
- freshness has not been verified.

Printing performs no business write, does not transfer authority and does not claim synchronization success.

Detailed marking/printing behavior remains in `printing.md` and `docs/decisions/non-authoritative-device-printing.md`.

## 14. Offline behavior

The authoritative device may continue normal local operation during temporary Internet/GitHub loss because `live.db` is local.

**Close and retain authority** remains possible as a local close operation; it does not claim to publish or release authority.

A new **Transfer authority and close** cannot complete until required GitHub snapshot/grant publication receives strict server receipts (HTTP 201, uploaded state, exact name/size/asset ID/digest).

If a transfer fails before durable relinquishment, it may be safely aborted and authority retained. If GitHub publication fails after durable relinquishment, the source remains read-only/pending-transfer and may retry the same technical transfer when connectivity returns.

Cloud disaster-recovery checkpoints may remain pending/unavailable while offline; ordinary local work remains authoritative only while the device has not relinquished authority.

A non-authoritative device that cannot validate a handoff specifically targeted to itself cannot enter write mode from an older copy, though read-only use and approved printing remain available.

## 15. Annual archive

Annual archives are separate historical SQLite files stored in the application-managed local `Archive\` area, independent from installed binaries and active local `live.db`.

They are read-only historical databases in ordinary POS use and are not part of normal live handoff lineage.

### 15.1 Schedule and strict calendar-year boundary

The authoritative device automatically processes the previous **complete calendar year** on **February 1** each year.

The trigger date never extends the target window into January of the current year.

Example: the February 1, 2027 archive processes only archive-year 2026 records ending no later than December 31, 2026.

If the POS is not run on February 1, the archive occurs on the first later startup on which the authoritative device can safely perform it. Delayed execution does not enlarge the target year.

Only the authoritative device may create an annual archive.

The automatic February/late-start flow is non-interactive: it does not ask the operator to choose a destination path.

### 15.2 Archive-year rules

Archive year is the natural year in which the business order ends:

- `CLOSED` order -> year of `closed_at`;
- `CANCELLED` order -> year of `cancelled_at`;
- `OPEN` order -> remains in `live.db` regardless of age.

This rule applies equally to ordinary `POS` and hidden-source `HIBOUTIK_PASTE` orders. The source discriminator affects ordinary POS financial/export inclusion; it does not create different archive-year semantics.

Example: an order created December 2026 and Closed January 10, 2027 belongs to archive year 2027, stays live through the February 2027 archive and becomes eligible when archive year 2027 is processed in February 2028.

### 15.3 Local publication safety

Archive creation is failure-safe:

1. identify target-year eligible records;
2. build the archive in safe local staging under application-managed temporary storage;
3. validate archive schema, SQLite integrity and expected records;
4. durably promote the completed archive into the application-managed local `Archive\` area;
5. reopen and validate the promoted canonical archive file;
6. only then remove those records from `live.db`;
7. create a new local recovery point; later formal handoff contains the post-archive live state.

If creation, validation, promotion or post-promotion validation fails, records remain live and archiving is retried later.

OneDrive publication/synchronization is not part of annual archive completion.

Before any eligible order leaves `live.db`, the M12 implementation must also preserve every valid not-yet-emitted/pending Gestion export action as the durable immutable technical payload required by the approved implementation plan.

### 15.4 Local archive independence and retention

Completed canonical annual archives:

- live under application-managed local business-data storage, conceptually `%LOCALAPPDATA%\Sushi81 POS\Archive\`;
- survive ordinary application update/reinstall because the installer must preserve application business data;
- are not automatically synchronized/shared between paired devices;
- are immutable/read-only in normal POS use;
- remain queryable/reprintable through explicit archive-year selection;
- are retained permanently by normal POS workflow with no rolling deletion;
- remain outside normal handoff lineage.

Conceptually:

```text
%LOCALAPPDATA%\Sushi81 POS\Archive\
    sushi81-archive-2026.db
    sushi81-archive-2027.db
```

Exact filenames may be refined technically.

A normal authority handoff transfers the active live lineage/snapshot only; it does not copy local annual archive files to the target device.

### 15.5 Explicit user-selected archive export

The operator may explicitly export/copy a completed validated annual archive.

Rules:

- the operator chooses the destination path/location;
- export copies the archive and never moves/deletes the canonical local archive;
- export is separate from automatic February/late-start archiving;
- export failure must leave the canonical local archive and `live.db` unchanged;
- an existing destination must not be silently destroyed by a failed export/finalization attempt.

The chosen destination may be a local disk, removable drive, network/shared folder or other filesystem location selected by the operator. Sushi81 POS does not require OneDrive for this action.

## 16. Rolling retention

V1 uses:

- Local Recovery: latest **5** validated snapshots;
- GitHub Handoff: latest **3** complete validated target-directed snapshot+grant units;
- OneDrive Disaster Recovery: latest **5** validated checkpoints;
- Annual Archive: permanent local canonical files/no rolling deletion.

Cleanup happens after, never before, a newer replacement is safely generated/validated and, where required, synchronized.

These counts are fixed V1 technical defaults rather than ordinary operator-configurable settings.

## 17. Storage invariants

Implementation must preserve all of the following:

1. `live.db` is local/application-managed, never a live OneDrive database.
2. Any number of devices may pair; exactly-two-device assumptions are forbidden.
3. At most one device writes at a time.
4. Normal application close does not automatically release authority; the operator explicitly chooses retain-versus-transfer behavior.
5. Normal write transfer is target-directed by the current authoritative device to exactly one eligible paired `target_device_id`.
6. The source must durably relinquish business-write authority before a target-releasing ready/grant marker can exist.
7. After durable relinquishment, the source may retry only the same immutable transfer and may not silently resume writes or retarget it.
8. Only the exact designated target may acquire a normal handoff; non-target devices remain read-only and do not compete through claims/election.
9. No stale normal force takeover or target substitution is permitted.
10. Disaster Recovery is explicit/recovery-only and creates a new generation.
11. Old-generation devices cannot resume writes without reinitialization.
12. Non-authoritative devices use clearly marked stale/read-only or pending-transfer state.
13. Non-authoritative printing is allowed only under the explicit stale-data warning rules already Approved in Phase 4.
14. Local Recovery/Handoff/Disaster Recovery each retain five valid rolling versions.
15. Annual archive targets only the previous complete natural year when triggered on/after February 1.
16. Closed and Cancelled orders use their end timestamp year; Open orders remain live.
17. Archive records leave `live.db` only after validated durable publication of the canonical local archive file and required pending-export preservation.
18. Annual archives remain independent permanent local historical files; ordinary handoff does not copy them between devices.
19. Explicit archive export uses an operator-selected destination and never moves/deletes the canonical local archive.
20. GitHub Release Asset server acknowledgement is required for normal handoff; OneDrive synchronization is not part of that authority gate and is not a distributed lock or competitive acquisition primitive.

## 18. Approval

This document remains the **Approved — Phase 3 baseline**, amended on 2026-08-28 by `docs/decisions/target-directed-authority-handoff.md` after the M02 feasibility blocker and by `docs/decisions/github-handoff-transport.md` for normal handoff transport, and on 2026-09-21 by `docs/decisions/m12-local-archive-and-user-selected-export.md` for annual archive storage/export.

The storage architecture is local SQLite per paired device, N-device single-writer authority, **target-directed source-arbitrated normal handoff**, application-managed local recovery, change-triggered recovery-only cloud checkpoints, explicit generation-changing Disaster Recovery, non-authoritative read-only access, five-version rolling technical protection and independent permanent **local** February natural-year archives with optional operator-selected export copies.

Low-level filenames, coordination serialization and similar pure implementation details may be selected during implementation only if every invariant above remains true.
