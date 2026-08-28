# Target-directed authority handoff

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-08-28  
**Trigger:** M02 OneDrive single-writer feasibility blocker  
**Affected documents:** `product-requirements.md` where applicable, `architecture.md`, `storage-strategy.md`, `acceptance-criteria.md`, `v1-specification-freeze.md`, `implementation-plan.md`, `implementation-status.md`

## Context

M02 proved that an eventually synchronized OneDrive/local-filesystem claim/election protocol cannot provide the frozen guarantee that arbitrary paired devices may compete for the same released handoff while still proving that at most one device becomes writable under delayed, reordered or offline visibility.

The M02 deterministic model contains an executable double-writer counterexample for claim-only acquisition. A fail-closed implementation can prevent double writers only by refusing N-device write activation unless an external atomic grant exists, which does not satisfy the intended normal handoff workflow.

The project does not want to add Microsoft Graph, OAuth, Azure, a hosted coordinator, a remote database or another server merely to arbitrate normal POS ownership.

## Decision

V1 keeps the N-device, local-SQLite, single-writer architecture, but **normal authority transfer is target-directed by the current authoritative device**. Devices no longer compete to acquire a generic released handoff.

### 1. Two distinct close operations

The authoritative device exposes two distinct operator intents:

1. **Close and retain authority**
   - closes the application without publishing a formal authority release;
   - the same device remains the authoritative device for the lineage;
   - on its next valid launch it may continue writing locally;
   - every other paired device remains non-authoritative/read-only;
   - this is the safe/default close meaning when no transfer is explicitly requested.

2. **Transfer authority and close**
   - explicitly transfers normal write authority to exactly one other eligible paired device;
   - with one eligible other device, the UI may preselect that target, but transfer remains an explicit action;
   - with multiple eligible devices, the operator selects the target by human-readable device name;
   - the selected target is represented by immutable `target_device_id` in the handoff metadata.

Closing the window must never silently turn a generic handoff into an N-device election.

### 2. Source device is the normal-transfer arbiter

Only the current authoritative device may create a normal transfer grant.

For one lineage/generation/handoff version, the source durably commits one source/target identity. After the source reaches the irreversible local relinquishment point, restart/retry may only continue the **same** target-bound transfer. It may not choose another target or resume business writes merely because OneDrive publication is delayed.

This removes the need for cross-client claim arbitration during normal handoff.

### 3. Safety ordering for target-directed transfer

A normal transfer follows this safety order:

1. operator explicitly chooses **Transfer authority and close** and one eligible target device;
2. source blocks new business edits and finishes accepted writes;
3. source creates a SQLite-safe complete snapshot;
4. source validates SQLite integrity;
5. source assigns the next immutable handoff version and computes checksum/required metadata;
6. source publishes the immutable snapshot to OneDrive and waits for the required documented synchronization confirmation;
7. source **durably records local relinquishment/pending-transfer state** containing at least lineage, generation, handoff version, source device, target device and checksum;
8. from that durable relinquishment point onward, the source must remain business-read-only across process restart; it may perform only technical work needed to finish/retry the already-fixed transfer;
9. only after step 7 succeeds may the source create/publish the matching target-bound ready/grant marker;
10. source waits until that marker is also confirmed synchronized;
11. only then may the UI report the normal transfer as completed and finish the close flow.

The target-bound ready/grant marker must not exist before the source has durably relinquished business-write authority.

A snapshot alone never releases authority to another device.

### 4. Failure boundaries

Before the durable relinquishment point, a failed/cancelled transfer may safely abort and leave the source authoritative, provided no target-bound ready/grant marker has been published.

After the durable relinquishment point:

- the source may not roll back to ordinary writable authority;
- restart remains read-only/pending-transfer;
- the source may retry publication/synchronization of the same immutable target-bound grant;
- it may not retarget the handoff;
- if the target-bound transfer can no longer be completed and the intended target cannot be recovered, the recovery path is explicit Disaster Recovery, not ordinary force takeover.

Safety takes precedence over convenience at this boundary.

### 5. Target acquisition

A receiving device may acquire a normal handoff only when:

- its immutable local `device_id` exactly equals the handoff `target_device_id`;
- source and target IDs are distinct and both belong to the current paired-device set for the lineage/generation;
- snapshot and target-bound ready/grant marker are both locally available;
- lineage, generation, handoff version, source device, target device and checksum metadata match;
- SQLite integrity and any required size/checksum validation pass;
- the handoff is not stale relative to the device's known durable state;
- no failure/unknown condition applies.

A non-target device seeing the same valid handoff remains read-only and must not create a competing claim.

No claim/election protocol is part of normal V1 authority transfer after this amendment.

### 6. Target unavailable after transfer

Once a target-bound release has crossed the durable relinquishment point, the former source does not silently reclaim authority.

Normal remedies are:

- start/recover the designated target and complete/acquire the handoff, then transfer onward if desired; or
- if the designated target is genuinely unavailable, use the explicit Disaster Recovery flow and advance generation.

A third device may not take the target's place through ordinary takeover.

### 7. Pairing and N-device behavior

The architecture remains N-device.

Each installation keeps an opaque immutable `device_id` and optional human-readable display name. The shared lineage metadata keeps the paired-device set required to select/validate a target.

There are no fixed SHOP/HOME slots and no fixed logical maximum.

### 8. Disaster Recovery remains exceptional

This amendment does not convert Disaster Recovery into a normal handoff shortcut.

Disaster Recovery remains explicit, warning-driven, generation-advancing and reserved for genuine abnormal loss/unavailability where normal target-directed handoff cannot be completed. Old-generation devices remain prohibited from resuming writes without reinitialization.

## Rationale

The current authoritative device is already the one actor entitled to make a normal ownership decision. Binding the next handoff to one target therefore turns authority movement into a directed token transfer instead of a distributed election.

The crucial safety rule is that the source becomes durably non-writable **before** the target-releasing marker can exist. This means OneDrive only transports immutable state/grant artifacts; it is not asked to provide an atomic distributed lock.

The design preserves the project's priorities:

**reliability > simplicity > maintainability > operational clarity > novelty.**

It also preserves the local-first/no-hosted-backend boundary.

## Superseded behavior

The following earlier V1 assumptions are superseded:

- ordinary application exit automatically releases authority;
- a generic released handoff may be competed for by multiple paired devices;
- normal acquisition needs a distributed claim/election over OneDrive files.

The affected baseline and acceptance documents are amended to contain the target-directed semantics directly so implementation must not rely on chronology to resolve the change.