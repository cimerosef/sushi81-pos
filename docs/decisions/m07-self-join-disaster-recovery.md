# M07 self-join and disaster-recovery safety model

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Owner approval:** Project owner explicitly approved the material product/safety choices in the M07 design discussion on 2026-09-07.  
**Affected documents:** `product-requirements.md`, `architecture.md`, `storage-strategy.md`, `acceptance-criteria.md`, `v1-specification-freeze.md`, M07 implementation/governance records.  
**Supersedes:** the pairing proposal in `docs/implementation/milestone-07-preauthorization-design-review.md` that required the current authoritative device to approve a new device. That proposal was never an Approved baseline rule.

## 1. Context

M07 must complete the production implementation of N-device membership, normal target-directed GitHub handoff, recovery-only OneDrive checkpoints and explicit Disaster Recovery.

Three product/safety questions could not safely be delegated to implementation:

1. whether a newly installed replacement computer may join an existing Sushi81 POS lineage when the old authoritative computer is permanently dead;
2. how Disaster Recovery can coexist truthfully with the Approved local-first rule that an already-authoritative computer may continue ordinary business writes during temporary network loss;
3. which cloud data may be used when Disaster Recovery is unavoidable.

The following decisions are final for V1.

## 2. Decision A — self-join is allowed and is not authority

### 2.1 New devices do not require approval from the old authoritative computer

A newly installed Sushi81 POS computer may **self-join an existing lineage** using the configured shared lineage/system information. Self-join must not require an approval action on the current or former authoritative computer.

This requirement exists specifically so the system remains recoverable when the former authoritative computer has suffered permanent hardware, disk or operating-system loss and can no longer approve anything.

A new installation creates:

- a new immutable random `device_id`;
- an operator-readable device display name, which may later be edited without changing `device_id`;
- local fail-closed authority state for that identity.

A Windows reinstall that loses the old `device_id` is a new device and joins again with a new identity.

### 2.2 Self-join never grants or releases write authority

Self-join establishes only device identity/membership for the current lineage/generation. It does **not**:

- release authority from another computer;
- make the joining computer authoritative;
- create a normal transfer grant;
- allow the joining computer to acquire a grant targeted to another device;
- elect a writer;
- permit a generic or competitive takeover.

If the existing authoritative computer is healthy, the newly joined computer remains non-authoritative/read-only until that authoritative source explicitly performs the Approved normal target-directed handoff to its exact `device_id`.

### 2.3 Self-join must also support replacement-PC Disaster Recovery

If the former authoritative/designated-target computer is genuinely unavailable and the normal handoff path cannot be completed, a newly installed computer may:

1. self-join the existing lineage;
2. remain non-authoritative/fail-closed;
3. enter the separate explicit Disaster Recovery workflow;
4. become writable only if the Disaster Recovery rules in this decision are fully satisfied.

A missing ordinary read-only seed snapshot must not create a dead end. A newly joined replacement computer may exist as **paired/uninitialized read-only** and still be allowed to start the DR workflow. It may not perform ordinary business writes until DR successfully completes.

### 2.4 Membership representation

For V1 implementation, current-generation membership should use immutable per-device registration evidence rather than one conflict-prone mutable paired-device-set file. Registration artifacts must be lineage/generation/device-bound, integrity-checked and must not themselves contain an authority grant.

The OneDrive `System` area is the Approved shared location for non-authority lineage/device membership metadata. It is not a distributed lock and it is not a normal handoff transport.

After a Disaster Recovery generation advance, an old-generation device is not automatically an eligible normal target. It must be reinitialized/join the current generation before it can later receive a normal handoff.

## 3. Decision B — Disaster Recovery is operationally fenced and exceptional

### 3.1 Preserve local-first ordinary operation

The current authoritative computer remains allowed to continue ordinary local POS business writes during temporary Internet/OneDrive/GitHub unavailability, consistent with AC-PROD-002.

V1 therefore does **not** introduce a renewable remote lease that would automatically expire ordinary write authority merely because Internet access is unavailable for a period of time.

### 3.2 DR has an explicit quarantine precondition

Disaster Recovery may be used only when the applicable authoritative/designated-target path is genuinely unavailable and cannot be completed normally.

Before DR activation, the operator must explicitly confirm that the prior authoritative/designated-target computer:

- is genuinely unavailable for the normal path; and
- is stopped, disconnected from operational use or otherwise quarantined; and
- will **not** be used for Sushi81 POS business writes again until it has been reinitialized into the new current generation.

The product must state this condition clearly in French and Simplified Chinese.

This is a real safety precondition, not cosmetic warning text.

### 3.3 Truthful safety boundary

Because ordinary authoritative operation is intentionally local-first, software on another computer cannot instantly revoke writes from an already-running old authoritative process that is disconnected and cannot observe new remote state.

