# M07 self-service pairing and disaster recovery

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Scope:** device joining/pairing eligibility, exceptional Disaster Recovery fencing, Disaster Recovery source selection  
**Affected documents:** `product-requirements.md`, `architecture.md`, `storage-strategy.md`, `acceptance-criteria.md`, `v1-specification-freeze.md`, `implementation-plan.md`, `implementation-status.md`, M07 implementation contract/acceptance records

## Context

M02 established the target-directed normal authority-transfer model and GitHub Release Asset transport. M06 established fail-closed local authority/read-only enforcement and local recovery. During M07 pre-authorization review, three remaining material product/safety questions were identified.

The project owner approved the final rules below on 2026-09-07.

This record supersedes any M07 preparation proposal that required the current authoritative device to approve a new device merely so that the new installation could join the lineage. Such a requirement would create a permanent lockout when the former authoritative computer is irrecoverably lost.

## Decision 1 — self-service joining is allowed; joining never grants write authority

A new Sushi81 POS installation may join an existing Sushi81 POS lineage without approval from the currently authoritative device.

The joining device:

- creates and durably stores a new immutable opaque `device_id` plus a human-readable display name;
- selects/uses the configured existing shared Sushi81 POS system location and validates the lineage/shared metadata that are available;
- registers itself as a current-generation device through an immutable, generation-bound device-registration record;
- remains non-authoritative/read-only after joining;
- must never become writable merely because it joined, is the only device currently running, sees shared files, or observes that another device is offline.

Access to the configured Sushi81 POS shared storage/transport configuration is the V1 operational trust boundary for self-service joining. V1 does not add employee accounts, roles or a separate pairing-approval login system.

A joining device may have no usable current local `live.db` yet. This is valid: membership/identity establishment is distinct from read-only hydration and distinct from write-authority acquisition.

When a current authoritative device is available, it may produce a fresh read-only initialization seed for joined devices. That seed has no authority-grant semantics.

When the former authoritative device is genuinely lost and cannot produce such a seed or perform a normal handoff, a newly joined replacement computer may proceed directly into the explicit Disaster Recovery workflow described below.

A Windows reinstall that loses the local `device_id` is a new device and joins again with a new identity. After a generation advance, an old-generation device must be reinitialized/re-registered into the current generation before it is again eligible as a normal handoff target.

## Decision 2 — Disaster Recovery uses an explicit operational quarantine precondition

Normal authoritative operation remains local-first. Temporary Internet/GitHub/OneDrive loss must not make an otherwise healthy authoritative POS stop ordinary local business writes merely because a remote lease cannot be renewed.

Because a completely disconnected already-running old authoritative process cannot be remotely forced to observe a later generation change, Disaster Recovery is an exceptional operator-controlled process with an explicit safety precondition.

Before Disaster Recovery may activate a new writer, the operator must explicitly confirm that the previously authoritative/designated-target computer is genuinely unavailable and **will remain stopped/quarantined and will not be used for business writes again until it has been reinitialized into the new generation**.

Consequences:

- Disaster Recovery is not a convenience takeover path.
- Disaster Recovery itself requires online remote coordination and cannot complete offline.
- A recovery candidate must obtain a server-acknowledged, immutable next-generation activation with a deterministic single-winner/create-once property before local write authority is enabled.
- M07 must prove the selected GitHub coordination primitive with deterministic automated race/retry tests and a disposable real private-repository concurrency drill before relying on it for production recovery. If the primitive cannot prove one winner, implementation stops for architecture amendment rather than weakening the guarantee.
- A crash after remote activation but before local activation must produce zero writable recovery devices; only the same recovery identity/device may resume that already-won activation.
- Returning old-generation devices are stale/read-only and must be reinitialized before they can write again.
- Product/help/acceptance wording must state truthfully that the software cannot physically revoke writes from an already-running disconnected old computer if the operator violates the quarantine precondition.

This preserves the approved local-first availability boundary while making the exceptional Disaster Recovery safety obligation explicit.

## Decision 3 — Disaster Recovery uses the freshest validated safe data candidate

Disaster Recovery is not limited to the newest OneDrive recovery-only checkpoint when a newer, fully completed and independently verifiable normal GitHub handoff unit exists.

For the current pre-recovery generation, an eligible recovery candidate is:

1. a complete GitHub handoff snapshot with its valid matching target-bound grant, because the grant proves the source crossed durable relinquishment for that exact snapshot; or
2. a complete validated OneDrive recovery-only checkpoint.

A GitHub snapshot without its valid matching grant is **never** an eligible Disaster Recovery source. It may have been uploaded before the source relinquished write authority and therefore cannot prove that it is a safe released data point.

M07 must attach a monotonic, durable business-data revision/change watermark to both eligible candidate types so freshness can be compared across GitHub handoffs and OneDrive checkpoints without relying on wall-clock timestamps. Timestamps remain operator information only, not the authority/freshness ordering primitive.

The Disaster Recovery UI must:

- enumerate only candidates that pass lineage/generation/protocol/size/hash/SQLite-integrity validation;
- identify candidate type, source device, revision/version and effective timestamp;
- select/recommend the freshest validated safe candidate by the durable revision ordering;
- display the potential data-loss window before confirmation;
- allow no candidate that is incomplete, contradictory, stale relative to known durable state, or otherwise unverified.

Whatever candidate is restored, Disaster Recovery creates a new generation. It is not ordinary acquisition of the old handoff target binding.

## Normal handoff remains unchanged

These decisions do not weaken or replace the already Approved target-directed normal handoff rules:

- only the current authoritative source decides a normal transfer;
- exactly one current-generation eligible target is selected;
- source durable relinquishment precedes any target-releasing grant;
- after relinquishment the source cannot resume ordinary writes or retarget;
- only the exact target may acquire;
- non-target devices do not claim/elect authority;
- genuine target loss after relinquishment uses explicit Disaster Recovery.

Self-service joining therefore means **self-service membership/identity**, never self-service ordinary takeover.

## Rationale

The approved behavior avoids two unsafe or unusable extremes:

- requiring a dead authoritative computer to approve its replacement would make the POS lineage permanently unrecoverable;
- allowing any newly joined computer to become writable automatically would reintroduce competitive authority acquisition and double-writer risk.

The chosen boundary is simple for the operator:

- a new computer may join by itself;
- joining alone is always read-only;
- normal write authority still moves by explicit target-directed handoff;
- if the old authoritative path is genuinely gone, the new computer may explicitly perform Disaster Recovery after confirming the old computer is quarantined;
- recovery restores the freshest fully validated safe data and advances generation.

This follows the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.

## Superseded preparation proposal

`docs/implementation/milestone-07-preauthorization-design-review.md` proposed an authoritative-device-approved pairing model as one option for owner review. That pairing-approval proposal is rejected/superseded by this Approved decision. The review document remains historical preparation evidence and must not be read as implementation authority where it conflicts with this record.
