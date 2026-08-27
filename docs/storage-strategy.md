# Storage strategy

**Status:** Draft — Phase 3 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define how live data, local recovery snapshots, OneDrive handoff snapshots and archive databases are stored and transferred without allowing two computers to diverge silently.

## 1. Core storage principle

Each Sushi81 POS computer uses its own **local working SQLite database**.

The live database is never directly opened from a OneDrive-synchronized folder and is never intentionally written by two computers at the same time.

OneDrive is used only for controlled transfer of validated complete snapshots and for retained recovery/backup copies.

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

1. identify the latest formally completed handoff version available through OneDrive;
2. ensure both the immutable snapshot and matching ready marker are locally available;
3. verify version/checksum consistency;
4. run SQLite integrity validation on the received snapshot;
5. compare it with the computer's existing local working version;
6. if the received handoff is newer, replace/restore the local working database from that validated snapshot using an atomic/safe local operation;
7. only after successful acquisition may the application allow normal writes.

A partially synchronized snapshot, a snapshot with no ready marker, a checksum mismatch or a failed integrity check must never become the local live database automatically.

## 6. No normal force takeover — approved Phase 3 decision

If the current working computer did not complete a formal handoff, the other computer must **not** start writing from an older OneDrive version.

There is no ordinary "force takeover" button that silently promotes stale data.

If the shop computer was left running or was closed without completing handoff, the normal remedy is to return to that computer and complete the POS close/handoff process.

This inconvenience is deliberate: preventing silent data loss or database divergence has priority over allowing a second computer to continue from uncertain/stale data.

## 7. Forgotten close and abnormal termination

If the operator forgets to close Sushi81 POS normally, formal handoff may not have occurred.

The project explicitly accepts this operational consequence rather than attempting to guess or merge uncertain states automatically.

Unexpected power loss, operating-system termination, application crash or hardware failure are treated separately as **recovery events**, not as normal handoff.

Local recovery snapshots and any future disaster-recovery copies may be used to recover as much data as possible, but recovery must clearly disclose the recovery point/time and must not be presented as a guaranteed current handoff.

Exact disaster-recovery workflow is still to be specified.

## 8. Single-writer / anti-fork invariant

The central safety invariant is:

> At most one computer is permitted to advance the authoritative business database lineage at a time.

Normal acquisition must always descend from the latest completed handoff lineage.

The system must never silently create two valid-looking successors from the same earlier authoritative version.

No automatic database merge algorithm is required in v1.

## 9. Offline behavior

The computer currently holding the active local working lineage may continue normal local operation during temporary Internet/OneDrive loss because its SQLite database is local.

However, it cannot complete a successful formal handoff until the required OneDrive synchronization is confirmed.

A second computer that cannot verify/acquire the latest completed handoff may not enter normal write mode merely because it has an older local copy.

## 10. Local database versus OneDrive location

The active working SQLite database must reside in a normal local application-data location outside OneDrive synchronization.

The OneDrive handoff area contains only validated released snapshots, ready markers and retained recovery/backup artifacts approved by this strategy.

The exact Windows paths and whether operator-visible export/archive folders live elsewhere are still to be frozen.

## 11. Annual archive relationship

The logical archive model remains governed by `data-model.md` and `product-requirements.md`:

- the live database retains the current natural year plus older unresolved records that still need to remain live;
- eligible older completed data may be manually archived by natural year into separate archive databases;
- archived orders remain queryable and reprintable;
- live/archive databases use compatible logical schemas.

This document has not yet frozen archive filenames, exact archive folder location, archive transfer between computers or retention rules for Cancelled/emergency records; those are still part of Phase 3 storage review.

## 12. Decisions still to freeze

Before this document becomes an approved Phase 3 baseline, the project should still decide:

- exact Windows local data folder structure;
- exact OneDrive handoff folder structure/configuration;
- local recovery snapshot retention policy;
- formal handoff snapshot retention policy;
- disaster-recovery procedure;
- archive database filenames/location and archive-year treatment for Cancelled/Hiboutik emergency records;
- how first installation/pairing of the two computers establishes device identity and initial authoritative version;
- whether the application supports read-only access on a non-authoritative computer before handoff.

## 13. Approval rule

The handoff mechanism in sections 1-10 is an approved Phase 3 direction: local working SQLite, local recovery snapshots, normal-exit formal handoff, immutable versioned OneDrive publication, completion marker, verified receiver acquisition and no stale force takeover.

The document remains **Draft — Phase 3 working design** until the remaining folder/retention/recovery/archive details are reviewed.