# M07 implementation authorization — pairing, target-directed handoff and disaster recovery

**Status:** Authorized for implementation  
**Authorized:** 2026-09-07  
**Project owner statement:** “批准 M07 正式实施。按照当前已批准的规格和实施合同，开始 M07 implementation。”  
**Authorized preparation head:** `e8a9992ba04a21ac4854492bd3bcb7a8ce6e4b96`  
**Execution gate:** CLOSED at authorization-record creation; may open only after branch/PR/mailbox/handoff verification.

## 1. Authorization scope

The project owner explicitly authorizes production implementation of M07 — Pairing, target-directed formal handoff and disaster recovery — under the current Approved specification and prepared implementation contract.

Controlling implementation documents include:

- `docs/implementation/milestone-07-pairing-handoff-disaster-recovery.md`;
- `docs/implementation/milestone-07-parallel-execution-plan.md`;
- `docs/implementation/milestone-07-worklog.md`;
- `docs/implementation/milestone-07-final-manual-acceptance.md`;
- `docs/decisions/m07-self-join-disaster-recovery.md`;
- `docs/acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`;
- all earlier Approved baseline/decision documents not superseded by later Approved wording.

## 2. Owner-approved material safety semantics

Implementation must preserve these final V1 decisions:

1. a fresh device may self-join an existing lineage without approval from the former/current authoritative device, but self-join/membership never grants write authority;
2. ordinary valid authoritative operation remains local-first/offline-capable, while exceptional Disaster Recovery requires explicit old-device stop/quarantine confirmation and online single-winner next-generation activation;
3. Disaster Recovery uses the freshest validated safe candidate: either a completed GitHub handoff snapshot with its valid matching grant or a validated OneDrive recovery checkpoint; a snapshot without its valid matching grant is never eligible.

Codex must not reinterpret or reopen these as product questions unless implementation evidence proves the Approved model impossible. In that case it must fail closed and surface the blocker for architecture/specification review.

## 3. Governance authorization

This record authorizes the governance controller to:

- mark M07 In progress;
- create/use the dedicated M07 implementation branch;
- create the M07 implementation PR;
- update GitHub issue #4 to point only to that branch/PR;
- publish one complete executable `CODEX_HANDOFF_READY` record on that PR;
- verify authorization/base/head/branch/PR/mailbox consistency;
- reopen issue #4 only after those prerequisites are valid so Codex may execute M07.

This authorization does **not** authorize merge.

## 4. Non-authorized actions

Even after this implementation authorization:

- M08 and later milestones must not start;
- M07 must not be marked Passed without required automated evidence and project-owner exact-head Windows/WPF multi-device acceptance;
- the M07 PR must not be merged without a separate explicit project-owner merge approval;
- material product/business/authority/recovery/data-loss changes still require project-owner approval.

## 5. Execution result boundary

Codex may implement only after the mailbox/gate sequence is complete. ChatGPT remains specification/review/governance controller. Codex must record commits/tests/evidence in the M07 worklog and stop at any defined architecture blocker, especially inability to prove the DR single-winner activation primitive.
