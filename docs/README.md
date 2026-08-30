# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01 Passed/merged; M02 Passed/merged; M03 active on PR #5 and remains Partial pending the approved filtered bulk activation/deactivation extension plus remaining operator acceptance.**

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
- `v1-specification-freeze.md` — Approved — Phase 5 baseline, amended through 2026-08-30.
- final repo-wide consistency review — complete.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered implementation milestones and gates, amended 2026-08-30 to assign AC-CAT-013 to M03.
- `implementation-status.md` — living acceptance/milestone traceability record.
- `implementation/milestone-01-foundation.md` — detailed M01 task contract; M01 implementation is complete.
- `implementation/milestone-02-github-transport-revalidation.md` — M02 GitHub Release Asset transport/revalidation contract; M02 is Passed and merged through PR #3.
- `implementation/milestone-03-catalogue-settings.md` — original detailed M03 Catalogue/BusinessSettings contract; M03 is authorized and active on PR #5.
- `implementation/milestone-03-filtered-bulk-activation-extension.md` — detailed authorized M03 extension implementing the approved 2026-08-30 filtered bulk activation/deactivation amendment.
- `implementation/milestone-03-filtered-bulk-activation-authorization.md` — durable authorization for that M03 extension.

Normal target-directed handoff uses the configured dedicated private GitHub repository (`sushi81-pos-handoff` conceptually), one long-lived Release and immutable snapshot/grant assets. OneDrive references in this documentation remain only for approved recovery/archive or historical M02 evidence.

Phase 6 approval does not authorize all milestones at once. Codex must implement only the milestone/task explicitly assigned in the current durable handoff.

## Decision records

Approved materially constraining decisions live under `decisions/`.

Those records supplement the baseline documents. Post-freeze amendments are folded into affected baselines or accompanied by explicit approved acceptance amendments so implementation does not need to choose between contradictory requirements.

The 2026-08-30 filtered bulk activation/deactivation amendment is recorded in `decisions/filtered-catalogue-bulk-activation.md`.

## Implementation authority rule

Codex and other implementation agents must use the approved GitHub specification as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md` and the detailed file for the explicitly assigned milestone/task under `implementation/`, in addition to `AGENTS.md` and the frozen/amended specification sources referenced by that task.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, the affected path must be surfaced for specification resolution rather than silently guessed.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**
