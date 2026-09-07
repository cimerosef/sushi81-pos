# Acceptance-criteria amendment — M07 self-join and disaster recovery

**Status:** Approved — V1 acceptance amendment  
**Date:** 2026-09-07  
**Decision source:** `docs/decisions/m07-self-join-disaster-recovery.md`  
**Scope:** Clarify AC-PROD-002 and AC-STO-002/005/009 for M07 without weakening AC-STO-003/004/007/008/010.

This file is controlling where the corresponding wording in `acceptance-criteria.md` is less specific. It must be consolidated into the main acceptance document during the next repository-wide specification consolidation, but M07 implementation and review must apply this amendment now.

## AC-PROD-002 — local-first operation with online-only coordination actions

The current authoritative device supports normal local order entry/modification, catalogue/settings writes, payment/lifecycle writes and other already-approved local business operations during temporary Internet, GitHub or OneDrive unavailability, subject to later printer-adapter acceptance owned by M08.

Network-dependent operations may be unavailable while offline, including:

- normal GitHub handoff publication/acquisition that requires server evidence;
- OneDrive disaster-recovery checkpoint publication/observation;
- Disaster Recovery activation;
- OneDrive archive publication where later applicable.

Temporary network loss alone must not make an already-valid authoritative device read-only through a renewable authority lease.

**Evidence:** automated offline/network-failure tests plus M07 owner acceptance that an already-authoritative PC continues ordinary local business writes offline while handoff/DR network actions fail clearly without corrupting authority state.

## AC-STO-002 — N-device membership, self-join and single writer

The design supports more than two devices without fixed SHOP/HOME slots.

A newly installed device may self-join an existing Sushi81 POS lineage without approval from the current/former authoritative device. Self-join creates/binds its immutable `device_id` and current-lineage/generation membership only. **Self-join never releases, transfers, claims or grants write authority.**

After normal self-join:

- if another device still holds valid authority, the new device remains non-authoritative/read-only;
- it can become authoritative through ordinary operation only when the current authoritative source later directs a valid normal handoff to that exact `device_id`;
- if the former authoritative/designated-target path is genuinely unavailable, the self-joined replacement device may instead start the separate explicit DR workflow under AC-STO-009.

At most one device may be accepted as authoritative/writable in the coordinated current generation. Non-target devices never compete for ordinary handoff authority.

A newly joined replacement device with no usable ordinary read-only seed may remain paired/uninitialized/read-only and must still be able to start valid DR; absence of a seed must not force an unsafe authority grant or create an unrecoverable onboarding dead end.

**Evidence:** three-device tests; fresh-install self-join without source approval; joining while an authority exists; joining after permanent authority loss; no-authority-from-membership tests; reinstall/new-device-ID tests; current-generation target eligibility tests.

## AC-STO-005 — no silent force takeover; replacement PC uses DR

The existing no-force-takeover/source-rollback/target-substitution rules remain unchanged for normal operation.

Self-join does not create an exception to those rules. A self-joined replacement computer may not simply promote its local/remote data to writable state because the former authority appears absent.

If the authoritative device retained authority or a target-directed transfer cannot be completed/recovered, the only abnormal path to make a replacement/third device writable is explicit Disaster Recovery satisfying AC-STO-009.

**Evidence:** protocol/UI tests demonstrating self-join remains read-only, normal grant target binding is enforced, old source cannot rollback after relinquishment, third device cannot ordinary-acquire, and replacement-PC recovery routes only through DR.

## AC-STO-008 — recovery-only checkpoints remain non-authority artifacts

The existing AC-STO-008 is retained. In addition:

- OneDrive checkpoint metadata must carry enough lineage/generation/source/change-ordering/integrity information to participate safely in DR candidate comparison;
- publication/retention failure must not alter local write authority;
- a checkpoint is never a self-join approval, pairing grant or ordinary authority grant.

**Evidence:** changed/unchanged scheduling, 15-minute maximum normal frequency, newest-five retention, metadata/integrity, offline publication and no-authority-effect tests.

## AC-STO-009 — explicit operationally fenced DR with safe-candidate recovery

Disaster Recovery is reserved for genuine abnormal inability to complete/recover the normal authoritative or target-directed path.

### Operator safety precondition

Before starting authority activation, the operator must explicitly confirm that the prior authoritative/designated-target device is genuinely unavailable and will remain stopped/quarantined from Sushi81 POS business writes until reinitialized.

The application must present this as a real safety condition, not imply that software can remotely stop an already-running disconnected stale writer.

### Eligible recovery data

The selected DR source must be a validated safe candidate from the current pre-recovery generation:

1. a complete GitHub handoff snapshot with its valid matching target-bound grant; or
2. a complete validated OneDrive recovery-only checkpoint.

A GitHub snapshot without its valid matching grant is never eligible.

The application must validate candidate lineage/generation/protocol identity, applicable ordering/high-water information, size/SHA-256 and SQLite integrity/schema before activation. It should select the freshest provably ordered safe candidate. If valid candidates cannot be safely ordered, it must not silently choose by wall-clock timestamp; the ambiguity and possible data-loss window must be shown for explicit operator selection.

### Online next-generation single-winner activation

DR itself requires online coordination. Before the recovering device may become locally authoritative:

1. determine immutable recovery identity and next generation `G+1` from current generation `G`;
2. obtain a strict server-acknowledged create-once/single-winner recovery activation in the configured private GitHub handoff repository;
3. durably record local DR-pending state while the write guard remains non-writable;
4. restore/install and reopen/revalidate the selected candidate locally;
5. durably commit current-device authority in generation `G+1` tied to the exact recovery activation;
6. only then enable business writes.

The server activation primitive must be proven by deterministic concurrency tests and a disposable real-private-repository race/retry drill before M07 can pass. If it cannot prove at most one accepted activation for one lineage/next generation, M07 is Blocked — architecture decision required.

A process crash after accepted remote activation but before local authority commit must leave zero writable recovery devices; only the same activation/recovery identity/device may resume idempotently.

### Old-generation invalidation

After successful DR:

- old-generation devices cannot resume ordinary writes or consume old grants;
- on observing the newer generation they remain/become stale read-only;
- they require reinitialization into the current generation before later write eligibility/normal target selection.

There is no automatic merge of independently written old-generation data.

**Evidence:** replacement-PC end-to-end DR; safe-candidate choice; corrupted/incomplete/snapshot-without-grant rejection; concurrent recovery contenders; 201/duplicate/starter/retry/crash boundaries; generation advancement; old-generation stale/read-only; reinitialization; FR/zh-CN quarantine/data-loss UI.

## AC-STO-003 / AC-STO-004 / AC-STO-007 / AC-STO-010

These existing criteria remain controlling without semantic relaxation:

- source durable relinquishment must precede target-releasing grant;
- only the exact normal target may acquire after complete validation and durable local acquisition;
- newest three complete GitHub normal-handoff units are retained;
- non-authoritative, pending-transfer and stale-generation business writes remain centrally blocked.

Self-join and DR must be implemented through those safety boundaries rather than around them.

## Acceptance result rule

M07 may not be marked Passed until automated evidence and the project-owner Windows/WPF multi-device checklist demonstrate all amended behavior on the accepted exact production head, including the fresh-replacement-PC DR scenario. Preparation documents or feasibility-only tool code are not sufficient acceptance evidence.
