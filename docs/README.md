# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M08 Passed/merged; M09 — Hiboutik paste-order fallback — Passed / ready for separate merge approval.**

M08 — Printing and reprinting — Passed project-owner Windows/WPF/physical-print acceptance and was merged through PR #14 to `main` at merge commit `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`. Accepted production candidate: `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`; final pass-record head: `5f8c92c29116e17a3365ecd8801f7e13f107269c`; final exact-head CI #675 succeeded.

M09 implementation, automated evidence and owner Windows/WPF manual acceptance are complete on accepted production candidate `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`, with accepted EXE/ZIP hashes recorded in `implementation/milestone-09-final-manual-acceptance.md`. The owner-approved 2026-09-14 amendment narrows paste input to the Hiboutik product-detail block, requires fail-safe unresolved-line operator handling, permits passive read-only Hiboutik source identification, and permits nullable read-only `source_total_ttc` as a reconciliation reference while keeping ordinary POS pricing authoritative.

The final M09 documentation/evidence closure is recorded on branch `codex/m09-hiboutik-paste-fallback` / PR #17. PR #17 remains OPEN / unmerged pending separate explicit project-owner merge approval; the closure documentation head must not be treated as a replacement production candidate. Issue #4 remains the sole Codex execution gate, and M10 and later milestones remain unauthorized.

The V1 Specification remains frozen-and-amended. The formal freeze record is `v1-specification-freeze.md`; the primary implementation acceptance contract is `acceptance-criteria.md` together with approved acceptance amendments; the approved implementation sequence is `implementation-plan.md`; the living current control state is `implementation-status.md`.

## V1 documentation baseline

### Phase 1 — Current system and product scope

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline.

### Phase 2 — Business model

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline, amended for filtered bulk activation/deactivation.

### Phase 3 — Core technical architecture

- `architecture.md` — Approved — Phase 3 baseline, including later approved handoff amendments.
- `data-model.md` — Approved — Phase 3 baseline, subject to later approved decision amendments where explicitly stated.
- `storage-strategy.md` — Approved — Phase 3 baseline, including later approved GitHub handoff and M07 self-join/DR decisions.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive disaster-recovery/archive behavior and annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline, amended 2026-09-14 for M09 product-block/operator-resolution/source-reference semantics.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

M08 owner-selected visual/identity detail is frozen in `decisions/m08-print-layout-and-receipt-identity.md` and the matching implementation-contract addendum.

### Phase 5 — V1 specification freeze and acceptance amendments

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved amendment adding AC-CAT-013.
- `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md` — Approved M07 amendment.
- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md` — Approved 2026-09-14 M09 acceptance amendment for AC-HIB-001 through AC-HIB-009 where stated.
- `v1-specification-freeze.md` — Approved — Phase 5 baseline, amended through approved post-freeze decisions.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered milestones and gates.
- `implementation-status.md` — living current acceptance/milestone control record.
- `implementation/agent-execution-contract.md` — main-agent/subagent execution governance.
- `implementation/interactive-quality-gate.md` — mandatory WPF/user-visible quality gate.
- `implementation/control-state-preservation.md` — interactive-state invariant.
- `implementation/post-task-power-policy.md` — explicit one-shot host power policy.

Historical completed milestone records remain under `implementation/` and in their original PRs:

- M01 — Passed/merged through PR #1;
- M02 — Passed/merged after target-directed GitHub transport revalidation;
- M03 — Passed/merged through PR #5;
- M04 — Passed/merged through PR #6;
- M05 — Passed/merged through PR #10;
- M06 — Passed/merged through PR #11;
- M07 — Passed/merged through PR #13;
- M08 — Passed/merged through PR #14.

Current M09 control package:

- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md` — owner-approved V1 amendment;
- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md` — amended AC-HIB acceptance contract;
- `paste-order-import.md` — consolidated amended operational specification;
- `implementation/milestone-09-preparation-readiness.md` — readiness/control record;
- `implementation/milestone-09-hiboutik-paste-fallback.md` — detailed authorized implementation contract;
- `implementation/milestone-09-final-manual-acceptance.md` — prepared owner Windows/WPF checklist;
- `implementation/milestone-09-authorization.md` — durable explicit project-owner implementation authorization;
- `samples/pasted-orders/hiboutik-product-block-synthetic.txt` — synthetic source-structure fixture only.

## Decision records

Approved materially constraining decisions live under `decisions/` and supplement/amend the baseline documents.

Relevant later decisions include:

- `decisions/filtered-catalogue-bulk-activation.md`;
- M04/M05 implementation clarification decisions;
- `decisions/target-directed-authority-handoff.md` and `decisions/github-handoff-transport.md`;
- M07 self-join/DR/recovery-ordering decisions;
- `decisions/m08-print-layout-and-receipt-identity.md`;
- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`.

Where a later Approved decision explicitly supersedes a narrow older clause, the later decision controls until the next complete consolidation pass. A genuine unresolved contradiction still requires implementation to stop rather than guess.

## Implementation authority rule

Codex and other implementation agents must use current GitHub specification/control records as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md`, the detailed explicitly assigned milestone files, `AGENTS.md`, and all frozen/amended specification/decision sources referenced by that milestone.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, stop the affected path for specification resolution rather than silently guessing.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

Phase 6 approval does not authorize all milestones at once. Issue #4 is the master execution gate; while CLOSED, Codex makes no project changes.
