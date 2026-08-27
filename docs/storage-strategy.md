# Storage strategy

**Status:** Draft — Phase 3 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define how live data, local recovery snapshots, OneDrive handoff snapshots and archive databases are stored and transferred without allowing two computers to diverge silently.

## 1. Core storage principle

Each Sushi81 POS computer uses its own **local working SQLite database**.

The live database is never directly opened from a OneDrive-synchronized folder and is never intentionally written by two computers at the same time.

OneDrive is used only for controlled transfer of validated complete snapshots and for retained recovery/backup/archive copies.

The operator does not manually manage the active `live.db` file or local recovery snapshot files.

## 2. Two distinct snapshot purposes

### 2.1 Local recovery snapshots

During normal use, important durable business saves trigger local recovery protection.

Typical triggers include:

- confirmation of a new order;
- saving an order modification;
- changing CB/Espèce amounts;
- closing or cancelling an order;
- saving catalogue changes;
- successful catalogue batch import;
- saving business settings.

Local snapshot generation may use a short debounce/coalescing delay so several saves occurring within a few seconds can be protected by one snapshot rather than creating unnecessary duplicate files.

These snapshots remain local and exist primarily for crash/corruption/accidental-change recovery.

They are application-managed technical recovery data. The normal operator workflow does not require browsing, copying, renaming or selecting these files.

The exact local snapshot retention count/time window is still to be frozen.

### 2.2 Formal handoff snapshots

A **formal handoff snapshot** is the only normal mechanism by which another computer receives authority to continue editing Sushi81 POS business data.

Normal application exit triggers this handoff automatically.

The operator does not manually copy database files, use a USB drive or choose which snapshot to send.

## 3. Normal exit equals formal handoff — approved Phase 3 decision

When the operator closes Sushi81 POS, the application does not immediately terminate.

It enters a handoff/closing state in which new business edits are blocked and performs the following sequence:

1. complete and commit all accepted application/database writes;
2. generate a complete SQLite handoff snapshot using a SQLite-safe backup mechanism rather than blindly copying an active database file;
3. run local integrity validation on the generated snapshot;
4. assign an immutable monotonically advancing handoff version;
5. compute and retain a checksum/hash for the snapshot;
6. place/publish the snapshot into the configured OneDrive handoff area;
7. monitor OneDrive/Windows synchronization status until the snapshot is confirmed no longer pending upload and no sync error is reported;
8. publish a small completion/ready marker containing at least the handoff version and snapshot checksum;
9. wait until that completion marker is also confirmed synchronized;
10. only then mark the handoff successful, show a clear success confirmation and complete application exit.

If any required step fails, the application must not report a successful handoff.

The normal user-facing workflow is therefore simply:

**Close POS -> wait for successful handoff confirmation -> leave.**

## 4. Immutable handoff versions

Formal handoff snapshots should be versioned as immutable files rather than repeatedly overwriting one `latest.db` while it is being synchronized.

Conceptually:

- `handoff-v000157.db`
- `handoff-v000157.ready`
- `handoff-v000158.db`
- `handoff-v000158.ready`

Exact filenames are implementation details, but the semantics are fixed:

- a handoff database without its matching completed ready marker is **not** an accepted released version;
- the ready marker references the exact expected version/checksum;
- older complete versions can remain available according to a retention policy rather than being destroyed immediately by the next handoff.

## 5. Receiving computer startup

Before another computer can enter normal write/edit mode, it must establish that a valid released handoff exists.

Startup/acquisition sequence:

1. identify the latest formally completed handoff version available through the configured OneDrive handoff folder;
2. ensure both the immutable snapshot and matching ready marker are locally available;
3. verify version/checksum consistency;
4. run SQLite integrity validation on the received snapshot;
5. compare it with the computer's existing local working version;
6. if the received handoff is newer, replace/restore the local working database from that validated snapshot using an atomic/safe local operation;
7. only after successful acquisition may the application allow normal writes.

A partially synchronized snapshot, a snapshot with no ready marker, a checksum mismatch or a failed integrity check must never become the local live database automatically.

## 6. OneDrive handoff-folder configuration and pairing — approved Phase 3 decision

The operator may choose the OneDrive folder used by Sushi81 POS for formal handoff exchange.

