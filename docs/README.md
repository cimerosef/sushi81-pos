# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M04 Passed/merged; M05 implementation and project-owner Windows/WPF acceptance are Passed under PR #10, which remains open/unmerged pending explicit merge approval.**

M04 merged through PR #6 at merge commit `ab218263bd4eee9c1be203d36acc552988cef43a` after complete Windows/WPF manual acceptance. M05 implementation and project-owner Windows/WPF acceptance are Passed under the dedicated branch `codex/m05-lifecycle-payments-search-dashboard`; PR #10 remains open/unmerged pending explicit merge approval and M06+ remains unauthorized.

The V1 Specification remains frozen-and-amended. The formal freeze record is `v1-specification-freeze.md`; the primary implementation acceptance contract is `acceptance-criteria.md` together with approved acceptance amendments; the approved implementation sequence is `implementation-plan.md`.

## V1 documentation baseline

### Phase 1 — Current system and product scope

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline.

### Phase 2 — Business model

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline, amended 2026-08-30 for filtered bulk activation/deactivation.

### Phase 3 — Core technical architecture

- `architecture.md` — Approved — Phase 3 baseline.
- `data-model.md` — Approved — Phase 3 baseline.
- `storage-strategy.md` — Approved — Phase 3 baseline.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive disaster-recovery/archive behavior and annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

### Phase 5 — V1 specification freeze

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved 2026-08-30 V1 acceptance amendment adding AC-CAT-013 until the next consolidated acceptance rewrite.
- `v1-specification-freeze.md` — Approved — Phase 5 baseline, amended through approved post-freeze decisions.
- final repo-wide consistency review — complete.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered implementation milestones and gates.
- `implementation-status.md` — living acceptance/milestone traceability record; historical sections may describe the state at the time evidence was recorded.
- `implementation/milestone-01-foundation.md` — M01 contract; Passed/merged.
- `implementation/milestone-02-github-transport-revalidation.md` — M02 contract; Passed/merged.
- `implementation/milestone-03-catalogue-settings.md` and related extension/worklog — M03 historical implementation records; Passed/merged through PR #5.
- `implementation/milestone-04-order-entry.md`, authorization/worklog/final manual acceptance — M04 historical implementation records; Passed/merged through PR #6.
- `implementation/milestone-05-lifecycle-payments-search-dashboard.md` — current approved M05 implementation contract.
- `implementation/milestone-05-authorization.md` — durable project-owner authorization for controlled M05 execution.
- `implementation/milestone-05-final-manual-acceptance.md` — final project-owner Windows/WPF acceptance record for M05.

Normal target-directed handoff uses the configured dedicated private GitHub repository (`sushi81-pos-handoff` conceptually), one long-lived Release and immutable snapshot/grant assets. OneDrive references remain only for approved recovery/archive or historical M02 evidence.

Phase 6 approval does not authorize all milestones at once. Codex must implement only the milestone/task explicitly assigned in the current durable handoff.

## Decision records

Approved materially constraining decisions live under `decisions/`.

Those records supplement the baseline documents. Post-freeze amendments are folded into affected baselines or accompanied by explicit approved decision/acceptance records so implementation does not need to choose between contradictory requirements.

Relevant recent amendments include:

- `decisions/filtered-catalogue-bulk-activation.md` — 2026-08-30 M03 catalogue amendment;
- `decisions/m04-order-entry-pricing-clarifications.md` and `decisions/m04-order-entry-operator-ergonomics-amendment.md` — approved M04 amendments;
- `decisions/m05-lifecycle-payment-modification-clarifications.md` — 2026-09-02 approved M05 human-reference, snapshot-preservation and cumulative-payment clarification.

## Implementation authority rule

Codex and other implementation agents must use the approved GitHub specification as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md` and the detailed file for the explicitly assigned milestone/task under `implementation/`, in addition to `AGENTS.md` and the frozen/amended specification sources referenced by that task.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, the affected path must be surfaced for specification resolution rather than silently guessed.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**
