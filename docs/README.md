# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M07 Passed/merged; M08 — Printing and reprinting — is in Preparation only and is NOT implementation-authorized.**

M07 — Pairing, target-directed formal handoff and disaster recovery — Passed project-owner Windows/WPF multi-device acceptance and was merged through PR #13 to `main` at merge commit `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`. Accepted production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`; final closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`; exact-head CI #631 / run `34698627867` succeeded; Release tests 541/541 Passed.

M08 preparation documents are present under `implementation/`. The owner-approved print-layout/receipt-identity decision is `decisions/m08-print-layout-and-receipt-identity.md`. M08 implementation remains unauthorized: Issue #4 is CLOSED, no M08 implementation branch/PR exists, and no executable handoff exists.

M09 and later milestones are not started/authorized.

The V1 Specification remains frozen-and-amended. The formal freeze record is `v1-specification-freeze.md`; the primary implementation acceptance contract is `acceptance-criteria.md` together with approved acceptance amendments; the approved implementation sequence is `implementation-plan.md`.

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
- `data-model.md` — Approved — Phase 3 baseline.
- `storage-strategy.md` — Approved — Phase 3 baseline, including later approved GitHub handoff and M07 self-join/DR decisions.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive disaster-recovery/archive behavior and annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

M08 owner-selected visual/identity detail is frozen in `decisions/m08-print-layout-and-receipt-identity.md` and the matching implementation-contract addendum.

### Phase 5 — V1 specification freeze

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved amendment adding AC-CAT-013.
- `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md` — Approved M07 amendment.
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
- M07 — Passed/merged through PR #13.

Current M08 preparation package:

- `implementation/milestone-08-printing-reprinting.md` — detailed base contract, non-executable until authorization;
- `implementation/milestone-08-contract-addendum-print-layout-identity.md` — controlling owner layout/identity addendum;
- `implementation/milestone-08-preparation-readiness.md` — readiness/control checklist;
- `implementation/milestone-08-authorization.md` — currently **NOT AUTHORIZED**;
- `implementation/milestone-08-worklog.md` — preparation/evidence log;
- `implementation/milestone-08-final-manual-acceptance.md` — prepared owner Windows/WPF/real-print checklist;
- `decisions/m08-print-layout-and-receipt-identity.md` — owner-approved visual/receipt-identity decision.

## Decision records

Approved materially constraining decisions live under `decisions/` and supplement the baseline documents.

Relevant later decisions include:

- `decisions/filtered-catalogue-bulk-activation.md`;
- M04/M05 implementation clarification decisions;
- `decisions/target-directed-authority-handoff.md` and `decisions/github-handoff-transport.md`;
- M07 self-join/DR/recovery-ordering decisions;
- `decisions/m08-print-layout-and-receipt-identity.md` — owner-selected kitchen/customer ticket visual targets plus customer receipt business identity and storage semantics.

## Implementation authority rule

Codex and other implementation agents must use current GitHub specification/control records as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md`, the detailed explicitly assigned milestone files, `AGENTS.md`, and all frozen/amended specification/decision sources referenced by that milestone.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, stop the affected path for specification resolution rather than silently guessing.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

Phase 6 approval does not authorize all milestones at once. Issue #4 is the master execution gate; while CLOSED, Codex makes no project changes.