This folder selection is the normal user-facing configuration point for cross-computer synchronization. It does **not** expose or relocate the local working database.

### 6.1 First computer

On first installation/setup of the first Sushi81 POS computer:

- the application keeps its local working `live.db` and local recovery area in application-managed local storage;
- the operator chooses or creates a folder inside the locally available OneDrive tree to serve as the Sushi81 POS handoff folder;
- the application validates that the selected path is suitable and stores the path in local machine configuration;
- the application initializes the required handoff metadata/structure in that folder as needed;
- subsequent formal handoffs are published there automatically.

The operator is not asked to choose a location for `live.db` or for local recovery snapshots.

### 6.2 Second computer

On installation/setup of another Sushi81 POS computer:

- the operator opens the synchronization/storage setting and selects the same OneDrive handoff folder as the first computer;
- the application scans that folder for the latest formally completed handoff version;
- only a snapshot with its matching completed ready marker and valid checksum/integrity may be accepted;
- the application copies/restores that validated handoff into its own application-managed local `live.db` location;
- the OneDrive snapshot itself is never opened as the working database;
- after successful initialization/acquisition, subsequent starts follow the normal handoff rules in section 5.

This pairing model allows both computers to participate in the same authoritative database lineage without manual file copying or USB transfer.

### 6.3 User interaction boundary

Normal users may configure/change which OneDrive handoff folder Sushi81 POS uses, but they are not expected to:

- open or edit handoff database files;
- rename versioned snapshot or ready-marker files;
- copy snapshots manually between computers;
- move the local working database;
- manage local recovery snapshot files.

If the handoff folder setting is changed later, the application must validate the new location before adopting it and must not silently begin a new unrelated authoritative lineage.

Exact validation/relink behavior for changing an already-established handoff folder may be finalized during implementation/storage review, but silent data-lineage splitting is forbidden.

## 7. No normal force takeover — approved Phase 3 decision

If the current working computer did not complete a formal handoff, the other computer must **not** start writing from an older OneDrive version.

There is no ordinary "force takeover" button that silently promotes stale data.

If the shop computer was left running or was closed without completing handoff, the normal remedy is to return to that computer and complete the POS close/handoff process.

This inconvenience is deliberate: preventing silent data loss or database divergence has priority over allowing a second computer to continue from uncertain/stale data.

## 8. Forgotten close and abnormal termination

If the operator forgets to close Sushi81 POS normally, formal handoff may not have occurred.

The project explicitly accepts this operational consequence rather than attempting to guess or merge uncertain states automatically.

Unexpected power loss, operating-system termination, application crash or hardware failure are treated separately as **recovery events**, not as normal handoff.

Local recovery snapshots and any future disaster-recovery copies may be used to recover as much data as possible, but recovery must clearly disclose the recovery point/time and must not be presented as a guaranteed current handoff.

Exact disaster-recovery workflow is still to be specified.

## 9. Single-writer / anti-fork invariant

The central safety invariant is:

> At most one computer is permitted to advance the authoritative business database lineage at a time.

Normal acquisition must always descend from the latest completed handoff lineage.

The system must never silently create two valid-looking successors from the same earlier authoritative version.

No automatic database merge algorithm is required in v1.

## 10. Offline behavior

The computer currently holding the active local working lineage may continue normal local operation during temporary Internet/OneDrive loss because its SQLite database is local.

However, it cannot complete a successful formal handoff until the required OneDrive synchronization is confirmed.

A second computer that cannot verify/acquire the latest completed handoff may not enter normal write mode merely because it has an older local copy.

## 11. Local database versus OneDrive location

The active working SQLite database must reside in a normal application-managed local data location outside OneDrive synchronization.

The local recovery-snapshot area is also application managed and is not part of ordinary operator file handling.

The OneDrive handoff area is operator-configurable and contains only validated/released handoff snapshots, ready markers and any retained recovery/backup artifacts explicitly approved by this strategy.

The application must never interpret the configured OneDrive handoff folder itself as the location of the working `live.db`.

Exact Windows local paths and whether operator-visible export folders live elsewhere are still to be frozen.

## 12. Annual archive — approved Phase 3 decision

Annual order archives are separate historical database files stored in OneDrive and kept independent from the installed application and from the active local `live.db`.

They are not working databases and are not part of the normal handoff lineage.

