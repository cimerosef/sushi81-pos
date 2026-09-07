# M06 controlled implementation authorization

**Status:** Approved authorization  
**Approved by:** project owner  
**Approval date:** 2026-09-06  
**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Implementation branch:** `codex/m06-local-recovery-read-only`  
**Base:** `main` at M05 merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`  
**Implementation contract:** `milestone-06-local-recovery-read-only-enforcement.md`  
**POST_TASK_POWER_ACTION:** `NONE`

## Authorization

On 2026-09-06 the project owner explicitly instructed ChatGPT to complete all remaining M06 preparation work without another intermediate stop and, once the executable handoff was ready, to create the M06 mailbox, publish the Codex implementation task and open the Codex execution gate.

This record therefore authorizes controlled M06 implementation after the repository's closed-gate sequencing is completed.

Production implementation may begin only when all are true:

1. the M06 branch exists;
2. the active M06 implementation PR mailbox exists;
3. issue #4 points unambiguously to that M06 PR/branch/contract;
4. a unique top-level `CODEX_HANDOFF_READY: M06-IMPLEMENT-01` record is published on that PR;
5. issue #4 is reopened only after steps 1–4 are complete.

The preparation/branch/PR/pointer/handoff sequence must be completed while issue #4 is CLOSED. Reopening issue #4 is the final activation step, not a prerequisite for preparation.

## Scope authority

Codex may implement only the behavior required by:

- the frozen-and-amended V1 specification;
- `milestone-06-local-recovery-read-only-enforcement.md`;
- inherited implementation governance under `docs/implementation/`;
- the approved target-directed authority semantics only insofar as M06 must persist/enforce local read-only states safely.

M06 does not authorize M07 pairing, real target-directed handoff, GitHub snapshot/grant transfer, target acquisition, cloud recovery checkpoints, Disaster Recovery/generation advancement, or M08–M13 work.

## Acceptance authority

M06 owns implementation/evidence for:

- `AC-STO-006`;
- `AC-STO-010`;
- the M06 partial contribution to `AC-PROD-002`;
- supporting failure evidence for `AC-NFR-004`.

Later milestones retain their frozen owners and cross-check responsibilities.

## Manual acceptance gate

Codex must prepare the exact reviewed Windows self-contained build and automated evidence but must not mark the project-owner Windows/WPF checklist Passed until the owner actually executes it.

## Merge gate

The M06 implementation PR must remain open/unmerged after Codex completion and after any subsequent remediation unless the project owner explicitly approves merge.

A `CODEX_DONE` record is evidence of task completion only. It never authorizes merge and never authorizes M07.