Therefore V1 must not claim that remote generation metadata is a physical kill switch. If an operator violates the DR quarantine precondition and continues using a disconnected old writer, the software cannot guarantee prevention of conflicting writes on that isolated machine until it observes the newer generation.

The system must nevertheless fail closed whenever the stale computer later observes the newer generation or otherwise re-enters coordinated operation.

### 3.4 DR itself requires online single-winner activation

Disaster Recovery cannot complete offline.

Before any recovery device becomes locally authoritative in generation `G+1`, it must first obtain a server-acknowledged immutable **next-generation recovery activation** with a deterministic single-winner/create-once property in the configured dedicated private GitHub handoff repository.

The implementation must prove the selected GitHub primitive before broad DR implementation with:

- deterministic fake/concurrency tests; and
- a disposable real-private-repository race/retry drill using synthetic data only.

If the primitive cannot prove that concurrent recovery contenders converge to at most one accepted activation for the same lineage/next generation, M07 must stop for architecture amendment instead of weakening the invariant.

A crash after remote activation but before local authority activation yields zero writable recovery devices. Only the same recovery identity/device may resume that already-created activation.

### 3.5 Returning old-generation devices

After DR advances the lineage generation:

- old-generation local authority state is stale;
- old-generation normal grants cannot be consumed;
- an old device that observes the new generation becomes/read remains non-authoritative/read-only;
- it must be reinitialized into the current generation before it may later become a normal handoff target or writer.

There is no automatic merge of writes made independently in two generations.

## 4. Decision C — DR uses the freshest validated safe recovery candidate

Disaster Recovery is not limited to a file whose type is named `checkpoint`.

For the current pre-recovery generation, an eligible DR recovery candidate is either:

1. a complete validated GitHub normal-handoff snapshot **with its valid matching target-bound grant**; or
2. a complete validated OneDrive recovery-only checkpoint.

A GitHub handoff snapshot without its valid matching grant is **never** an eligible DR source. The snapshot may have been uploaded before the source durably relinquished authority and therefore cannot prove that it is the safely released state.

The DR workflow selects the freshest validated safe candidate using the protocol's durable ordering/high-water metadata, not filename timestamp alone. Candidate validation includes lineage, generation, identity/version metadata, byte size, SHA-256 and SQLite integrity/schema checks as applicable.

If valid candidates cannot be safely ordered from their durable protocol metadata, the application must not silently guess from wall-clock timestamps. It must fail closed to an explicit operator selection that displays the ambiguity and possible data-loss window.

Before confirmation, the UI displays enough information to understand the candidate, including at least:

- candidate type (completed handoff or recovery checkpoint);
- effective/source device where applicable;
- generation/version/change-watermark information where applicable;
- timestamp for operator orientation;
- warning about the possible data-loss window.

Whichever eligible candidate is selected, the action remains **Disaster Recovery**: it creates a new generation and does not convert the old target binding into a normal acquisition.

## 5. Normal handoff remains unchanged

This decision does not weaken the Approved normal target-directed protocol.

For normal authority movement:

- only the current authoritative source chooses exactly one eligible current-generation target;
- source durable relinquishment occurs before any target-releasing grant exists;
- after relinquishment the source cannot return to ordinary writes or retarget;
- only the exact target may acquire the normal grant;
- third devices remain read-only;
- inability to recover the designated target after relinquishment requires explicit DR.

`docs/decisions/target-directed-authority-handoff.md` and `docs/decisions/github-handoff-transport.md` remain controlling for normal transfer mechanics except where this decision explicitly clarifies self-join/DR semantics.

## 6. Storage/wording consequences

For V1:

- normal handoff snapshot/grant transport is GitHub Release Assets in the configured dedicated private handoff repository;
- OneDrive `System` is used for non-authority lineage/device membership metadata;
- OneDrive `DisasterRecovery` is used for recovery-only checkpoints;
- OneDrive `Archive` remains the annual archive location;
- any older wording that assigns normal handoff authority semantics to OneDrive `Handoff` is superseded and must not guide M07 implementation.

Product requirement NFR-004 is consequently understood as: approved **local recovery, GitHub target-directed normal handoff, OneDrive disaster recovery and annual archive** paths are implemented before production use.

## 7. Rationale

The decision preserves all three required operational properties together:

1. a healthy authoritative computer can keep the shop operating during a temporary network failure;
2. a newly purchased replacement computer is not permanently locked out merely because the previous authority computer is dead;
3. a computer cannot become writable merely by joining the system — authority still moves only through exact normal handoff or explicit generation-advancing DR.

The design continues to prioritize:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 8. Governance consequence

Approval of this decision authorizes M07 **implementation preparation only** in the current governance state. It does not itself create a Codex execution authorization, implementation branch/PR or execution-gate opening.

A separate explicit project-owner implementation approval is still required before the M07 authorization/handoff/gate sequence may begin.