### 12.1 Automatic schedule

The application automatically performs the previous calendar year's archive on **February 1** each year.

If Sushi81 POS is not run on February 1, the archive is performed automatically on the first later startup on which the computer currently holding the authoritative write lineage can safely perform it.

Only the current authoritative/writable computer may create an annual archive. A non-authoritative computer must never independently create a competing archive from an older local database.

Example:

- on February 1, 2027, the application archives all records whose approved archive year is 2026;
- if the application is first opened on February 3, 2027, the same 2026 archive is performed then.

### 12.2 Archive-year rules

An order is archived according to the natural year in which it is actually completed/ended, not merely the year in which it was created.

The rules are:

- ordinary `CLOSED` POS order => archive year = year of `closed_at`;
- ordinary `CANCELLED` POS order => archive year = year of `cancelled_at`;
- Hiboutik emergency order ending as `CLOSED` => archive year = year of `closed_at`;
- Hiboutik emergency order ending as `CANCELLED` => archive year = year of `cancelled_at`;
- any order still `OPEN` at archive time remains in `live.db`, regardless of creation date or planned fulfilment date.

Therefore an unresolved order from an older year remains live until it is eventually closed or cancelled. It is then archived under the year of that actual closing/cancellation and will be moved during the following year's February archive cycle.

### 12.3 Archive file location and independence

Annual archive databases are stored in an operator-visible OneDrive archive folder outside the application installation/data directories.

A recommended conceptual organization is:

```text
OneDrive\Sushi81 POS\
    Handoff\
    Archive\
        sushi81-archive-2026.db
        sushi81-archive-2027.db
```

The exact folder/file names may be refined during implementation, but these semantics are fixed:

- archive files are ordinary independent files that survive application uninstall/reinstall;
- they are not hidden inside the program installation directory;
- they are not stored inside either computer's local application-data area as the authoritative archive copy;
- OneDrive provides the shared durable location visible from both computers;
- the application may offer an "Open archive folder" action, but the user does not need to move files manually for normal archive operation.

### 12.4 Safe archive creation

Archiving must be failure-safe.

The application must:

1. identify all records eligible for the target archive year;
2. build the annual archive database in a safe temporary/local staging location;
3. validate the archive database and verify that the intended records are present;
4. publish the completed archive database to the configured OneDrive Archive area;
5. confirm successful OneDrive synchronization/publication;
6. only after successful archive publication remove the archived records from the active `live.db`;
7. generate a new local recovery point and, when the application later closes, include the post-archive live state in the next normal handoff snapshot.

If archive generation, validation or OneDrive publication fails, eligible records remain in `live.db` and the application retries later rather than risking data loss.

### 12.5 Archive immutability and access

A completed annual archive is historical business data and should normally be treated as read-only/immutable.

Archived orders must remain queryable and reprintable from Sushi81 POS as required by `product-requirements.md`.

The application should therefore discover registered annual archive files from the configured OneDrive Archive location and access them in read-only mode. If direct access to a OneDrive-backed file is not sufficiently reliable on a particular machine, the implementation may transparently hydrate/copy the archive to a local read-only cache before querying it; this must not turn the archive into a second writable business database.

Annual archives are not subject to automatic rolling deletion. They are retained permanently unless the operator deliberately manages them outside the normal application workflow.

## 13. Decisions still to freeze

Before this document becomes an approved Phase 3 baseline, the project should still decide:

- exact Windows local data folder structure;
- exact OneDrive handoff/archive subfolder naming beyond the approved user-selected root concept;
- local recovery snapshot retention policy;
- formal handoff snapshot retention policy;
- disaster-recovery procedure;
- exact device-identity metadata created during first installation/pairing;
- whether the application supports read-only access on a non-authoritative computer before handoff.

## 14. Approval rule

The handoff mechanism in sections 1-11 and annual archive behavior in section 12 are approved Phase 3 directions: local working SQLite, application-managed local recovery snapshots, normal-exit formal handoff, immutable versioned OneDrive publication, completion marker, verified receiver acquisition, user-selected OneDrive handoff-folder pairing, no stale force takeover, automatic February 1 annual archiving, end-date-based archive-year assignment, and independent OneDrive archive databases.

The document remains **Draft — Phase 3 working design** until the remaining folder/retention/recovery/access details are reviewed.